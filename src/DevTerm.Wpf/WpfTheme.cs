using System.Windows;
using System.Windows.Media;
using DevTerm.Configuration;

namespace DevTerm.Wpf;

/// <summary>
/// Maps a <see cref="DevTermTheme"/>'s roles onto WPF. Every window calls <see cref="Attach"/> once
/// after <c>InitializeComponent</c>; that merges a per-window <see cref="ThemeDictionary"/> into its
/// resources holding:
/// <list type="bullet">
/// <item>one frozen brush per <see cref="ThemeRole"/>, under <see cref="Key"/> (<c>"DevTerm.OutputError"</c>),
/// which XAML and code-behind reference with <c>DynamicResource</c>/<c>SetResourceReference</c> - so a
/// live switch is just swapping the dictionary;</item>
/// <item>for any theme other than the stock <see cref="BuiltInThemes.Light"/>, overrides of the
/// <see cref="SystemColors"/> brush keys the stock (Aero2) control styles read their defaults from
/// (window/control/menu/highlight/gray-text), which re-colors text boxes, lists, menus and the status
/// bar without replacing their templates;</item>
/// <item>for a dark theme (<see cref="DevTermTheme.IsDark"/>), <see cref="DarkControls"/>: replacement
/// templates for the few stock controls whose chrome is hard-coded light (Button, ComboBox,
/// drop-down menu items), which would otherwise show light-on-light text.</item>
/// </list>
/// .NET's Fluent theme (<c>ThemeMode</c>) was tried first and not used - see docs/design/theming.md.
/// A theme switch reaches every attached window through <see cref="ActiveTheme.Changed"/>, marshaled
/// to each window's own dispatcher.
/// </summary>
public static class WpfTheme
{
    private static readonly Lock _lock = new();
    private static readonly List<WeakReference<Window>> _windows = [];
    private static Application? _application;

    static WpfTheme()
    {
        ActiveTheme.Changed += (_, _) => ReapplyAll();
    }

    /// <summary>A boxed <see cref="bool"/> resource: whether the theme's charts use <c>ChartPalette.DarkSlots</c>.</summary>
    public const string ChartPaletteDarkKey = "DevTerm.ChartPaletteDark";

    /// <summary>The resource key of <paramref name="role"/>'s brush: <c>"DevTerm." + role</c>.</summary>
    public static string Key(ThemeRole role) => "DevTerm." + role;

    public static Color ToColor(ThemeColor color) => Color.FromRgb(color.R, color.G, color.B);

    /// <summary>A frozen brush for <paramref name="role"/> in <paramref name="theme"/> (default: the current theme).</summary>
    public static SolidColorBrush Brush(ThemeRole role, DevTermTheme? theme = null)
    {
        var brush = new SolidColorBrush(ToColor((theme ?? ActiveTheme.Current)[role]));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Binds <paramref name="property"/> of <paramref name="element"/> to <paramref name="role"/>'s brush as a
    /// resource reference (follows a live switch) and returns the element - for code-built controls,
    /// e.g. <c>new TextBlock { ... }.Themed(TextBlock.ForegroundProperty, ThemeRole.MutedForeground)</c>.
    /// </summary>
    public static T Themed<T>(this T element, DependencyProperty property, ThemeRole role)
        where T : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetResourceReference(property, Key(role));
        return element;
    }

    /// <summary>Themes <paramref name="window"/> now and on every later switch, until it closes.</summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Apply(window, ActiveTheme.Current);
        window.SetResourceReference(Window.BackgroundProperty, Key(ThemeRole.Background));
        window.SetResourceReference(Window.ForegroundProperty, Key(ThemeRole.Foreground));

        var reference = new WeakReference<Window>(window);
        lock (_lock)
        {
            _windows.RemoveAll(existing => !existing.TryGetTarget(out _));
            _windows.Add(reference);
        }

