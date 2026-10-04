namespace DevTerm.Test.Utilities;

/// <summary>
/// One front end (CLI, TUI, WPF or web) driven the way a user drives it, so <see cref="UserFlowTestsBase"/> can run the same
/// flow against all of them. Every driver is already connected to a loopback device when a flow starts.
/// </summary>
public interface IFrontEndDriver
{
    /// <summary>Front end name, for assertion messages.</summary>
    string Name { get; }

    /// <summary>False for a front end with no way to drop and re-open the connection by hand (the CLI, the web page).</summary>
    bool CanDisconnect { get; }

    /// <summary>Types <paramref name="line"/> into the front end's input and submits it.</summary>
    Task SendAsync(string line);

    /// <summary>Everything the front end has shown so far, as one string.</summary>
    Task<string> OutputAsync();

    /// <summary>Waits until the output contains <paramref name="text"/>; false on timeout.</summary>
    Task<bool> WaitForOutputAsync(string text, TimeSpan timeout);

    /// <summary>Whether the front end currently reports itself connected.</summary>
    Task<bool> IsConnectedAsync();

    /// <summary>The Connect/Disconnect action. Only valid when <see cref="CanDisconnect"/>.</summary>
    Task DisconnectAsync();

    /// <summary>The Connect/Disconnect action. Only valid when <see cref="CanDisconnect"/>.</summary>
    Task ReconnectAsync();

    /// <summary>True when the front end can start a session log while running (the CLI and web page cannot).</summary>
    bool CanLog { get; }

    Task StartLoggingAsync(string path);

    Task StopLoggingAsync();

    /// <summary>True when the front end can switch to another connection profile while running.</summary>
    bool CanSwitchProfile { get; }

    /// <summary>Switches to a fresh loopback profile (the same device, a new session).</summary>
    Task SwitchToLoopbackProfileAsync();
}
