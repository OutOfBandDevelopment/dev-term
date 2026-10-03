using DevTerm.Core.Plugins;

namespace DevTerm.Console;

/// <summary>
/// Asks on the terminal whether an out-of-process plugin may run, before the UI starts. One keypress: <c>y</c> for this start,
/// <c>a</c> to always allow this exact version, anything else to refuse. Only installed when the terminal is interactive.
/// </summary>
internal sealed class ConsolePluginApprover : IPluginApprover
{
    public PluginApprovalChoice Ask(PluginApprovalRequest request)
    {
        System.Console.Error.WriteLine();
        System.Console.Error.WriteLine($"Plugin '{request.Name}' {request.Version} wants to run a program with your rights:");
        System.Console.Error.WriteLine($"  {request.CommandLine}");
        System.Console.Error.WriteLine($"  from {request.Folder} (content hash {request.Hash[..12]})");
        System.Console.Error.Write("Run it? [y] this time, [a]lways for this exact version, [N]o: ");
        var key = System.Console.ReadKey(intercept: true).KeyChar;
        System.Console.Error.WriteLine(key);
        return char.ToLowerInvariant(key) switch
        {
            'y' => PluginApprovalChoice.Once,
            'a' => PluginApprovalChoice.Always,
            _ => PluginApprovalChoice.Deny,
        };
    }
}
