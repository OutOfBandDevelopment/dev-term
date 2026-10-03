using System.Windows;
using DevTerm.Core.Plugins;

namespace DevTerm.Wpf;

/// <summary>Asks in a dialog whether an out-of-process plugin may run: Yes = always for this exact version, No = this time only, Cancel = don't run it.</summary>
internal sealed class MessageBoxPluginApprover : IPluginApprover
{
    public PluginApprovalChoice Ask(PluginApprovalRequest request)
    {
        var text = $"The plugin '{request.Name}' {request.Version} wants to run a program with your rights:\n\n{request.CommandLine}\n\nFrom {request.Folder}\n\n"
            + "Yes: run it, and don't ask again for this exact version.\nNo: run it this time only.\nCancel: don't run it.";
        return MessageBox.Show(text, "Run plugin program?", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => PluginApprovalChoice.Always,
            MessageBoxResult.No => PluginApprovalChoice.Once,
            _ => PluginApprovalChoice.Deny,
        };
    }
}
