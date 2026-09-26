using DevTerm.Test.Utilities;

namespace DevTerm.Devices.De5000.Tests;

/// <summary>
/// Confirms <see cref="De5000ControlSurface"/> is the deliberate no-op it's documented as — the
/// DE-5000 has no writable commands, so every call must throw rather than silently succeed.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.DerEe_De5000)]
[TestClass]
public sealed class De5000ControlSurfaceTests
{
    [TestMethod]
    public async Task InvokeAsync_AnyCommandId_ThrowsArgumentException()
    {
        var surface = new De5000ControlSurface();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => surface.InvokeAsync("anything", null));
    }

    [TestMethod]
    public async Task InvokeAsync_NullCommandId_ThrowsArgumentNullException()
    {
        var surface = new De5000ControlSurface();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => surface.InvokeAsync(null!, null));
    }
}
