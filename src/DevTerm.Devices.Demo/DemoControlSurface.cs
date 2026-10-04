using System.Globalization;
using System.Text;
using DevTerm.Core.Control;
using DevTerm.Core.Sessions;

namespace DevTerm.Devices.Demo;

/// <summary>Holds the toggle and slider state and, on "apply", sends one text line, <c>SET LED=&lt;0|1&gt; LEVEL=&lt;n&gt;</c>, over the session.</summary>
public sealed class DemoControlSurface(Session session) : IControlSurface
{
    private readonly Lock _stateLock = new();
    private bool _led;
    private int _level;

    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        lock (_stateLock)
        {
            switch (commandId)
            {
                case "led":
                    _led = value is "1" or "true" or "True";
                    return Task.CompletedTask;
                case "level":
                    _level = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? Math.Clamp(parsed, 0, 9) : 0;
                    return Task.CompletedTask;
                case "apply":
                    return session.SendAsync(Encoding.ASCII.GetBytes($"SET LED={(_led ? 1 : 0)} LEVEL={_level}\r"), cancellationToken);
                default:
                    return Task.CompletedTask;
            }
        }
    }
}
