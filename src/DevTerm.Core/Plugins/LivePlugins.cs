using DevTerm.Core.Presenters;

namespace DevTerm.Core.Plugins;

/// <summary>An out-of-process plugin that was found but not yet approved: what to ask, and how to start it once approved.</summary>
public sealed record PendingPlugin(PluginApprovalRequest Request, string Command, IReadOnlyList<string> Arguments, TimeSpan ReplyTimeout);

/// <summary>
/// Out-of-process plugins that load while the app is running. A plugin the loader found but nobody approved at startup waits here;
/// approving it (<see cref="Approve"/>, from a front end's dialog) makes its presenter resolvable at once, through any
/// <see cref="PresenterCatalog"/>, with no restart. Process-wide, like <see cref="PluginTrust"/>.
/// </summary>
public static class LivePlugins
{
    private static readonly Lock _gate = new();
    private static readonly List<PendingPlugin> _pending = [];
    private static readonly Dictionary<string, PendingPlugin> _live = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after a plugin goes live, so a front end can refresh whatever lists presenters.</summary>
    public static event Action<string>? Activated;

    /// <summary>Plugins waiting for approval, in the order found.</summary>
    public static IReadOnlyList<PendingPlugin> Pending
    {
        get
        {
            lock (_gate)
            {
                return [.. _pending];
            }
        }
    }

    /// <summary>Names of plugins approved while running.</summary>
    public static IReadOnlyCollection<string> ActiveNames
    {
        get
        {
            lock (_gate)
            {
                return [.. _live.Keys];
            }
        }
    }

    internal static void AddPending(PendingPlugin plugin)
    {
        lock (_gate)
        {
            _pending.RemoveAll(p => string.Equals(p.Request.Name, plugin.Request.Name, StringComparison.OrdinalIgnoreCase));
            _pending.Add(plugin);
        }
    }

    /// <summary>Approves a waiting plugin and starts offering its presenter. <see cref="PluginApprovalChoice.Always"/> also remembers the approval.</summary>
    public static bool Approve(string name, PluginApprovalChoice choice)
    {
        if (choice == PluginApprovalChoice.Deny)
        {
            return false;
        }

        PendingPlugin? plugin;
        lock (_gate)
        {
            plugin = _pending.Find(p => string.Equals(p.Request.Name, name, StringComparison.OrdinalIgnoreCase));
            if (plugin is null)
            {
                return false;
            }

            _pending.Remove(plugin);
            _live[plugin.Request.Name] = plugin;
        }

        if (choice == PluginApprovalChoice.Always)
        {
            try
            {
                PluginTrust.Remember(plugin.Request.Name, plugin.Request.Hash);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Runs this start; asked again next time.
            }
        }

        Activated?.Invoke(plugin.Request.Name);
        return true;
    }

    /// <summary>Asks <paramref name="ask"/> about each waiting plugin and applies the answer; returns how many went live. A front end's dialog supplies <paramref name="ask"/>.</summary>
    public static int ReviewPending(Func<PendingPlugin, PluginApprovalChoice> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        return Pending.Count(plugin => Approve(plugin.Request.Name, ask(plugin)));
    }

    /// <summary>A fresh presenter for every live plugin (presenters are stateful, so each catalog gets its own).</summary>
    internal static IReadOnlyList<IPresenter> CreatePresenters()
    {
        lock (_gate)
        {
            return [.. _live.Values.Select(p => (IPresenter)new LazyExternalPresenter(p.Request.Name, p.Command, p.Arguments, p.ReplyTimeout))];
        }
    }

    /// <summary>Forgets everything (tests).</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
            _live.Clear();
        }
    }
}
