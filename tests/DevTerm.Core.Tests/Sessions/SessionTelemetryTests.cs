using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Pipelines;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Core.Tests.Sessions;

/// <summary><see cref="Session"/> records into <see cref="DevTermTelemetry"/>'s source and meter; an exporter only has to subscribe.</summary>
[TestCategory(TestCategories.Unit)]
[DoNotParallelize]
[TestClass]
public sealed class SessionTelemetryTests
{
    private static Mock<ITransport> Transport(Pipe pipe)
    {
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        return transport;
    }

    private static MeterListener Listen(Dictionary<string, long> totals)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == DevTermTelemetry.Name)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (totals)
            {
                totals[instrument.Name] = totals.GetValueOrDefault(instrument.Name) + value;
            }
        });
        listener.Start();
        return listener;
    }

    private static long ReceivedBytes(Dictionary<string, long> totals)
    {
        lock (totals)
        {
            return totals.GetValueOrDefault("devterm.bytes.received");
        }
    }

    [TestMethod]
    public async Task OpenSendReceiveClose_RecordMetricsAndAnOpenSpan()
    {
        var totals = new Dictionary<string, long>();
        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DevTermTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (activities) { activities.Add(activity); } },
        };
        ActivitySource.AddActivityListener(activityListener);
        using var meterListener = Listen(totals);

        var pipe = new Pipe();
        var transport = Transport(pipe);
        await using var session = new Session(transport.Object, new Pipeline([]));

        await session.OpenAsync(TestContext.CancellationToken);
        await session.SendAsync(new byte[] { 1, 2, 3 }, TestContext.CancellationToken);
        await pipe.Writer.WriteAsync(new byte[] { 9, 9 }, TestContext.CancellationToken);
        for (var i = 0; i < 200 && ReceivedBytes(totals) < 2; i++)
        {
            await Task.Delay(25, TestContext.CancellationToken);
        }

        await session.CloseAsync(TestContext.CancellationToken);
        meterListener.RecordObservableInstruments();

        lock (totals)
        {
            Assert.AreEqual(1, totals["devterm.session.opened"]);
            Assert.AreEqual(3, totals["devterm.bytes.sent"]);
            Assert.AreEqual(2, totals["devterm.bytes.received"]);
            Assert.AreEqual(1, totals["devterm.session.closed"]);
        }

        lock (activities)
        {
            var open = activities.Single(a => a.OperationName == "devterm.session.open");
            Assert.AreEqual(ActivityStatusCode.Unset, open.Status);
            Assert.IsNotNull(open.GetTagItem("devterm.transport"));
        }
    }

    [TestMethod]
    public async Task AFailedOpen_MarksTheSpanAsAnError_AndStillThrows()
    {
        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DevTermTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (activities) { activities.Add(activity); } },
        };
        ActivitySource.AddActivityListener(activityListener);

        var transport = new Mock<ITransport>();
        transport.Setup(t => t.OpenAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("no device"));
        await using var session = new Session(transport.Object, new Pipeline([]));

        await Assert.ThrowsExactlyAsync<IOException>(() => session.OpenAsync(TestContext.CancellationToken));

        lock (activities)
        {
            var open = activities.Single(a => a.OperationName == "devterm.session.open");
            Assert.AreEqual(ActivityStatusCode.Error, open.Status);
            Assert.AreEqual("System.IO.IOException", open.GetTagItem("error.type"));
        }
    }

    public TestContext TestContext { get; set; } = null!;
}
