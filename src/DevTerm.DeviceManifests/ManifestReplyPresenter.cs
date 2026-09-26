using System.Text.RegularExpressions;
using DevTerm.Core.Presenters;

namespace DevTerm.DeviceManifests;

/// <summary>
/// The inbound half of a manifest's text protocol: the shared <see cref="LineReplyPresenter"/>
/// line-buffering and FIFO query correlation (exactly the SCPI module's), plus every
/// <see cref="InboundProtocol.Patterns"/> regex tested against each complete line — correlated reply
/// or unsolicited telemetry alike — publishing its captures as live values (see
/// <see cref="ResponsePattern"/>). That's what feeds a manifest panel's indicators, bar graphs,
/// strip charts and vector displays.
/// </summary>
/// <remarks>
/// Renders no output text of its own: it's bound into an already-open session only for the life of
/// one panel (<see cref="ManifestPanel"/>), and the connection's own presenters already show every
/// line — rendering them again would print each one twice.
/// </remarks>
public sealed class ManifestReplyPresenter : LineReplyPresenter
{
    private readonly List<(ResponsePattern Pattern, Regex Regex)> _patterns = [];

    public ManifestReplyPresenter(DeviceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        foreach (var pattern in manifest.Inbound?.Patterns ?? [])
        {
            _patterns.Add((pattern, new Regex(pattern.Match, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250))));
        }

        ConfigureTerminator(manifest.Inbound?.LineTerminated == false ? string.Empty : "\n");
    }

    public override string Name => "manifest";

    protected override bool RendersLines => false;

    protected override void AddLineValues(string line, Dictionary<string, string> values)
    {
        foreach (var (pattern, regex) in _patterns)
        {
            Match match;
            try
            {
                match = regex.Match(line);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!match.Success)
            {
                continue;
            }

            values[pattern.Name] = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
            foreach (var group in match.Groups.Cast<Group>().Where(g => g.Success && !int.TryParse(g.Name, out _)))
            {
                values[group.Name] = group.Value;
            }
        }
    }
}
