using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using TgColor = Terminal.Gui.Drawing.Color;

namespace DevTerm.Console.Tests;

/// <summary>
/// Legacy Windows conhost sessions (no VT processing enabled at startup) get permanently forced into
/// Terminal.Gui's 16-color mode (<c>WindowsOutput</c>/<c>OutputBase.IsLegacyConsole</c> -&gt;
/// <c>Force16Colors</c>, irreversible for the session). Every truecolor value then renders as whichever
/// of the 16 named ANSI colors is closest by Euclidean RGB distance
/// (<see cref="TgColor.GetClosestNamedColor16"/>). Two theme roles that must stay visually distinct
/// (a focused field vs. a merely-editable one) collapse onto the same rendered color if they snap to
/// the same named color - this is the root cause behind TODO.md's "startup editor looks unthemed in
/// legacy conhost: invisible field backgrounds, faint focus". See docs/design/theming.md and
/// <see cref="BuiltInThemes.Dark"/>'s doc comment on <see cref="ThemeRole.SelectionBackground"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class LegacyConsole16ColorTests
{
    private static TgColor Closest(DevTermTheme theme, ThemeRole role)
    {
        var color = theme[role];
        return new TgColor(color.R, color.G, color.B).GetClosestNamedColor16();
    }

    [TestMethod]
    public void Dark_FocusedFieldBackground_DoesNotCollapseOntoAnUnfocusedFieldBackground()
    {
        var selection = Closest(BuiltInThemes.Dark, ThemeRole.SelectionBackground);
        var field = Closest(BuiltInThemes.Dark, ThemeRole.FieldBackground);
        var hover = Closest(BuiltInThemes.Dark, ThemeRole.ControlHoverBackground);

        Assert.AreNotEqual(field, selection, "A focused field must not render the same as a merely-editable one under a 16-color downgrade.");
        Assert.AreNotEqual(hover, selection, "A focused field must not render the same as a hovered-but-unfocused one under a 16-color downgrade.");
    }

    [TestMethod]
    public void Dark_FocusedFieldBackground_AlsoDiffersFromTheWindowBackground()
    {
        var selection = Closest(BuiltInThemes.Dark, ThemeRole.SelectionBackground);
        var background = Closest(BuiltInThemes.Dark, ThemeRole.Background);

        Assert.AreNotEqual(background, selection);
    }
}
