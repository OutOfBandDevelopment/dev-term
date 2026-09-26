using System.Drawing;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// A layout checker for a real, laid-out Terminal.Gui view tree (call it after
/// <c>LayoutAndDraw(true)</c>): walks every visible view under a window and reports, naming the
/// offending view and its frame,
/// <list type="bullet">
/// <item><b>containment</b> - a view's frame lies inside its SuperView's content area (the content
/// size, which is larger than the viewport for a scrolling container), and the window fits the screen;</item>
/// <item><b>overlap</b> - visible siblings don't overlap (a button's shadow, which lives in its
/// <c>Margin</c>, doesn't count; deliberate overlays go in <see cref="TuiLayoutOptions.AllowOverlap"/>);</item>
/// <item><b>text fit</b> - a <c>Label</c>/<c>Button</c>/<c>CheckBox</c> shows all of its text: every
/// non-space character of its (hotkey-stripped) text appears in the lines its <c>TextFormatter</c>
/// actually produced for its size. A wrapped label passes only if its height holds every wrapped line;</item>
/// <item><b>reachability</b> - every focusable input view (button, field, check box, list...) has a
/// non-empty size, and every ancestor it's inside has a non-empty viewport (so it's either on screen
/// or inside a container that scrolls to it);</item>
/// <item><b>rendered text</b> (buffer level) - every text view that's fully on screen really drew its
/// whole text into the screen buffer, row by row (catches text overdrawn by a sibling or cut by a border).</item>
/// </list>
/// </summary>
/// <remarks>
/// A plain container <see cref="View"/> (no border, no text - a form's section body, say) is measured
/// by the union of its visible children rather than its own frame: the control panel deliberately
/// gives each section body a fixed, generous width so a wide row can lay out at its natural width,
/// and that invisible extent isn't a layout problem - only what's drawn in it is.
/// </remarks>
internal static class TuiLayoutAssert
{
    /// <summary>Asserts the laid-out tree under <paramref name="root"/> has no layout problems; the failure lists all of them.</summary>
    public static void Check(IApplication app, View root, TuiLayoutOptions? options = null)
    {
        var problems = FindProblems(app, root, options);
        if (problems.Count > 0)
        {
            Assert.Fail($"{problems.Count} layout problem(s) in {Describe(root)} at {app.Screen.Width}x{app.Screen.Height}:\n  " + string.Join("\n  ", problems));
        }
    }

    public static IReadOnlyList<string> FindProblems(IApplication app, View root, TuiLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(root);
        options ??= new TuiLayoutOptions();
        var problems = new List<string>();

        var screen = app.Screen;
        if (!Contains(screen, root.Frame))
        {
            problems.Add($"{Describe(root)} doesn't fit the {screen.Width}x{screen.Height} screen.");
        }

        Walk(app, root, options, problems);
        if (options.CheckRenderedText)
        {
            CheckRenderedText(app, root, options, problems);
        }

        return problems;
    }

    private static void Walk(IApplication app, View view, TuiLayoutOptions options, List<string> problems)
    {
        var children = view.SubViews.Where(c => c.Visible && !options.Ignores(c)).ToList();
        var content = new Rectangle(Point.Empty, view.GetContentSize());

        foreach (var child in children)
        {
            var extent = Extent(child);
            if (extent is { } rect && !Contains(content, rect))
            {
                problems.Add($"{Describe(child)} extends outside its SuperView {Describe(view)}'s content area {Format(content)}: {Format(rect)}.");
            }

            CheckSize(child, problems);
            CheckText(child, problems);
        }

        for (var i = 0; i < children.Count; i++)
        {
            for (var j = i + 1; j < children.Count; j++)
            {
                var a = children[i];
                var b = children[j];
                if (options.AllowsOverlap(a) || options.AllowsOverlap(b))
                {
                    continue;
                }

                var solidA = Solids(a);
                var solidB = Solids(b);
                if (FirstOverlap(solidA, solidB) is { } both)
                {
                    problems.Add($"{Describe(a)} and {Describe(b)} overlap in {Describe(view)}: {Format(both)}.");
                }
                else if (FirstOverlap(Shadows(a), solidB) is { } underA)
                {
                    problems.Add($"The shadow of {Describe(a)} is drawn over {Describe(b)} in {Describe(view)}: {Format(underA)}.");
                }
                else if (FirstOverlap(Shadows(b), solidA) is { } underB)
                {
                    problems.Add($"The shadow of {Describe(b)} is drawn over {Describe(a)} in {Describe(view)}: {Format(underB)}.");
                }
            }
        }

        foreach (var child in children)
        {
            if (child.Viewport.Width <= 0 || child.Viewport.Height <= 0)
            {
                if (IsInput(child) || child.SubViews.Any(c => c.Visible && IsInput(c)))
                {
                    problems.Add($"{Describe(child)} has an empty viewport {Format(child.Viewport)}, so nothing in it can be seen or reached.");
                }

                continue;
            }

            Walk(app, child, options, problems);
        }
    }

