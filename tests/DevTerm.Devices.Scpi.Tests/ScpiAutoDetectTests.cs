using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Devices.Scpi.Tests;

/// <summary>
/// The shared SCPI auto-detect (TUI and WPF) through a real <see cref="Session"/> and
/// <see cref="ScpiReplyPresenter"/>: a matched, an unrecognized, and a missing <c>*IDN?</c> reply,
/// each with the configured timeout and a user-facing description.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Scpi)]
[TestClass]
public sealed class ScpiAutoDetectTests
{
    public required TestContext TestContext { get; set; }

    // A device that answers *IDN? with idnReply (or never answers, when null).
    private async Task<(Session Session, ScpiReplyPresenter Presenter)> OpenAsync(string? idnReply)
    {
        var pipe = new Pipe();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        transport.Setup(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ReadOnlyMemory<byte> _, CancellationToken _) =>
            {
                if (idnReply is not null)
                {
                    await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes(idnReply + "\n"), CancellationToken.None);
                }
            });

        var presenter = new ScpiReplyPresenter();
        var session = new Session(transport.Object, new Pipeline([presenter]));
        await session.OpenAsync(TestContext.CancellationToken);
        return (session, presenter);
    }

    [TestMethod]
    public async Task AKnownInstrument_IsMatched()
    {
        var (session, presenter) = await OpenAsync("HEWLETT-PACKARD,34401A,0,11-5-2");
        await using var _ = session;

        var result = await ScpiAutoDetect.DetectAsync(session, presenter, TimeSpan.FromSeconds(5));

        Assert.AreEqual(ScpiAutoDetectOutcome.Matched, result.Outcome);
        StringAssert.Contains(result.Profile!.Name, "34401A");
        StringAssert.StartsWith(result.Describe(TimeSpan.FromSeconds(5)), "Detected ");
    }

    [TestMethod]
    public async Task AnUnknownInstrument_IsUnrecognized_WithItsReply()
    {
        var (session, presenter) = await OpenAsync("ACME,WIDGET-9000,1,0.1");
        await using var _ = session;

        var result = await ScpiAutoDetect.DetectAsync(session, presenter, TimeSpan.FromSeconds(5));

        Assert.AreEqual(ScpiAutoDetectOutcome.Unrecognized, result.Outcome);
        Assert.IsNull(result.Profile);
        StringAssert.Contains(result.Describe(TimeSpan.FromSeconds(5)), "ACME,WIDGET-9000");
    }

    [TestMethod]
    public async Task NoReply_TimesOutAfterTheConfiguredWait_NotAFixedThreeSeconds()
    {
        var (session, presenter) = await OpenAsync(idnReply: null);
        await using var _ = session;
        var timeout = TimeSpan.FromMilliseconds(250);

        var started = DateTime.UtcNow;
        var result = await ScpiAutoDetect.DetectAsync(session, presenter, timeout);
        var elapsed = DateTime.UtcNow - started;

        Assert.AreEqual(ScpiAutoDetectOutcome.NoReply, result.Outcome);
        Assert.IsLessThan(TimeSpan.FromSeconds(2), elapsed, "Should honor the 250 ms timeout, not wait 3 s.");
        Assert.AreEqual("No *IDN? reply within 0.3 s — opening the Generic panel.", result.Describe(timeout));
    }

    [TestMethod]
    public void ProgressMessage_SaysWhatItsWaitingForAndHowLong() =>
        Assert.AreEqual("Auto-detecting the SCPI instrument: sent *IDN?, waiting up to 8 s…", ScpiAutoDetect.ProgressMessage(TimeSpan.FromSeconds(8)));

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task DetectAsync_AfterNoReply_ALaterQueryStillCorrelatesCorrectly()
    {
        // Regression test for bug 006: DetectAsync's own pending id ("scpiAutoDetect.reply") used
        // to stay queued forever after a NoReply timeout, so the *next* query's reply landed on
        // that stale id instead of its own. See docs/bugs/fixed/006-reply-queue-desync.md.
        var (session, presenter) = await OpenAsync(idnReply: null);
        await using var _ = session;

        var result = await ScpiAutoDetect.DetectAsync(session, presenter, TimeSpan.FromMilliseconds(250));
        Assert.AreEqual(ScpiAutoDetectOutcome.NoReply, result.Outcome);

        IReadOnlyDictionary<string, string>? received = null;
        presenter.ValuesChanged += (_, values) => received = values;
        presenter.QuerySent("next.reply");
        presenter.Render(new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes("ACTUAL_REPLY\n")));

        Assert.IsNotNull(received);
        Assert.AreEqual("ACTUAL_REPLY", received!["next.reply"]);
    }

    [TestMethod]
    public async Task NoScpiPresenter_SendsNothing()
    {
        var (session, _) = await OpenAsync("HEWLETT-PACKARD,34401A,0,11-5-2");
        await using var s = session;

        var result = await ScpiAutoDetect.DetectAsync(session, presenter: null, TimeSpan.FromSeconds(1));

        Assert.AreEqual(ScpiAutoDetectOutcome.NoPresenter, result.Outcome);
    }
}
