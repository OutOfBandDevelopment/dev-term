using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Core.Plugins;

/// <summary>One plugin folder's outcome: loaded, or skipped with the reason.</summary>
public sealed record PluginLoadResult(string Folder, string Name, bool Loaded, string Message);

/// <summary>
/// Loads plugins from a directory of per-plugin folders (<c>&lt;dir&gt;/&lt;plugin&gt;/plugin.json</c> plus its assemblies), each in its own
/// <see cref="AssemblyLoadContext"/>, and lets each <see cref="IPluginModule"/> register its services. Never throws for a
/// bad plugin: it is skipped and reported, so one broken folder can't stop the app starting.
/// </summary>
public static class PluginLoader
{
    /// <summary>The plugin contract this build of dev-term implements. A plugin built for another number is skipped.</summary>
    public const int ContractVersion = 1;

    public static IReadOnlyList<PluginLoadResult> LoadAll(string? directory, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var results = new List<PluginLoadResult>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return results;
        }

        foreach (var folder in Directory.GetDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(folder, PluginManifest.FileName)))
            {
                results.Add(Load(folder, services));
            }
        }

        return results;
    }

    private static PluginLoadResult Load(string folder, IServiceCollection services)
    {
        var label = Path.GetFileName(folder);
        var manifest = PluginManifest.TryRead(Path.Combine(folder, PluginManifest.FileName), out var error);
        if (manifest is null)
        {
            return new(folder, label, false, error);
        }

        if (manifest.Contract != ContractVersion)
        {
            return new(folder, manifest.Name, false, $"built for plugin contract {manifest.Contract}; this dev-term implements {ContractVersion}.");
        }

        var assemblyPath = Path.GetFullPath(Path.Combine(folder, manifest.Assembly));
        if (!assemblyPath.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(assemblyPath))
        {
            return new(folder, manifest.Name, false, $"assembly '{manifest.Assembly}' isn't in the plugin folder.");
        }

        try
        {
            var assembly = new PluginLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
            var modules = assembly.GetTypes()
                .Where(type => typeof(IPluginModule).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
                .ToList();
            if (modules.Count == 0)
            {
                return new(folder, manifest.Name, false, "no IPluginModule implementation found.");
            }

            // Register into a scratch collection first so a module that throws halfway leaves nothing behind.
            var staged = new ServiceCollection();
            foreach (var type in modules)
            {
                ((IPluginModule)Activator.CreateInstance(type)!).ConfigureServices(staged);
            }

            foreach (var descriptor in staged)
            {
                services.Add(descriptor);
            }

            return new(folder, manifest.Name, true, $"loaded {manifest.Name} {manifest.Version}.");
        }
        catch (Exception ex)
        {
            return new(folder, manifest.Name, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Resolves a plugin's own dependencies from its folder, but never a second copy of what the host already loaded (so contract types unify).</summary>
    private sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(pluginPath))
    {
        private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (Default.Assemblies.Any(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.Ordinal)))
            {
                return null;
            }

            return _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;
        }
    }
}