    private static void CheckSize(View view, List<string> problems)
    {
        if ((IsInput(view) || IsText(view)) && (view.Frame.Width <= 0 || view.Frame.Height <= 0) && !string.IsNullOrEmpty(TextOf(view)))
        {
            problems.Add($"{Describe(view)} has collapsed to {Format(view.Frame)}.");
        }
        else if (IsInput(view) && (view.Frame.Width <= 0 || view.Frame.Height <= 0))
        {
            problems.Add($"{Describe(view)} (focusable) has collapsed to {Format(view.Frame)} and can't be reached.");
        }
    }

    private static void CheckText(View view, List<string> problems)
    {
        if (!IsText(view) || view.Viewport.Width <= 0 || view.Viewport.Height <= 0)
        {
            return;
        }

        var source = Significant(StripHotKey(view, view.TextFormatter.Text ?? string.Empty));
        if (source.Length == 0)
        {
            return;
        }

        var lines = view.TextFormatter.GetLines();
        var shown = Significant(string.Concat(lines.Take(view.Viewport.Height)));
        if (shown != source)
        {
            problems.Add($"{Describe(view)} is cut off: its {Format(view.Viewport)} viewport shows \"{string.Join("|", lines.Take(view.Viewport.Height))}\" of \"{view.TextFormatter.Text}\".");
        }
    }

    private static void CheckRenderedText(IApplication app, View top, TuiLayoutOptions options, List<string> problems)
    {
        var contents = app.Driver?.GetOutputBuffer().Contents;
        if (contents is null)
        {
            return;
        }

        var screen = app.Screen;
        foreach (var view in Descendants(top, options).Where(IsText))
        {
            if (view.Viewport.Width <= 0 || view.Viewport.Height <= 0)
            {
                continue;
            }

            var onScreen = view.ViewportToScreen(new Rectangle(Point.Empty, view.Viewport.Size));
            if (!IsFullyVisible(view, onScreen) || !Contains(screen, onScreen))
            {
                continue;
            }

            var lines = view.TextFormatter.GetLines();
            for (var line = 0; line < lines.Count && line < onScreen.Height; line++)
            {
                var expected = Significant(lines[line]);
                if (expected.Length == 0)
                {
                    continue;
                }

                var row = new StringBuilder();
                for (var col = onScreen.X; col < onScreen.Right; col++)
                {
                    row.Append(contents[onScreen.Y + line, col].Grapheme);
                }

                if (!Significant(row.ToString()).Contains(expected, StringComparison.Ordinal))
                {
                    problems.Add($"{Describe(view)} didn't render its text on screen row {onScreen.Y + line}: expected \"{lines[line]}\", the screen shows \"{row}\".");
                }
            }
        }
    }

