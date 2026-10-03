using DevTerm.Core.StreamContent;
using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class StreamCaptureViewTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private static StreamMonitorCapture Make(StreamContentKind kind, int size, string device, int minute, string file) =>
        new(new StreamCapture(kind, new byte[size], _t0, StreamCaptureEnd.Complete, WasDeclared: false), device, _t0.AddMinutes(minute), @"C:\x\" + file, null);

    private static readonly StreamMonitorCapture _scopeBmp = Make(StreamContentKind.Bmp, 78_000, "tds2024", 5, "tds2024_a.bmp");
    private static readonly StreamMonitorCapture _awgPng = Make(StreamContentKind.Png, 4_000, "dg1062z", 1, "dg1062z_b.png");
    private static readonly StreamMonitorCapture _scopePng = Make(StreamContentKind.Png, 9_000, "tds2024", 3, "tds2024_c.png");

    private static readonly StreamMonitorCapture[] _all = [_scopeBmp, _awgPng, _scopePng];

    [TestMethod]
    public void Apply_NoCriteria_SortsOldestFirst() =>
        Assert.AreSequenceEqual([_awgPng, _scopePng, _scopeBmp], StreamCaptureView.Apply(_all));

    [TestMethod]
    public void Apply_SortsNewestLargestKindAndDevice()
    {
        Assert.AreSequenceEqual([_scopeBmp, _scopePng, _awgPng], StreamCaptureView.Apply(_all, sort: StreamCaptureSort.Newest));
        Assert.AreSequenceEqual([_scopeBmp, _scopePng, _awgPng], StreamCaptureView.Apply(_all, sort: StreamCaptureSort.Largest));
        Assert.AreEqual(StreamContentKind.Bmp.DisplayName, StreamCaptureView.Apply(_all, sort: StreamCaptureSort.Kind)[0].Capture.Kind.DisplayName);
        Assert.AreEqual("dg1062z", StreamCaptureView.Apply(_all, sort: StreamCaptureSort.Device)[0].DeviceName);
    }

    [TestMethod]
    public void Apply_FiltersByKindAndDevice_AndCombines()
    {
        Assert.AreSequenceEqual([_awgPng, _scopePng], StreamCaptureView.Apply(_all, kind: StreamContentKind.Png.DisplayName));
        Assert.AreSequenceEqual([_scopePng, _scopeBmp], StreamCaptureView.Apply(_all, device: "TDS2024"));
        Assert.AreSequenceEqual([_scopePng], StreamCaptureView.Apply(_all, kind: StreamContentKind.Png.DisplayName, device: "tds2024"));
    }

    [TestMethod]
    public void Apply_SearchMatchesDeviceKindFileAndTime_CaseInsensitively()
    {
        Assert.AreSequenceEqual([_awgPng], StreamCaptureView.Apply(_all, search: "DG1062"));
        Assert.AreSequenceEqual([_scopeBmp], StreamCaptureView.Apply(_all, search: "bmp image"));
        Assert.AreSequenceEqual([_scopePng], StreamCaptureView.Apply(_all, search: "_c.png"));
        Assert.AreSequenceEqual([_scopeBmp], StreamCaptureView.Apply(_all, search: "2026-10-03 09:05"));
        Assert.IsEmpty(StreamCaptureView.Apply(_all, search: "nothing like this"));
        Assert.HasCount(3, StreamCaptureView.Apply(_all, search: "  "));
    }

    [TestMethod]
    public void KindsAndDevices_AreDistinctAndSorted()
    {
        Assert.AreSequenceEqual(["BMP image", "PNG image"], StreamCaptureView.Kinds(_all));
        Assert.AreSequenceEqual(["dg1062z", "tds2024"], StreamCaptureView.Devices(_all));
    }
}
