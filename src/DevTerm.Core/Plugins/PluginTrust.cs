using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DevTerm.Core.Plugins;

/// <summary>What the user decided about running an out-of-process plugin.</summary>
public enum PluginApprovalChoice
{
    /// <summary>Don't run it (and ask again next start).</summary>
    Deny,

    /// <summary>Run it for this start only.</summary>
    Once,

    /// <summary>Run it, and remember the approval for exactly this content (its hash), so it isn't asked about again until it changes.</summary>
    Always,
}

/// <summary>One remembered approval (<see cref="PluginTrust.Approvals"/>).</summary>
public sealed record PluginApproval(string Name, string Hash, DateTimeOffset ApprovedAt);

/// <summary>What the user is asked to approve: a program that will run with their rights.</summary>
public sealed record PluginApprovalRequest(string Name, string Version, string Folder, string CommandLine, string Hash);

/// <summary>Asks the user whether an out-of-process plugin may run. A front end supplies one; with none, only already-remembered plugins run.</summary>
public interface IPluginApprover
{
    PluginApprovalChoice Ask(PluginApprovalRequest request);
}

/// <summary>
/// The trust model for out-of-process plugins (docs/design/proposals/out-of-process-plugins.md): such a plugin runs only after
/// the user approves it, optionally once per <see cref="PluginHash"/> so an unchanged plugin isn't asked about again and any
/// edit (a swapped script, an updated binary) asks again. No signing. The approvals live in
/// <c>plugin-approvals.json</c> under <see cref="DevTermHome"/>.
/// </summary>
public static class PluginTrust
{
    private static readonly Lock _gate = new();

    /// <summary>The front end's prompt; null means "never ask" (only remembered approvals run).</summary>
    public static IPluginApprover? Approver { get; set; }

    /// <summary>Where approvals are remembered; defaults to <c>plugin-approvals.json</c> in <see cref="DevTermHome.Root"/>.</summary>
    public static string StorePath => Path.Combine(DevTermHome.Root, "plugin-approvals.json");

    /// <summary>Whether this exact content (<paramref name="hash"/>) was approved with <see cref="PluginApprovalChoice.Always"/>.</summary>
    public static bool IsApproved(string name, string hash)
    {
        lock (_gate)
        {
            return Read().Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Hash, hash, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static void Remember(string name, string hash)
    {
        lock (_gate)
        {
            var entries = Read();
            entries.RemoveAll(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            entries.Add(new Entry { Name = name, Hash = hash, ApprovedAt = DateTimeOffset.UtcNow });
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(new Store { Approved = entries }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>Every remembered approval, oldest first: the plugin name, the approved content hash and when it was approved.</summary>
    public static IReadOnlyList<PluginApproval> Approvals()
    {
        lock (_gate)
        {
            return [.. Read().OrderBy(e => e.ApprovedAt).Select(e => new PluginApproval(e.Name, e.Hash, e.ApprovedAt))];
        }
    }

    /// <summary>Forgets a plugin's remembered approval.</summary>
    public static void Forget(string name)
    {
        lock (_gate)
        {
            var entries = Read();
            if (entries.RemoveAll(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                File.WriteAllText(StorePath, JsonSerializer.Serialize(new Store { Approved = entries }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    /// <summary>Decides whether the plugin may run, asking (and remembering) as needed. Never throws.</summary>
    public static bool Authorise(PluginApprovalRequest request)
    {
        if (IsApproved(request.Name, request.Hash))
        {
            return true;
        }

        var choice = Approver?.Ask(request) ?? PluginApprovalChoice.Deny;
        if (choice == PluginApprovalChoice.Always)
        {
            try
            {
                Remember(request.Name, request.Hash);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Couldn't save the approval: still run this once, and ask again next time.
            }
        }

        return choice != PluginApprovalChoice.Deny;
    }

    private static List<Entry> Read()
    {
        try
        {
            return File.Exists(StorePath) ? [.. JsonSerializer.Deserialize<Store>(File.ReadAllText(StorePath))?.Approved ?? []] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private sealed class Store
    {
        public List<Entry> Approved { get; set; } = [];
    }

    private sealed class Entry
    {
        public string Name { get; set; } = string.Empty;

        public string Hash { get; set; } = string.Empty;

        public DateTimeOffset ApprovedAt { get; set; }
    }
}

/// <summary>A SHA-256 over a plugin folder's every file (relative path and content, in a fixed order), so any change to the plugin changes its hash.</summary>
public static class PluginHash
{
    public static string Compute(string folder)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var root = Path.GetFullPath(folder);
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') + "\n"));
            hash.AppendData(File.ReadAllBytes(file));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