    /// <summary>Whether <paramref name="onScreen"/> is inside every ancestor's visible viewport - i.e. not scrolled or clipped out.</summary>
    private static bool IsFullyVisible(View view, Rectangle onScreen)
    {
        for (var ancestor = view.SuperView; ancestor is not null; ancestor = ancestor.SuperView)
        {
            var visible = ancestor.ViewportToScreen(new Rectangle(Point.Empty, ancestor.Viewport.Size));
            if (!Contains(visible, onScreen))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<View> Descendants(View view, TuiLayoutOptions options)
    {
        foreach (var child in view.SubViews.Where(c => c.Visible && !options.Ignores(c)))
        {
            yield return child;
            foreach (var descendant in Descendants(child, options))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// What a view takes up in its SuperView's content: its frame (a shadowless view's margin left
    /// out - it draws nothing), or for a plain container the union of what's shown in it.
    /// </summary>
    private static Rectangle? Extent(View view)
    {
        if (!IsPlainContainer(view))
        {
            return HasShadow(view) ? view.Frame : Solid(view);
        }

        Rectangle? union = null;
        foreach (var rect in ChildRects(view, Extent))
        {
            union = union is { } u ? Rectangle.Union(u, rect) : rect;
        }

        return union;
    }

    /// <summary>The areas a view draws over whatever is under it, in its SuperView's content coordinates: its frame less its margin (where a button's shadow goes) - for a plain container, each of its children's.</summary>
    private static List<Rectangle> Solids(View view)
    {
        if (IsPlainContainer(view))
        {
            return ChildRects(view, v => null, Solids);
        }

        return Solid(view) is { } solid ? [solid] : [];
    }

    private static Rectangle? Solid(View view)
    {
        var frame = view.Frame;
        if (view.Margin is { } margin)
        {
            var thickness = margin.Thickness;
            frame = new Rectangle(frame.X + thickness.Left, frame.Y + thickness.Top, Math.Max(0, frame.Width - thickness.Horizontal), Math.Max(0, frame.Height - thickness.Vertical));
        }

        return frame.Width > 0 && frame.Height > 0 ? frame : null;
    }

    /// <summary>Where a view's shadow is drawn (its frame less its solid part), in its SuperView's content coordinates - for a plain container, each of its children's.</summary>
    private static List<Rectangle> Shadows(View view)
    {
        if (IsPlainContainer(view))
        {
            return ChildRects(view, v => null, Shadows);
        }

        if (!HasShadow(view) || Solid(view) is not { } solid)
        {
            return [];
        }

        var frame = view.Frame;
        return
        [
            new Rectangle(solid.Right, frame.Y, frame.Right - solid.Right, frame.Height),
            new Rectangle(frame.X, solid.Bottom, frame.Width, frame.Bottom - solid.Bottom),
        ];
    }

    private static bool HasShadow(View view) => view.ShadowStyle != ShadowStyles.None && view.Margin is { } margin && margin.Thickness != Terminal.Gui.Drawing.Thickness.Empty;

    /// <summary>Each visible child's rect(s), moved from <paramref name="container"/>'s content into its SuperView's content coordinates.</summary>
    private static List<Rectangle> ChildRects(View container, Func<View, Rectangle?> one, Func<View, List<Rectangle>>? many = null)
    {
        var offset = container.GetViewportOffsetFromFrame();
        var dx = container.Frame.X + offset.X - container.Viewport.X;
        var dy = container.Frame.Y + offset.Y - container.Viewport.Y;
        var rects = new List<Rectangle>();
        foreach (var child in container.SubViews.Where(c => c.Visible))
        {
            foreach (var rect in many?.Invoke(child) ?? (one(child) is { } r ? [r] : []))
            {
                var moved = rect;
                moved.Offset(dx, dy);
                rects.Add(moved);
            }
        }

        return rects;
    }

    /// <summary>The first place any of <paramref name="covering"/> intersects any of <paramref name="covered"/>, if one does.</summary>
    private static Rectangle? FirstOverlap(List<Rectangle> covering, List<Rectangle> covered)
    {
        foreach (var a in covering.Where(r => r.Width > 0 && r.Height > 0))
        {
            foreach (var b in covered.Where(b => a.IntersectsWith(b)))
            {
                return Rectangle.Intersect(a, b);
            }
        }

        return null;
    }

    /// <summary>A borderless, textless, non-scrolling <see cref="View"/> that only groups others - a scroller (its own content size) is a real container instead.</summary>
    private static bool IsPlainContainer(View view) =>
        view.GetType() == typeof(View)
        && view.ContentSizeTracksViewport
        && string.IsNullOrEmpty(view.Text)
        && (view.Border is null || view.Border.Thickness == Terminal.Gui.Drawing.Thickness.Empty);

    private static bool IsText(View view) => view is Label or Button or CheckBox;

    private static bool IsInput(View view) =>
        view.CanFocus && view is Button or TextField or CheckBox or OptionSelector or ListView or Terminal.Gui.Editor.Editor;

    private static string TextOf(View view) => view.TextFormatter.Text ?? view.Text ?? string.Empty;

    private static string StripHotKey(View view, string text)
    {
        var specifier = view.HotKeySpecifier;
        if (specifier.Value == 0xFFFF || specifier.Value == 0)
        {
            return text;
        }

        var marker = specifier.ToString();
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? text : text.Remove(index, marker.Length);
    }

    /// <summary>The text with every whitespace (and empty-grapheme) character dropped - what has to be visible somewhere.</summary>
    private static string Significant(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch) && ch != '\0')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool Contains(Rectangle outer, Rectangle inner) =>
        inner.Width <= 0 || inner.Height <= 0 || outer.Contains(inner);

    private static string Format(Rectangle rect) => $"(x={rect.X}, y={rect.Y}, w={rect.Width}, h={rect.Height})";

    /// <summary>Names a view for a failure message: its type, then its Id, Title or Text (whichever it has), then its frame.</summary>
    internal static string Describe(View view)
    {
        var name = view.GetType().Name;
        var label = !string.IsNullOrEmpty(view.Id) ? $"#{view.Id}"
            : !string.IsNullOrEmpty(view.Title) ? $"\"{Shorten(view.Title)}\""
            : !string.IsNullOrEmpty(view.Text) ? $"\"{Shorten(view.Text)}\""
            : view.SuperView is { } parent ? $"(child {parent.SubViews.ToList().IndexOf(view)} of {parent.GetType().Name})"
            : string.Empty;
        return $"{name} {label} {Format(view.Frame)}".Replace("  ", " ", StringComparison.Ordinal);
    }

    private static string Shorten(string text)
    {
        var flat = text.Replace('\n', '|');
        return flat.Length > 40 ? flat[..39] + "…" : flat;
    }
}

/// <summary>Deliberate exceptions for <see cref="TuiLayoutAssert"/> - each should say why in a comment at the call site.</summary>
internal sealed class TuiLayoutOptions
{
    /// <summary>Views allowed to overlap their siblings (an overlay, a popover).</summary>
    public List<View> AllowOverlap { get; } = [];

    /// <summary>Views (and everything under them) left out of every check.</summary>
    public List<View> Ignore { get; } = [];

    /// <summary>Also compare what's in the screen buffer with each fully visible text view's text. Off when something is drawn over the window (an open menu, a dialog).</summary>
    public bool CheckRenderedText { get; set; } = true;

    internal bool AllowsOverlap(View view) => AllowOverlap.Contains(view);

    internal bool Ignores(View view) => Ignore.Contains(view);
}
