using System.Text;
using DevTerm.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.Editor;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// View &gt; All Sessions Log...: the merged, time-ordered traffic of every open tab (<see cref="MergedSessionLog"/>) in a
/// read-only dialog. A snapshot of the log when opened; Refresh re-reads it, Clear empties it.
/// </summary>
internal static class MergedLogMode
{
    internal static string Render(MergedSessionLog log)
    {
        var builder = new StringBuilder();
        foreach (var entry in log.Entries)
        {
            builder.Append(entry).Append('\n');
        }

        return builder.Length == 0 ? "(no traffic yet - recording started when this dialog was first opened)" : builder.ToString();
    }

    internal static Dialog BuildDialog(IApplication app, MergedSessionLog log)
    {
        var dialog = new Dialog { Title = "All sessions log  (> received, < sent)", Width = Dim.Percent(90), Height = Dim.Percent(85) };
        var text = new Editor { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2), ReadOnly = true, WordWrap = true, Text = Render(log) };
        var refresh = new Button { X = 0, Y = Pos.AnchorEnd(1), Text = "Refresh" };
        refresh.Accepting += (_, e) =>
        {
            e.Handled = true;
            text.Text = Render(log);
        };
        var clear = new Button { X = Pos.Right(refresh) + 1, Y = Pos.AnchorEnd(1), Text = "Clear" };
        clear.Accepting += (_, e) =>
        {
            e.Handled = true;
            log.Clear();
            text.Text = Render(log);
        };
        var close = new Button { X = Pos.Right(clear) + 1, Y = Pos.AnchorEnd(1), Text = "Close", IsDefault = true };
        close.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };
        dialog.Add(text, refresh, clear, close);
        return dialog;
    }
}
