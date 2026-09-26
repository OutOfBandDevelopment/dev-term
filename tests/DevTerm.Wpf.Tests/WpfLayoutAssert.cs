using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// A deliberate exception to one <see cref="WpfLayoutAssert"/> check: an element the check would
/// flag that is fine by design (an overlay, a color swatch). Every allowance names why.
/// </summary>
/// <param name="Check">The check it applies to (a <see cref="WpfLayoutAssert"/> <c>*Check</c> constant).</param>
/// <param name="Match">Which elements it covers.</param>
/// <param name="Reason">Why this is fine - shown nowhere, but required so nobody adds one without saying.</param>
internal sealed record LayoutAllowance(string Check, Func<FrameworkElement, bool> Match, string Reason);

/// <summary>One thing <see cref="WpfLayoutAssert"/> found wrong.</summary>
internal sealed record LayoutProblem(string Check, string Element, Rect Bounds, string Detail)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{Check}] {Element} at ({Bounds.X:0.#},{Bounds.Y:0.#} {Bounds.Width:0.#}x{Bounds.Height:0.#}): {Detail}");
}

/// <summary>
/// Checks a real, laid-out WPF visual tree for the layout problems a reviewer looking at a screenshot
/// would flag, so a layout regression fails a test instead of waiting for someone to notice it:
/// <list type="bullet">
/// <item><see cref="BoundsCheck"/>: every visible element lies inside the window's client area, or inside
/// a <see cref="ScrollViewer"/> that can scroll to it (an axis whose scroll bar is <c>Disabled</c> can't).</item>
/// <item><see cref="OverlapCheck"/>: visible siblings in the same panel don't overlap (a <see cref="Grid"/>'s
/// children only when they're in different cells; a <see cref="Canvas"/> is never checked).</item>
/// <item><see cref="ClipCheck"/>: no element is arranged smaller than it needs (WPF's layout clip), and no
/// non-wrapping <see cref="TextBlock"/>'s text is wider than the block (trimmed or cut off) - unless it
/// trims on purpose and has a tooltip carrying the full text.</item>
/// <item><see cref="SizeCheck"/>: interactive controls (Button, TextBox, ComboBox, CheckBox, RadioButton,
/// Slider) aren't zero-sized or smaller than usable.</item>
/// <item><see cref="ContrastCheck"/>: each enabled TextBlock's/TextBox's text has at least
/// <see cref="LayoutCheckOptions.MinContrast"/> WCAG contrast against its effective background (the
/// first non-transparent background found walking up the tree, blended if translucent).</item>
/// <item><see cref="LightChromeCheck"/> (dark themes only): no visible surface of a real size is painted
/// a light color - the stock controls' hard-coded light chrome.</item>
/// </list>
/// Template internals (a Control's own template parts) are checked for bounds, clipping and contrast
/// (that is where a button's clipped caption or a dark theme's unreadable text actually lives) but
/// not for overlap or minimum size, which are the template author's business.
/// </summary>
internal static class WpfLayoutAssert
{
    public const string BoundsCheck = "bounds";
    public const string OverlapCheck = "overlap";
    public const string ClipCheck = "clip";
    public const string SizeCheck = "size";
    public const string ContrastCheck = "contrast";
    public const string LightChromeCheck = "light-chrome";

    private const double _tolerance = 0.75;

    /// <summary>Checks <paramref name="root"/> and fails with every problem found, one per line.</summary>
    public static void Check(FrameworkElement root, LayoutCheckOptions? options = null, string? context = null)
    {
        var problems = Find(root, options);
        if (problems.Count > 0)
        {
            Assert.Fail(Format(problems, context));
        }
    }

    public static string Format(IReadOnlyList<LayoutProblem> problems, string? context)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{problems.Count} layout problem(s){(context is null ? string.Empty : " in " + context)}:");
        foreach (var problem in problems)
        {
            builder.AppendLine().Append("  ").Append(problem);
        }

        return builder.ToString();
    }

    /// <summary>Every problem in <paramref name="root"/>'s visual tree, minus the allowed ones.</summary>
    public static IReadOnlyList<LayoutProblem> Find(FrameworkElement root, LayoutCheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        options ??= new LayoutCheckOptions();
        var problems = new List<LayoutProblem>();
        var clipped = new HashSet<FrameworkElement>();
        var rootRect = new Rect(0, 0, root.ActualWidth, root.ActualHeight);

        foreach (var element in Descendants(root))
        {
            if (!element.IsVisible)
            {
                continue;
            }

            void Report(string check, Rect bounds, string detail)
            {
                if (!options.Allowed.Any(a => a.Check == check && a.Match(element)))
                {
                    problems.Add(new LayoutProblem(check, Describe(element), bounds, detail));
                }
            }

            var sized = element.ActualWidth > 0 && element.ActualHeight > 0;
            var bounds = sized ? BoundsIn(element, root) : Rect.Empty;

            if (IsInteractive(element) && !IsTemplatePart(element))
            {
                CheckSize(element, bounds, options, Report);
            }

            if (!sized)
            {
                continue;
            }

            CheckBounds(element, root, rootRect, bounds, Report);
            if (!HasClippedAncestor(element, clipped) && CheckClip(element, bounds, Report))
            {
                clipped.Add(element);
            }

            if (element is Panel panel && panel is not Canvas && (!IsTemplatePart(panel) || panel.IsItemsHost))
            {
                CheckOverlap(panel, root, options, problems);
            }

            if (element.IsEnabled && (element is TextBlock || (element is TextBox && !IsTemplatePart(element))))
            {
                CheckContrast(element, root, bounds, options, Report);
            }

            if (options.IsDarkTheme)
            {
                CheckLightChrome(element, bounds, Report);
            }
        }

        return problems;
    }

    private static void CheckSize(FrameworkElement element, Rect bounds, LayoutCheckOptions options, Action<string, Rect, string> report)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            report(SizeCheck, bounds, "visible but zero-sized");
            return;
        }

        var minHeight = element is CheckBox or RadioButton ? options.MinToggleHeight : options.MinControlHeight;
        if (element.ActualHeight + _tolerance < minHeight || element.ActualWidth + _tolerance < options.MinControlWidth)
        {
            report(SizeCheck, bounds, string.Create(CultureInfo.InvariantCulture, $"smaller than usable (min {options.MinControlWidth}x{minHeight})"));
        }
    }

    private static void CheckBounds(FrameworkElement element, FrameworkElement root, Rect rootRect, Rect bounds, Action<string, Rect, string> report)
    {
        // Only the part that isn't already clipped by its own slot (the clip check reports that).
        var container = rootRect;
        bool horizontal = true, vertical = true;
        if (FindScrollPresenter(element) is { } presenter)
        {
            var viewer = presenter.ScrollOwner ?? presenter.TemplatedParent as ScrollViewer;
            container = BoundsIn(presenter, root);
            horizontal = viewer is null || viewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled;
            vertical = viewer is null || viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled;
        }

        var outside = (horizontal && (bounds.Left < container.Left - _tolerance || bounds.Right > container.Right + _tolerance))
            || (vertical && (bounds.Top < container.Top - _tolerance || bounds.Bottom > container.Bottom + _tolerance));
        if (outside)
        {
            report(BoundsCheck, bounds, string.Create(CultureInfo.InvariantCulture, $"extends outside its visible area ({container.X:0.#},{container.Y:0.#} {container.Width:0.#}x{container.Height:0.#}) and can't be scrolled to"));
        }
    }

    private static bool CheckClip(FrameworkElement element, Rect bounds, Action<string, Rect, string> report)
    {
        // Parts that clip by nature: a scroll viewport, a text box's own text view, a popup/adorner.
        if (element is ScrollContentPresenter || element.GetType().Name is "TextBoxView" || IsInsideTextBox(element))
        {
            return false;
        }

        if (LayoutInformation.GetLayoutClip(element) is { } clip && !clip.Bounds.IsEmpty)
        {
            var visible = clip.Bounds;
            if (visible.Width + _tolerance < element.ActualWidth || visible.Height + _tolerance < element.ActualHeight)
            {
                var text = FirstText(element);
                report(ClipCheck, bounds, string.Create(CultureInfo.InvariantCulture, $"needs {element.ActualWidth:0.#}x{element.ActualHeight:0.#} but only {visible.Width:0.#}x{visible.Height:0.#} is shown{(text is null ? string.Empty : $" (text \"{Shorten(text)}\")")}"));
                return true;
            }
        }

        if (element is TextBlock { TextWrapping: TextWrapping.NoWrap } block && !string.IsNullOrEmpty(block.Text))
        {
            var needed = TextWidth(block) + block.Padding.Left + block.Padding.Right;
            var deliberate = block.TextTrimming != TextTrimming.None && (block.ToolTip is not null || (block.Parent as FrameworkElement)?.ToolTip is not null);
            if (!deliberate && needed > block.ActualWidth + 1.0)
            {
                report(ClipCheck, bounds, string.Create(CultureInfo.InvariantCulture, $"text needs {needed:0.#}px but the block is {block.ActualWidth:0.#}px{(block.TextTrimming == TextTrimming.None ? " (cut off)" : " (trimmed, no tooltip)")}"));
                return true;
            }
        }

        return false;
    }

    private static void CheckOverlap(Panel panel, FrameworkElement root, LayoutCheckOptions options, List<LayoutProblem> problems)
    {
        var children = panel.Children.OfType<FrameworkElement>()
            .Where(c => c.IsVisible && c.ActualWidth > 0 && c.ActualHeight > 0)
            .Select(c => (Element: c, Bounds: BoundsIn(c, root)))
            .ToList();
        for (var i = 0; i < children.Count; i++)
        {
            for (var j = i + 1; j < children.Count; j++)
            {
                var (a, aBounds) = children[i];
                var (b, bBounds) = children[j];
                if (panel is Grid && !InDifferentCells(a, b))
                {
                    continue;
                }

                var overlap = Rect.Intersect(aBounds, bBounds);
                if (overlap.IsEmpty || overlap.Width <= 1 || overlap.Height <= 1)
                {
                    continue;
                }

                if (options.Allowed.Any(x => x.Check == OverlapCheck && (x.Match(a) || x.Match(b))))
                {
                    continue;
                }

                problems.Add(new LayoutProblem(OverlapCheck, Describe(a), aBounds, $"overlaps sibling {Describe(b)} by {overlap.Width.ToString("0.#", CultureInfo.InvariantCulture)}x{overlap.Height.ToString("0.#", CultureInfo.InvariantCulture)}"));
            }
        }
    }

    private static bool InDifferentCells(FrameworkElement a, FrameworkElement b)
    {
        static (int Start, int End) Span(int start, int span) => (start, start + Math.Max(1, span) - 1);
        var aRows = Span(Grid.GetRow(a), Grid.GetRowSpan(a));
        var bRows = Span(Grid.GetRow(b), Grid.GetRowSpan(b));
        var aColumns = Span(Grid.GetColumn(a), Grid.GetColumnSpan(a));
        var bColumns = Span(Grid.GetColumn(b), Grid.GetColumnSpan(b));
        var rowsShared = aRows.Start <= bRows.End && bRows.Start <= aRows.End;
        var columnsShared = aColumns.Start <= bColumns.End && bColumns.Start <= aColumns.End;
        return !(rowsShared && columnsShared);
    }

    private static void CheckContrast(FrameworkElement element, FrameworkElement root, Rect bounds, LayoutCheckOptions options, Action<string, Rect, string> report)
    {
        var text = element is TextBlock block ? block.Text : ((TextBox)element).Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var foreground = element is TextBlock tb ? tb.Foreground : ((TextBox)element).Foreground;
        if (foreground is not SolidColorBrush { } brush || EffectiveBackground(element) is not { } background)
        {
            return;
        }

        var color = Blend(brush.Color, brush.Opacity * EffectiveOpacity(element, root), background);
        var ratio = ContrastRatio(color, background);

        // A lone symbol (an info glyph, an arrow) is a graphical object, not text: WCAG's 3:1 non-text minimum.
        var minimum = text.Trim().Length <= 2 && !text.Any(char.IsLetterOrDigit) ? Math.Min(options.MinContrast, 3.0) : options.MinContrast;
        if (ratio + 0.005 < minimum)
        {
            report(ContrastCheck, bounds, string.Create(CultureInfo.InvariantCulture, $"text {Hex(color)} on {Hex(background)} has contrast {ratio:0.00}:1 (min {minimum:0.0}:1)"));
        }
    }

    private static void CheckLightChrome(FrameworkElement element, Rect bounds, Action<string, Rect, string> report)
    {
        // A template's own glyphs count too (a check box's box, a slider's thumb); a shape the window
        // draws itself (a status dot) is colored on purpose.
        var brush = element is Shape shape && IsTemplatePart(element) ? shape.Fill : OwnBackground(element);
        var minimum = element is Shape ? 8 : 12;
        if (bounds.Width < minimum || bounds.Height < minimum)
        {
            return;
        }

        if (brush is SolidColorBrush { } solid && solid.Color.A >= 200 && solid.Opacity >= 0.8 && RelativeLuminance(solid.Color) > 0.35)
        {
            report(LightChromeCheck, bounds, $"painted {Hex(solid.Color)} (light) in a dark theme");
        }

        // A bright frame line (the stock GroupBox's hard-coded white inner border).
        if (element is Border { BorderBrush: SolidColorBrush { } line } border && border.BorderThickness != default && line.Color.A >= 200 && RelativeLuminance(line.Color) > 0.6)
        {
            report(LightChromeCheck, bounds, $"framed in {Hex(line.Color)} (light) in a dark theme");
        }
    }

    /// <summary>The background the element's text is drawn over: layers found walking up the visual tree, blended down to the first opaque one.</summary>
    internal static Color? EffectiveBackground(DependencyObject element)
    {
        var layers = new List<(Color Color, double Opacity)>();
        for (DependencyObject? current = element; current is not null; current = Parent(current))
        {
            var brush = current is FrameworkElement fe ? OwnBackground(fe) : null;
            if (brush is null)
            {
                continue;
            }

            if (brush is not SolidColorBrush solid)
            {
                // A gradient or image: its color under the text is unknowable here.
                return null;
            }

            var opacity = solid.Color.A / 255.0 * solid.Opacity;
            if (opacity <= 0)
            {
                continue;
            }

            layers.Add((Color.FromRgb(solid.Color.R, solid.Color.G, solid.Color.B), opacity));
            if (opacity >= 0.999)
            {
                break;
            }
        }

        if (layers.Count == 0)
        {
            return null;
        }

        var result = Colors.White;
        for (var i = layers.Count - 1; i >= 0; i--)
        {
            result = Blend(layers[i].Color, layers[i].Opacity, result);
        }

        return result;
    }

    private static Brush? OwnBackground(FrameworkElement element) => element switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        TextBlock block => block.Background,
        Window window => window.Background,
        _ => null,
    };

    /// <summary>
    /// The element's opacity times its ancestors', up to but not including <paramref name="root"/> - a
    /// popup's content fades in (its root's opacity animates), which says nothing about its colors.
    /// </summary>
    private static double EffectiveOpacity(DependencyObject element, DependencyObject root)
    {
        var opacity = 1.0;
        for (var current = element; current is not null && !ReferenceEquals(current, root); current = Parent(current))
        {
            if (current is UIElement ui)
            {
                opacity *= ui.Opacity;
            }
        }

        return opacity;
    }

    internal static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    internal static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static Color Blend(Color top, double opacity, Color bottom)
    {
        opacity = Math.Clamp(opacity * (top.A / 255.0), 0, 1);
        byte Mix(byte t, byte b) => (byte)Math.Round((t * opacity) + (b * (1 - opacity)));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static double TextWidth(TextBlock block)
    {
        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        var formatted = new FormattedText(block.Text, CultureInfo.CurrentUICulture, block.FlowDirection, typeface, block.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(block).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static bool IsInteractive(FrameworkElement element) =>
        element is Button or TextBox or ComboBox or CheckBox or RadioButton or Slider;

    /// <summary>A part of some Control's own template (not something the window's author placed).</summary>
    internal static bool IsTemplatePart(FrameworkElement element) => element.TemplatedParent is Control;

    private static bool IsInsideTextBox(FrameworkElement element)
    {
        for (var current = Parent(element); current is not null; current = Parent(current))
        {
            if (current is TextBoxBase)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasClippedAncestor(FrameworkElement element, HashSet<FrameworkElement> clipped)
    {
        for (var current = Parent(element); current is not null; current = Parent(current))
        {
            if (current is FrameworkElement fe && clipped.Contains(fe))
            {
                return true;
            }
        }

        return false;
    }

    private static ScrollContentPresenter? FindScrollPresenter(FrameworkElement element)
    {
        for (var current = Parent(element); current is not null; current = Parent(current))
        {
            if (current is ScrollContentPresenter presenter)
            {
                return presenter;
            }
        }

        return null;
    }

    private static Rect BoundsIn(FrameworkElement element, FrameworkElement root)
    {
        var local = new Rect(element.RenderSize);
        if (LayoutInformation.GetLayoutClip(element) is { } clip && !clip.Bounds.IsEmpty)
        {
            local.Intersect(clip.Bounds);
            if (local.IsEmpty)
            {
                local = new Rect(element.RenderSize);
            }
        }

        return ReferenceEquals(element, root) ? local : element.TransformToAncestor(root).TransformBounds(local);
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    internal static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is FrameworkElement fe)
            {
                yield return fe;
            }

            for (var i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--)
            {
                stack.Push(VisualTreeHelper.GetChild(current, i));
            }
        }
    }

    /// <summary>"Type#Name "text"" plus the nearest named/labeled ancestor, for a failure message a person can act on.</summary>
    internal static string Describe(FrameworkElement element)
    {
        var builder = new StringBuilder(element.GetType().Name);
        if (!string.IsNullOrEmpty(element.Name))
        {
            builder.Append('#').Append(element.Name);
        }

        if (OwnText(element) is { } text)
        {
            builder.Append(" \"").Append(Shorten(text)).Append('"');
        }

        for (var current = Parent(element); current is not null; current = Parent(current))
        {
            if (current is FrameworkElement fe && (!string.IsNullOrEmpty(fe.Name) || (fe is HeaderedContentControl or HeaderedItemsControl && OwnText(fe) is not null)) && !IsTemplatePart(fe))
            {
                builder.Append(" in ").Append(fe.GetType().Name);
                if (!string.IsNullOrEmpty(fe.Name))
                {
                    builder.Append('#').Append(fe.Name);
                }
                else
                {
                    builder.Append(" \"").Append(Shorten(OwnText(fe)!)).Append('"');
                }

                break;
            }
        }

        return builder.ToString();
    }

    private static string? OwnText(FrameworkElement element) => element switch
    {
        TextBlock block => block.Text,
        TextBox box => box.Text,
        HeaderedContentControl { Header: string header } => header,
        HeaderedContentControl { Header: TextBlock header } => header.Text,
        HeaderedItemsControl { Header: string header } => header,
        ContentControl { Content: string content } => content,
        ContentControl { Content: TextBlock content } => content.Text,
        _ => null,
    };

    private static string? FirstText(FrameworkElement element) =>
        OwnText(element) ?? Descendants(element).OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

    private static string Shorten(string text)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length <= 40 ? text : text[..37] + "...";
    }
}

/// <summary>Thresholds and allowances for <see cref="WpfLayoutAssert"/>.</summary>
internal sealed class LayoutCheckOptions
{
    /// <summary>Minimum height of a Button/TextBox/ComboBox/Slider - the stock 12px-font control is about 20px.</summary>
    public double MinControlHeight { get; init; } = 18;

    /// <summary>Minimum height of a CheckBox/RadioButton - the stock glyph is 14px; its row with text about 16px.</summary>
    public double MinToggleHeight { get; init; } = 13;

    public double MinControlWidth { get; init; } = 14;

    /// <summary>WCAG AA for normal text.</summary>
    public double MinContrast { get; init; } = 4.5;

    /// <summary>Whether to run <see cref="WpfLayoutAssert.LightChromeCheck"/>.</summary>
    public bool IsDarkTheme { get; init; }

    public IReadOnlyList<LayoutAllowance> Allowed { get; init; } = [];
}
