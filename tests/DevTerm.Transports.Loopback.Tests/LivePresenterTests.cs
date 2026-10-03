using System.Buffers;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Loopback.Tests;

/// <summary>
/// Presenters that originate traffic, and presenters added or removed on a live session without
/// reconnecting, driven over the in-process loopback device (no hardware).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Loopback)]
[TestClass]
public sealed class LivePresenterTests
{
    // Buffers into lines; on a line equal to _trigger, originates _command (a simulation driver).
    private sealed class DriverPresenter(string name, string trigger, string command) : IOriginatingPresenter
    {
        private readonly StringBuilder _line = new();

        public string Name { get; } = name;

        public event EventHandler<ReadOnlyMemory<byte>>? Originated;

        public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
        {
            var lines = new List<string>();
            foreach (var ch in Encoding.ASCII.GetString(data.ToArray()))
            {
                if (ch != '\n')
                {
                    if (ch != '\r')
                    {
                        _line.Append(ch);
                    }

                    continue;
                }

                var text = _line.ToString();
                _line.Clear();
                lines.Add(text);
                if (text == trigger)
                {
                    Originated?.Invoke(this, Encoding.ASCII.GetBytes(command + "\r\n"));
                }
            }

            return lines;
        }
    }

    private static Session CreateSession(params IPresenter[] presenters) =>
        new(new LoopbackTransport(Options.Create(new LoopbackTransportOptions())), new Pipeline(presenters));

    private static async Task<List<string>> WaitForAsync(List<string> seen, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (seen)
            {
                if (seen.Count >= count)
                {
                    return [.. seen];
                }
            }

            await Task.Delay(10);
        }

        lock (seen)
        {
            return [.. seen];
        }
    }

    [TestMethod]
    public async Task OriginatingPresenter_AddedLive_SendsWhatItOriginates()
    {
        await using var session = CreateSession();
        var seen = new List<string>();
        session.Output += (_, o) => { lock (seen) { seen.Add(o.Text); } };
        await session.OpenAsync(TestContext.CancellationToken);

        // Added after the connection is already open: no reconnect.
        session.AddPresenter(new DriverPresenter("driver", "From Loopback test", "MEAS?"));
        await session.SendAsync("hello\r\n"u8.ToArray(), TestContext.CancellationToken);

        var lines = await WaitForAsync(seen, 2);
        Assert.AreEqual("From Loopback test", lines[0]);
        StringAssert.StartsWith(lines[1], "A=");
    }

    [TestMethod]
    public async Task OriginatingPresenter_Removed_StopsOriginating()
    {
        await using var session = CreateSession();
        var seen = new List<string>();
        session.Output += (_, o) => { lock (seen) { seen.Add(o.Text); } };
        await session.OpenAsync(TestContext.CancellationToken);
        var driver = new DriverPresenter("driver", "From Loopback test", "MEAS?");
        session.AddPresenter(driver);
        session.RemovePresenter(driver);

        await session.SendAsync("hello\r\n"u8.ToArray(), TestContext.CancellationToken);
        await Task.Delay(200, TestContext.CancellationToken);

        Assert.AreEqual(0, seen.Count, "A removed presenter neither renders nor originates.");
    }

    [TestMethod]
    public async Task Presenter_RemovedAndReAddedLive_KeepsTheConnectionOpen()
    {
        var driver = new DriverPresenter("driver", "x", "y");
        await using var session = CreateSession(driver);
        await session.OpenAsync(TestContext.CancellationToken);

        session.RemovePresenter(driver);
        Assert.AreEqual(0, session.Presenters.Count);
        session.AddPresenter(driver);

        Assert.AreEqual(1, session.Presenters.Count);
        Assert.AreEqual(ConnectionState.Open, session.State);
    }

    public TestContext TestContext { get; set; } = null!;
}
