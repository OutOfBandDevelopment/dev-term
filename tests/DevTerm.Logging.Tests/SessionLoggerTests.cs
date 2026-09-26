using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Logging.Tests;

/// <summary>
/// The recorder tap over a real <see cref="Session"/> (real read loop, real fault handling) and a
/// fake transport: what gets recorded, in what order, with which sequence numbers and timestamps.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class SessionLoggerTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    private string _path = null!;

    [TestInitialize]
    public void CreatePath() => _path = Path.Combine(Path.GetTempPath(), $"devterm-logger-{Guid.NewGuid():N}.jsonl");

    [TestCleanup]
    public void DeletePath()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static SessionLogHeader Header() => new() { Created = DateTimeOffset.MinValue, Connection = "loopback://", Presenters = ["ascii"] };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting for the logger.");
            }

            await Task.Delay(10);
        }
    }

    private static string Shape(SessionLog log) =>
        string.Join(",", log.Records.Select(r => SessionLogFormat.TypeName(r.Kind)));

    [TestMethod]
    public async Task RecordsOpenTxRxAndClose_InOrder_WithSequenceNumbersAndClockTimestamps()
    {
        var clock = new ManualTimeProvider();
        var transport = new FakeTransport();
        await using var session = new Session(transport, new Pipeline([]));
        using (var logger = SessionLogger.Start(_path, Header(), clock))
        {
            logger.Attach(session, "loopback://");

            clock.Advance(TimeSpan.FromSeconds(1));
            await session.OpenAsync(TestContext.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(1));
            await session.SendAsync("ID?\r"u8.ToArray(), TestContext.CancellationToken);
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await transport.PushIncomingAsync([0x00, 0xFF, (byte)'A']);
            await WaitForAsync(() => logger.RecordCount == 4);
            clock.Advance(TimeSpan.FromSeconds(1));
            await session.CloseAsync(TestContext.CancellationToken);
            Assert.AreEqual(5, logger.RecordCount);
        }

        var log = SessionLog.Load(_path);
        Assert.AreEqual("session,open,tx,rx,close", Shape(log));
        CollectionAssert.AreEqual(new long?[] { 1, 2, 3, 4, 5 }, log.Records.Select(r => r.Sequence).ToArray());

        var start = new ManualTimeProvider().GetUtcNow();
        Assert.AreEqual(start, log.Header.Created, "The header's Created comes from the logger's clock.");
        CollectionAssert.AreEqual(
            new[] { 0.0, 1.0, 2.0, 2.25, 3.25 },
            log.Records.Select(r => (r.Timestamp - start).TotalSeconds).ToArray());

        Assert.AreEqual("closed", log.Records[0].State);
        CollectionAssert.AreEqual("ID?\r"u8.ToArray(), log.Records[2].Data.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xFF, (byte)'A' }, log.Records[3].Data.ToArray());
    }

    [TestMethod]
    public async Task AReadFailure_IsRecordedAsADisconnectWithTheError()
    {
        var transport = new FakeTransport();
        await using var session = new Session(transport, new Pipeline([]));
        using var logger = SessionLogger.Start(_path, Header());
        logger.Attach(session, "loopback://");
        await session.OpenAsync(TestContext.CancellationToken);

        await transport.FailReadAsync(new IOException("cable unplugged"));
        await WaitForAsync(() => logger.RecordCount == 3);

        var log = SessionLog.Load(_path);
        Assert.AreEqual("session,open,disconnect", Shape(log));
        Assert.AreEqual("cable unplugged", log.Records[2].Text);
    }

    [TestMethod]
    public async Task ACleanHangUp_IsRecordedAsADisconnectWithNoError()
    {
        var transport = new FakeTransport();
        await using var session = new Session(transport, new Pipeline([]));
        using var logger = SessionLogger.Start(_path, Header());
        logger.Attach(session, "loopback://");
        await session.OpenAsync(TestContext.CancellationToken);

        await transport.HangUpAsync();
        await WaitForAsync(() => logger.RecordCount == 3);

        var log = SessionLog.Load(_path);
        Assert.AreEqual("session,open,disconnect", Shape(log));
        Assert.IsNull(log.Records[2].Text);
    }

    [TestMethod]
    public async Task AFailedSend_IsRecordedAsTheAttemptedTxThenADisconnect()
    {
        var transport = new FakeTransport();
        await using var session = new Session(transport, new Pipeline([]));
        using var logger = SessionLogger.Start(_path, Header());
        logger.Attach(session, "loopback://");
        await session.OpenAsync(TestContext.CancellationToken);

        transport.FailNextWrite(new IOException("write failed"));
        await Assert.ThrowsExactlyAsync<IOException>(() => session.SendAsync(new byte[] { 1 }, TestContext.CancellationToken));

        var log = SessionLog.Load(_path);
        Assert.AreEqual("session,open,tx,disconnect", Shape(log));
        Assert.AreEqual("write failed", log.Records[3].Text);
    }

    [TestMethod]
    public async Task AttachingToAnAlreadyOpenSession_RecordsItsStateAsOpen()
    {
        var transport = new FakeTransport();
        await using var session = new Session(transport, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);

        using (var logger = SessionLogger.Start(_path, Header()))
        {
            logger.Attach(session, "tcp://192.168.0.107:23", "tek2230");
        }

        var record = SessionLog.Load(_path).Records.Single();
        Assert.AreEqual(SessionLogRecordKind.Session, record.Kind);
        Assert.AreEqual("open", record.State);
        Assert.AreEqual("tcp://192.168.0.107:23", record.Connection);
        Assert.AreEqual("tek2230", record.Profile);
    }

    [TestMethod]
    public async Task FollowingAProfileSwitch_ContinuesTheSameLog_AndStopsRecordingTheOldSession()
    {
        var oldTransport = new FakeTransport();
        var newTransport = new FakeTransport();
        await using var oldSession = new Session(oldTransport, new Pipeline([]));
        await using var newSession = new Session(newTransport, new Pipeline([]));

        using (var logger = SessionLogger.Start(_path, Header()))
        {
            logger.Attach(oldSession, "tcp://a:23");
            await oldSession.OpenAsync(TestContext.CancellationToken);

            logger.Attach(newSession, "tcp://b:23");
            await oldSession.SendAsync(new byte[] { 9 }, TestContext.CancellationToken);
            await oldSession.CloseAsync(TestContext.CancellationToken);
            await newSession.OpenAsync(TestContext.CancellationToken);
            await newSession.SendAsync(new byte[] { 7 }, TestContext.CancellationToken);
        }

        var log = SessionLog.Load(_path);
        Assert.AreEqual("session,open,session,open,tx", Shape(log));
        Assert.AreEqual("tcp://b:23", log.Records[2].Connection);
        CollectionAssert.AreEqual(new byte[] { 7 }, log.Records[4].Data.ToArray());
        CollectionAssert.AreEqual(new long?[] { 1, 2, 3, 4, 5 }, log.Records.Select(r => r.Sequence).ToArray());
    }

    [TestMethod]
    public async Task AfterStopping_NothingMoreIsRecorded_AndTheSessionCarriesOn()
    {
        var transport = new FakeTransport();
        await using var session = new Session(transport, new Pipeline([]));
        var logger = SessionLogger.Start(_path, Header());
        logger.Attach(session, "loopback://");
        await session.OpenAsync(TestContext.CancellationToken);

        logger.Dispose();
        await session.SendAsync(new byte[] { 1 }, TestContext.CancellationToken);

        Assert.IsFalse(logger.IsActive);
        Assert.AreEqual("session,open", Shape(SessionLog.Load(_path)));
        Assert.HasCount(1, transport.WrittenPayloads);
    }

    [TestMethod]
    public async Task Logging_DoesNotChangeWhatThePresentersRender()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        await using var session = new Session(transport, new Pipeline([presenter]));
        var outputs = new List<string>();
        session.Output += (_, o) =>
        {
            lock (outputs)
            {
                outputs.Add(o.Text);
            }
        };

        using var logger = SessionLogger.Start(_path, Header());
        logger.Attach(session, "loopback://");
        await session.OpenAsync(TestContext.CancellationToken);
        await transport.PushIncomingAsync("hel"u8.ToArray());
        await WaitForAsync(() => logger.RecordCount == 3);
        await transport.PushIncomingAsync("lo\rworld\r"u8.ToArray());
        await WaitForAsync(() =>
        {
            lock (outputs)
            {
                return outputs.Count == 2;
            }
        });

        CollectionAssert.AreEqual(new[] { "hello", "world" }, outputs);
    }

    [TestMethod]
    public async Task AnObserverThatThrows_NeverBreaksTheConnection()
    {
        var transport = new FakeTransport();
        await using var session = new Session(transport, new Pipeline([]));
        using var registration = session.AddObserver(new ThrowingObserver());

        await session.OpenAsync(TestContext.CancellationToken);
        await session.SendAsync(new byte[] { 1 }, TestContext.CancellationToken);

        Assert.AreEqual(Core.Transports.ConnectionState.Open, session.State);
        Assert.HasCount(1, transport.WrittenPayloads);
    }

    private sealed class ThrowingObserver : ISessionObserver
    {
        public void OnOpened() => throw new InvalidOperationException("disk full");

        public void OnReceived(System.Buffers.ReadOnlySequence<byte> data) => throw new InvalidOperationException("disk full");

        public void OnSent(ReadOnlyMemory<byte> data) => throw new InvalidOperationException("disk full");

        public void OnClosed(bool requested, Exception? error) => throw new InvalidOperationException("disk full");
    }

    public required TestContext TestContext { get; set; }
}
