using System.Windows;
using DevTerm.Transports.Tcp;

namespace DevTerm.Wpf;

/// <summary>
/// View &gt; Software Flow Control (XON/XOFF): only meaningful for a TCP session (see
/// <see cref="TcpTransport.SoftwareFlowControl"/>) - enabled/checked per the active tab, since each
/// tab's transport has its own independent setting. Mirrors <c>TuiMode</c>'s equivalent menu item.
/// </summary>
public partial class MainWindow
{
    private void SoftwareFlowControlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWindowTabOrNull?.Tab.Session.Transport is TcpTransport tcp)
        {
            tcp.SoftwareFlowControl = SoftwareFlowControlMenuItem.IsChecked;
        }
    }

    /// <summary>Called from <c>RefreshConnectionUi(WindowTab, ConnectionState?)</c> for the active tab.</summary>
    private void RefreshSoftwareFlowControlMenu(WindowTab tab)
    {
        if (tab.Tab.Session.Transport is TcpTransport tcp)
        {
            SoftwareFlowControlMenuItem.IsEnabled = true;
            SoftwareFlowControlMenuItem.IsChecked = tcp.SoftwareFlowControl;
        }
        else
        {
            SoftwareFlowControlMenuItem.IsEnabled = false;
            SoftwareFlowControlMenuItem.IsChecked = false;
        }
    }
}
