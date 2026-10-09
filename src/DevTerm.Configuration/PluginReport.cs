using DevTerm.Core.Plugins;

namespace DevTerm.Configuration;

/// <summary>The one text every front end shows for "which plugins were found" (the CLI's <c>--listplugins</c>, the TUI and WPF Plugins items).</summary>
public static class PluginReport
{
    /// <summary>One line per plugin folder (loaded, or skipped with why), or a single "No plugins found." line.</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<PluginLoadResult>? results)
    {
        if (results is null || results.Count == 0)
        {
            return ["No plugins found."];
        }

        return [.. results.Select(plugin => plugin.Loaded
            ? $"{plugin.Name}  loaded  ({plugin.Folder})"
            : $"{plugin.Name}  skipped: {plugin.Message}  ({plugin.Folder})")];
    }

    /// <summary>One line per remembered out-of-process plugin approval (name, short content hash, when), or a single "none remembered" line.</summary>
    public static IReadOnlyList<string> ApprovalLines(IReadOnlyList<PluginApproval> approvals)
    {
        if (approvals.Count == 0)
        {
            return ["No plugin approvals are remembered."];
        }

        return [.. approvals.Select(a => $"{a.Name}  {a.Hash[..Math.Min(12, a.Hash.Length)]}  approved {a.ApprovedAt.LocalDateTime:yyyy-MM-dd HH:mm}")];
    }

    public static string ApprovalText(IReadOnlyList<PluginApproval> approvals) => string.Join(Environment.NewLine, ApprovalLines(approvals));

    public static string Text(IReadOnlyList<PluginLoadResult>? results) => string.Join(Environment.NewLine, Lines(results));
}