        window.Closed += (_, _) =>
        {
            lock (_lock)
            {
                _windows.Remove(reference);
            }
        };
    }

    /// <summary>
    /// Themes the application's own resources too (the running app only - tests have no
    /// <see cref="Application"/>): popups that aren't part of any window's resource tree, such as a
    /// text box's built-in context menu, only find resources there.
    /// </summary>
    public static void AttachApplication(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
        Apply(application.Resources, ActiveTheme.Current);
    }

    /// <summary>Replaces <paramref name="window"/>'s theme dictionary with one built from <paramref name="theme"/>.</summary>
    public static void Apply(Window window, DevTermTheme theme)
    {
        ArgumentNullException.ThrowIfNull(window);
        Apply(window.Resources, theme);
    }

    private static void Apply(ResourceDictionary resources, DevTermTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var merged = resources.MergedDictionaries;
        var existing = merged.OfType<ThemeDictionary>().FirstOrDefault();
        var replacement = new ThemeDictionary(theme);
        if (existing is null)
        {
            merged.Insert(0, replacement);
        }
        else
        {
            merged[merged.IndexOf(existing)] = replacement;
        }
    }

    /// <summary>The theme a window's resources currently hold (tests).</summary>
    internal static DevTermTheme? AppliedTo(Window window) =>
        window.Resources.MergedDictionaries.OfType<ThemeDictionary>().FirstOrDefault()?.Theme;

    private static void ReapplyAll()
    {
        var theme = ActiveTheme.Current;
        List<Window> windows;
        lock (_lock)
        {
            windows = [.. _windows.Select(reference => reference.TryGetTarget(out var window) ? window : null).OfType<Window>()];
        }

        if (_application is { } application)
        {
            if (application.Dispatcher.CheckAccess())
            {
                Apply(application.Resources, theme);
            }
            else if (!application.Dispatcher.HasShutdownStarted)
            {
                application.Dispatcher.BeginInvoke(() => Apply(application.Resources, theme));
            }
        }

        foreach (var window in windows)
        {
            if (window.Dispatcher.CheckAccess())
            {
                Apply(window, theme);
            }
            else if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Dispatcher.BeginInvoke(() => Apply(window, theme));
            }
        }
    }

    /// <summary>The per-window dictionary <see cref="Apply"/> merges in; a distinct type so it can be found and replaced.</summary>
    internal sealed class ThemeDictionary : ResourceDictionary
    {
        public ThemeDictionary(DevTermTheme theme)
        {
            Theme = theme;
            foreach (var role in Enum.GetValues<ThemeRole>())
            {
                this[Key(role)] = Brush(role, theme);
            }

            this[ChartPaletteDarkKey] = theme.ChartPalette == ChartPaletteVariant.Dark;

            if (ReferenceEquals(theme, BuiltInThemes.Light))
            {
                // The stock look: leave WPF's own system colors and control templates alone.
                return;
            }

            this[SystemColors.WindowBrushKey] = Brush(ThemeRole.ControlBackground, theme);
            this[SystemColors.WindowTextBrushKey] = Brush(ThemeRole.ControlForeground, theme);
            this[SystemColors.ControlBrushKey] = Brush(ThemeRole.Background, theme);
            this[SystemColors.ControlTextBrushKey] = Brush(ThemeRole.Foreground, theme);
            this[SystemColors.ControlLightBrushKey] = Brush(ThemeRole.ControlBorder, theme);
            this[SystemColors.ControlDarkBrushKey] = Brush(ThemeRole.ControlBorder, theme);
            this[SystemColors.MenuBrushKey] = Brush(ThemeRole.MenuBackground, theme);
            this[SystemColors.MenuBarBrushKey] = Brush(ThemeRole.MenuBackground, theme);
            this[SystemColors.MenuTextBrushKey] = Brush(ThemeRole.MenuForeground, theme);
            this[SystemColors.HighlightBrushKey] = Brush(ThemeRole.SelectionBackground, theme);
            this[SystemColors.HighlightTextBrushKey] = Brush(ThemeRole.SelectionForeground, theme);
            this[SystemColors.InactiveSelectionHighlightBrushKey] = Brush(ThemeRole.ControlHoverBackground, theme);
            this[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brush(ThemeRole.ControlForeground, theme);
            this[SystemColors.GrayTextBrushKey] = Brush(ThemeRole.MutedForeground, theme);
            this[SystemColors.InfoBrushKey] = Brush(ThemeRole.MenuBackground, theme);
            this[SystemColors.InfoTextBrushKey] = Brush(ThemeRole.MenuForeground, theme);

            if (theme.IsDark)
            {
                MergedDictionaries.Add(new DarkControls());
            }
        }

        public DevTermTheme Theme { get; }
    }
}
