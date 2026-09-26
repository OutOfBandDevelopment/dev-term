using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>
/// Everything a front end needs to open a loaded <see cref="DeviceManifest"/> as a live control
/// panel on the current session: the panel's <see cref="Definition"/> (see
/// <see cref="ManifestUiBuilder"/>), the <see cref="Surface"/> that executes its commands, and the
/// <see cref="Presenter"/> that correlates replies and matches response patterns — bound into the
/// session's live pipeline by <see cref="Attach"/> and unbound again by <see cref="Dispose"/>, so the
/// presenter lives exactly as long as the panel (reopening a panel never stacks up presenters). Both
/// front ends' "Device > Device Manifest..." items build one of these and hand its three parts to
/// their generic control-panel renderer.
/// </summary>
public sealed class ManifestPanel : IDisposable
{
    private readonly Session _session;
    private bool _disposed;

    private ManifestPanel(Session session, DeviceManifest manifest)
    {
        _session = session;
        Manifest = manifest;
        Definition = ManifestUiBuilder.Build(manifest);
        Presenter = new ManifestReplyPresenter(manifest);
        Surface = new ManifestControlSurface(session, manifest, Presenter, Definition);
    }

    public DeviceManifest Manifest { get; }

    public UiDefinition Definition { get; }

    public ManifestControlSurface Surface { get; }

    public ManifestReplyPresenter Presenter { get; }

    /// <summary>The window title both front ends use: <c>dev-term — {manifest name}</c>.</summary>
    public string Title => $"dev-term — {Manifest.Name}";

    /// <summary>Builds the panel's parts and binds its presenter into <paramref name="session"/>'s live pipeline.</summary>
    public static ManifestPanel Attach(Session session, DeviceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(manifest);

        var panel = new ManifestPanel(session, manifest);
        session.AddPresenter(panel.Presenter);
        return panel;
    }

    /// <summary>Unbinds the presenter from the session (the panel has closed).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.RemovePresenter(Presenter);
    }
}
