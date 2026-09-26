using System.Xml;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.Editor.Highlighting.Xshd;

namespace DevTerm.Console;

/// <summary>
/// Colors the TUI output pane per line, by the source tag every line already carries (see
/// <c>TuiMode.StatusLine</c>/<c>ErrorLine</c>): <c>[error] …</c> lines red and bold, <c>[dev-term] …</c>
/// status lines grey and italic, device output (<c>[ascii] …</c>) in the default color. The pane is a
/// single <c>Terminal.Gui.Editor</c>, which has no per-item styling like WPF's list does, but it's an
/// AvalonEdit port with a real syntax-highlighting engine, so a tiny XSHD definition whose rules match
/// whole lines by their tag is enough. The tags stay in the text, so the distinction survives a
/// terminal without color.
/// </summary>
internal static class OutputHighlighting
{
    private const string _xshd = """
        <SyntaxDefinition name="DevTermOutput" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Error" foreground="#D03C3C" fontWeight="bold" />
          <Color name="Status" foreground="#8A8A8A" fontStyle="italic" />
          <RuleSet>
            <Rule color="Error">^\[error\].*$</Rule>
            <Rule color="Status">^\[dev-term\].*$</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private static readonly Lazy<IHighlightingDefinition> _definition = new(() =>
    {
        using var reader = XmlReader.Create(new StringReader(_xshd));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    });

    public static IHighlightingDefinition Definition => _definition.Value;
}
