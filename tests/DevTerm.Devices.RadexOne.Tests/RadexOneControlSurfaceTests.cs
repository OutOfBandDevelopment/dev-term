using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Devices.RadexOne.Tests;

/// <summary>
/// Verifies the exact framer packets <see cref="RadexOneControlSurface"/> sends, against a mocked
/// <see cref="ITransport"/> behind a real <see cref="Session"/> — no real serial device involved, so
/// this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Serial)]
[TestCategory(TestCategories.Radex_One)]
[TestClass]
public sealed class RadexOneControlSurfaceTests
{
    private static (Session Session, Mock<ITransport> Transport) CreateSurfaceSession()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        return (session, transport);
    }

    [TestMethod]
    public async Task InvokeAsync_ReadData_SendsAWrappedQueryWithAnEmptyExtension()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new RadexOneControlSurface(session);

        await surface.InvokeAsync("readData", null, TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => IsWellFormedQuery(b.ToArray(), RadexOneCommand.ReadData)),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_ReadSerialVersion_SendsAWrappedQuery()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new RadexOneControlSurface(session);

        await surface.InvokeAsync("readSerialVersion", null, TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => IsWellFormedQuery(b.ToArray(), RadexOneCommand.ReadSerialVersion)),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_ReadSettings_SendsAWrappedQuery()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new RadexOneControlSurface(session);

        await surface.InvokeAsync("readSettings", null, TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => IsWellFormedQuery(b.ToArray(), RadexOneCommand.ReadSettings)),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_WriteSettings_SendsTheSameFrameThreeTimes()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new RadexOneControlSurface(session);
        await surface.InvokeAsync("alarmMode", "Audio", TestContext.CancellationToken);
        await surface.InvokeAsync("threshold", "300", TestContext.CancellationToken);

        await surface.InvokeAsync("writeSettings", null, TestContext.CancellationToken);

        transport.Verify(
            t => t.WriteAsync(
                It.Is<ReadOnlyMemory<byte>>(b => IsWellFormedQuery(b.ToArray(), RadexOneCommand.WriteSettings)),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownCommand_Throws()
    {
        var (session, _) = CreateSurfaceSession();
        var surface = new RadexOneControlSurface(session);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => surface.InvokeAsync("notARealCommand", null, TestContext.CancellationToken));
    }

    [TestMethod]
    public void PreviewCommand_ReadData_ShowsTheReportAsHex()
    {
        var (session, _) = CreateSurfaceSession();
        var surface = new RadexOneControlSurface(session);

        var preview = surface.PreviewCommand("readData", null);

        Assert.IsNotNull(preview);
        StringAssert.StartsWith(preview, "7B FF");
    }

    [TestMethod]
    public void PreviewCommand_StateOnlySetters_AreNull()
    {
        var (session, _) = CreateSurfaceSession();
        var surface = new RadexOneControlSurface(session);

        Assert.IsNull(surface.PreviewCommand("alarmMode", "Audio"));
        Assert.IsNull(surface.PreviewCommand("threshold", "300"));
    }

    private static bool IsWellFormedQuery(byte[] report, ushort expectedCommandCode) =>
        report.Length >= RadexOneFramer.HeaderLength + 2
        && report[0] == 0x7B && report[1] == 0xFF // outbound framer prefix
        && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(2, 2)) == 0x0020 // constant outbound type marker
        && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(RadexOneFramer.HeaderLength, 2)) == expectedCommandCode;

    public required TestContext TestContext { get; set; }
}
