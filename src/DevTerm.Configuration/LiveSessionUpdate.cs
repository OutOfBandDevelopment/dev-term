using System.Text.Json;
using System.Text.Json.Nodes;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;

namespace DevTerm.Configuration;

/// <summary>
/// Applies an edited connection profile to a session that is already running, without closing it, when
/// nothing that identifies the connection changed. "Live" settings are the serial line (baud, data bits,
/// parity, stop bits, DTR, RTS - see <see cref="IComPortControl"/>), the displaying presenters, the send
/// parser and the line ending. Anything else (transport, port, host, handshake, timeouts, ...) needs a new connection.
/// </summary>
public static class LiveSessionUpdate
{
    private static readonly string[] _liveProperties =
    [
        nameof(CliOptions.Baud),
        nameof(CliOptions.DataBits),
        nameof(CliOptions.Parity),
        nameof(CliOptions.StopBits),
        nameof(CliOptions.Dtr),
        nameof(CliOptions.Rts),
        nameof(CliOptions.Presenter),
        nameof(CliOptions.Parser),
        nameof(CliOptions.LineEnding),
    ];

    /// <summary>True when <paramref name="next"/> differs from <paramref name="current"/> only in live settings (or not at all).</summary>
    public static bool CanApplyLive(CliOptions current, CliOptions next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        return JsonNode.DeepEquals(Identity(current), Identity(next));
    }

    /// <summary>
    /// Applies the live differences to <paramref name="session"/> in place and returns true, or returns false
    /// (changing nothing) if a new connection is needed or a presenter or parser is unknown. Line settings
    /// reach the transport only if it implements <see cref="IComPortControl"/>; a transport that does not
    /// has no line settings in its profile to change.
    /// </summary>
    public static bool TryApply(Session session, PresenterCatalog catalog, CliOptions current, CliOptions next)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!CanApplyLive(current, next) || !catalog.TryGetInput(next.EffectiveParser, out _))
        {
            return false;
        }

        IReadOnlyList<IPresenter> wanted;
        try
        {
            wanted = DevTermSessionBuilder.ResolvePresenters(catalog, next);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (session.Transport is IComPortControl line)
        {
            if (current.Baud != next.Baud)
            {
                line.SetBaudRate(next.Baud);
            }

            if (current.DataBits != next.DataBits)
            {
                line.SetDataBits(next.DataBits);
            }

            if (current.Parity != next.Parity)
            {
                line.SetParity(ToCom(next.Parity));
            }

            if (current.StopBits != next.StopBits)
            {
                line.SetStopBits(ToCom(next.StopBits));
            }

            if (current.Dtr != next.Dtr)
            {
                line.SetDtr(next.Dtr);
            }

            if (current.Rts != next.Rts)
            {
                line.SetRts(next.Rts);
            }
        }

        // Presenters bound by other things (a control panel's scpi/manifest presenter) are left alone: only
        // the ones the profile names are added or removed.
        var named = new HashSet<IPresenter>(DevTermSessionBuilder.ResolvePresenters(catalog, current), ReferenceEqualityComparer.Instance as IEqualityComparer<IPresenter>);
        foreach (var presenter in session.Presenters)
        {
            if (named.Contains(presenter) && !wanted.Contains(presenter))
            {
                session.RemovePresenter(presenter);
            }
        }

        foreach (var presenter in wanted)
        {
            session.AddPresenter(presenter);
        }

        return true;
    }

    private static ComParity ToCom(System.IO.Ports.Parity parity) => parity switch
    {
        System.IO.Ports.Parity.Odd => ComParity.Odd,
        System.IO.Ports.Parity.Even => ComParity.Even,
        System.IO.Ports.Parity.Mark => ComParity.Mark,
        System.IO.Ports.Parity.Space => ComParity.Space,
        _ => ComParity.None,
    };

    private static ComStopBits ToCom(System.IO.Ports.StopBits stopBits) => stopBits switch
    {
        System.IO.Ports.StopBits.Two => ComStopBits.Two,
        System.IO.Ports.StopBits.OnePointFive => ComStopBits.OnePointFive,
        _ => ComStopBits.One,
    };

    private static JsonNode Identity(CliOptions options)
    {
        var node = JsonSerializer.SerializeToNode(options) ?? new JsonObject();
        if (node is JsonObject obj)
        {
            foreach (var key in _liveProperties.Concat([nameof(CliOptions.EffectivePresenters), nameof(CliOptions.EffectiveParser)]))
            {
                obj.Remove(key);
            }
        }

        return node;
    }
}
