using System.Collections.Concurrent;
using System.Xml;
using DevTerm.Configuration;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.Editor.Highlighting.Xshd;

namespace DevTerm.Console;

/// <summary>
/// Colors the TUI output pane per line, by the source tag every line already carries (see
/// <c>TuiMode.StatusLine</c>/<c>ErrorLine</c>): <c>[error] …</c> lines in the theme's
/// <see cref="ThemeRole.OutputError"/> and bold, <c>[dev-term] …</c> status lines in
/// <see cref="ThemeRole.OutputStatus"/> and italic, device output (<c>[ascii] …</c>) in the default color.
/// The pane is a single <c>Terminal.Gui.Editor</c>, which has no per-item styling like WPF's list does,
/// but it's an AvalonEdit port with a real syntax-highlighting engine, so a tiny XSHD definition whose
/// rules match whole lines by their tag is enough - generated per theme, since XSHD colors are fixed
/// at load. The tags stay in the text, so the distinction survives a terminal without color.
/// </summary>
internal static class OutputHighlighting
{
    private static readonly ConcurrentDictionary<DevTermTheme, IHighlightingDefinition> _definitions = new();

    /// <summary>The definition for the current theme (<see cref="ActiveTheme.Current"/>).</summary>
    public static IHighlightingDefinition Definition => For(ActiveTheme.Current);

    public static IHighlightingDefinition For(DevTermTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return _definitions.GetOrAdd(theme, static t =>
        {
            using var reader = XmlReader.Create(new StringReader(Xshd(t)));
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        });
    }

    /// <summary>The XSHD source for <paramref name="theme"/> - exposed for tests.</summary>
    internal static string Xshd(DevTermTheme theme) => $"""
        <SyntaxDefinition name="DevTermOutput" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Error" foreground="{theme[ThemeRole.OutputError].ToHex()}" fontWeight="bold" />
          <Color name="Status" foreground="{theme[ThemeRole.OutputStatus].ToHex()}" fontStyle="italic" />
          <RuleSet>
            <Rule color="Error">^\[error\].*$</Rule>
            <Rule color="Status">^\[dev-term\].*$</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;
}
