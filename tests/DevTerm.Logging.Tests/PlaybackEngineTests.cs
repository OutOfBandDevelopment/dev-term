using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Logging.Playback;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Logging.Tests;

/// <summary>The playback scheduler, driven by an injected clock so every timing assertion is exact.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class PlaybackEngineTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static SessionLogRecord Rx(long seq, double seconds, string text) =>
        new() { Kind = SessionLogRecordKind.Rx, Sequence = seq, Timestamp = _t0.AddSeconds(seconds), Data = Encoding.ASCII.GetBytes(text) };

    private static SessionLogRecord Tx(long seq, double seconds, string text) =>
        new() { Kind = SessionLogRecordKind.Tx, Sequence = seq, Timestamp = _t0.AddSeconds(seconds), Data = Encoding.ASCII.GetBytes(text) };

    /// <summary>Records at 0 s, 1 s, 2 s, 10 s: an ASCII line split across two chunks, a sent line, then a second line.</summary>
    private static SessionLog Log() => new(
        new SessionLogHeader { Created = _t0, Presenters = ["ascii"] },
        [
            Rx(1, 0, "hel"),
            Rx(2, 1, "lo\r"),
            Tx(3, 2, "ID?\r"),
            Rx(4, 10, "world\r"),
        ]);

    private static (PlaybackEngine Engine, ManualTimeProvider Clock) Create(SessionLog? log = null)
    {
        var clock = new ManualTimeProvider();
        var engine = new PlaybackEngine(
            log ?? Log(),
            () => new Pipeline([new AsciiPresenter(Options.Create(new AsciiPresenterOptions()))]),
            clock);
        return (engine, clock);
    }

    private static string[] Rendered(PlaybackBatch batch) => [.. batch.Items.SelectMany(i => i.Outputs).Select(o => o.Text)];

    [TestMethod]
    public void Realtime_PlaysEachRecordWhenItsRecordedTimeComes()
    {
        var (engine, clock) = Create();
        engine.Play();

        Assert.HasCount(1, engine.Tick().Items, "The first record is due at once.");
        Assert.AreEqual(TimeSpan.FromSeconds(1), engine.TimeUntilNextDue());

        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.IsEmpty(engine.Tick().Items);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        CollectionAssert.AreEqual(new[] { "hello" }, Rendered(engine.Tick()));
        Assert.AreEqual(2, engine.Position);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.HasCount(1, engine.Tick().Items);
        Assert.AreEqual(TimeSpan.FromSeconds(8), engine.TimeUntilNextDue());

        clock.Advance(TimeSpan.FromSeconds(8));
        CollectionAssert.AreEqual(new[] { "world" }, Rendered(engine.Tick()));
        Assert.IsTrue(engine.IsAtEnd);
        Assert.IsFalse(engine.IsPlaying, "Playback pauses itself at the end.");
        Assert.IsNull(engine.TimeUntilNextDue());
    }

    [TestMethod]
    [DataRow(2.0, 5000)]
    [DataRow(10.0, 1000)]
    [DataRow(0.5, 20000)]
    [DataRow(0.25, 40000)]
    public void Speed_ScalesTheWaitBetweenRecords(double speed, int millisecondsToReachTheLastRecord)
    {
        var (engine, clock) = Create();
        engine.Speed = speed;
        engine.Play();
        engine.Tick();

        clock.Advance(TimeSpan.FromMilliseconds(millisecondsToReachTheLastRecord - 1));
        engine.Tick();
        Assert.AreEqual(3, engine.Position, "Everything but the 10 s record has played.");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        engine.Tick();
        Assert.IsTrue(engine.IsAtEnd);
    }

    [TestMethod]
    public void MaxSpeed_PlaysEverythingInOneTick_WithoutTheClockMoving()
    {
        var (engine, _) = Create();
        engine.Speed = double.PositiveInfinity;
        engine.Play();

        var batch = engine.Tick();

        Assert.HasCount(4, batch.Items);
        CollectionAssert.AreEqual(new[] { "hello", "world" }, Rendered(batch));
    }

    [TestMethod]
    public void Tick_IsCappedPerCall_SoAHugeLogStillYieldsToTheUi()
    {
        var (engine, _) = Create();
        engine.Speed = double.PositiveInfinity;
        engine.Play();

        Assert.HasCount(3, engine.Tick(maxRecords: 3).Items);
        Assert.IsTrue(engine.IsPlaying);
        Assert.HasCount(1, engine.Tick(maxRecords: 3).Items);
    }

    [TestMethod]
    public void Pausing_FreezesPlaybackMidGap_AndResumingWaitsOnlyForWhatIsLeft()
    {
        var (engine, clock) = Create();
        engine.Play();
        engine.Tick();
        engine.Tick();
        clock.Advance(TimeSpan.FromSeconds(2));
        engine.Tick();
        Assert.AreEqual(3, engine.Position);

        clock.Advance(TimeSpan.FromSeconds(3));
        engine.Pause();
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.IsEmpty(engine.Tick().Items, "Nothing plays while paused.");

        engine.Play();
        Assert.AreEqual(TimeSpan.FromSeconds(5), engine.TimeUntilNextDue(), "3 s of the 8 s gap had already elapsed.");
    }

    [TestMethod]
    public void ChangingSpeedMidGap_KeepsTheProgressMadeAtTheOldSpeed()
    {
        var (engine, clock) = Create();
        engine.Play();
        engine.Tick();
        clock.Advance(TimeSpan.FromSeconds(2));
        engine.Tick();
        Assert.AreEqual(3, engine.Position);

        clock.Advance(TimeSpan.FromSeconds(4));
        engine.Speed = 2;

        Assert.AreEqual(TimeSpan.FromSeconds(2), engine.TimeUntilNextDue(), "4 s of log time left, at 2x.");
    }

    [TestMethod]
    public void Step_PlaysExactlyOneRecord_AndPauses()
    {
        var (engine, _) = Create();
        engine.Play();

        var batch = engine.Step();

        Assert.HasCount(1, batch.Items);
        Assert.AreEqual(0, batch.Items[0].Index);
        Assert.IsFalse(engine.IsPlaying);
        Assert.AreEqual(1, engine.Position);
    }

    [TestMethod]
    public void TxRecords_AreNeverFedToThePresenters()
    {
        var log = new SessionLog(new SessionLogHeader { Created = _t0 }, [Tx(1, 0, "abc\r")]);
        var (engine, _) = Create(log);

        var item = engine.Step().Items.Single();

        Assert.IsEmpty(item.Outputs);
        Assert.AreEqual("00:00.000 [tx] abc\\r", PlaybackText.Lines(item).Single().Text);
    }

    [TestMethod]
    public void Rewind_StartsOverWithFreshPresenters_SoAPartialLineDoesNotLeakIn()
    {
        var (engine, _) = Create();
        engine.Step();

        var reset = engine.Rewind();
        Assert.IsTrue(reset.Reset);
        Assert.AreEqual(0, engine.Position);

        engine.Step();
        CollectionAssert.AreEqual(new[] { "hello" }, Rendered(engine.Step()), "Not \"helhello\" - the first \"hel\" was discarded with the old presenter.");
    }

    [TestMethod]
    public void SeekingBackward_ReplaysFromTheStart_ReachingTheSamePresenterState()
    {
        var (engine, _) = Create();
        engine.SkipToEnd();

        var batch = engine.SeekTo(1);

        Assert.IsTrue(batch.Reset);
        Assert.AreEqual(1, engine.Position);
        CollectionAssert.AreEqual(new[] { "hello" }, Rendered(engine.Step()));
    }

    [TestMethod]
    public void FastForward_PlaysEverythingInTheSkippedSpanInstantly()
    {
        var (engine, _) = Create();
        engine.Step();

        var batch = engine.FastForward(TimeSpan.FromSeconds(5));

        Assert.IsFalse(batch.Reset);
        Assert.AreEqual(3, engine.Position, "Records at 1 s and 2 s are within 5 s of the 0 s record; the 10 s one isn't.");
        CollectionAssert.AreEqual(new[] { "hello" }, Rendered(batch));
    }

    [TestMethod]
    public void FastForward_AlwaysMakesProgress_AcrossAGapLongerThanTheSkip()
    {
        var (engine, _) = Create();
        engine.SeekTo(3);

        engine.FastForward(TimeSpan.FromSeconds(1));

        Assert.IsTrue(engine.IsAtEnd);
    }

    [TestMethod]
    public void ChangePipeline_ReplaysToTheSamePositionThroughTheNewPresenters()
    {
        var (engine, _) = Create();
        engine.SeekTo(2);

        var batch = engine.ChangePipeline(() => new Pipeline([new HexPresenter()]));

        Assert.IsTrue(batch.Reset);
        Assert.AreEqual(2, engine.Position);
        Assert.AreEqual("hex", batch.Items[0].Outputs[0].PresenterName);
    }

    [TestMethod]
    public void AddNote_InsertsAfterTheLastPlayedRecord_AndShowsItAtOnce()
    {
        var (engine, _) = Create();
        engine.SeekTo(2);

        var item = engine.AddNote("reply ok").Items.Single();

        Assert.AreEqual(2, item.Index);
        Assert.AreEqual(SessionLogRecordKind.Note, engine.Log.Records[2].Kind);
        Assert.AreEqual(3, engine.Position);
        Assert.AreEqual(TimeSpan.FromSeconds(1), item.Offset, "It takes the time of the record before it.");
        Assert.AreEqual("00:01.000 [note] reply ok", PlaybackText.Lines(item).Single().Text);
    }
}
