using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevTerm.Configuration;

namespace DevTerm.Wpf.Tests;

/// <summary>A size a window is reviewed at: its client area, as a real framed window of that outer size would have it.</summary>
internal sealed record ReviewSize(string Name, double Width, double Height);

/// <summary>
/// Drives <see cref="WpfLayoutAssert"/> across a window's sizes and saves a review screenshot of each
/// into <c>artifacts/ui-review/wpf/</c> (untracked - for a person to look over, not doc images). A
/// <see cref="UiLayoutReviewTests"/> test builds a window in the state it wants reviewed, then calls
/// <see cref="Review"/>, which shows it off-screen at its default size, its minimum size and a large
/// size, checks each, and fails once at the end listing every problem at every size.
/// </summary>
internal sealed class UiReview
{
    public static readonly string Directory = Path.Combine(FindRepoRoot(), "artifacts", "ui-review", "wpf");

    public static readonly ReviewSize Large = new("large", 1600, 1000);

    private readonly List<string> _failures = [];
    private readonly string _theme;
    private readonly Dictionary<Window, IReadOnlyList<ReviewSize>> _sizes = [];

    public UiReview(string theme)
    {
        _theme = theme;
        ActiveTheme.Select(theme, persist: false);
    }

    public bool IsDark => ActiveTheme.Current.IsDark;

    /// <summary>
    /// The frame a real window adds around its client area (the review shows windows without one,
    /// <see cref="WindowStyle.None"/>, so a "640x480" review is the client area a real 640x480 window
    /// has). 8+8 by 31+8 at 96 DPI on Windows 10/11.
    /// </summary>
    private static Thickness Frame => SystemParameters.WindowNonClientFrameThickness;

    /// <summary>
    /// The sizes to review <paramref name="window"/> at: its own default, its MinWidth/MinHeight, and
    /// <see cref="Large"/>. A default dimension the window sizes to its content (NaN) is 0 here,
    /// meaning "natural". Remembered per window, since <see cref="Show"/> clears the minimum it reads.
    /// </summary>
    public IReadOnlyList<ReviewSize> SizesFor(Window window, ReviewSize? defaultSize = null)
    {
        if (_sizes.TryGetValue(window, out var known))
        {
            return known;
        }

        var sizes = new List<ReviewSize>
        {
            defaultSize ?? new ReviewSize("default", double.IsNaN(window.Width) ? 0 : window.Width, double.IsNaN(window.Height) ? 0 : window.Height),
        };
        if (window.MinWidth <= 0 || window.MinHeight <= 0)
        {
            _failures.Add($"{window.GetType().Name} has no MinWidth/MinHeight - it can be shrunk until its content is gone.");
            sizes.Add(new ReviewSize("min", 480, 320));
        }
        else
        {
            sizes.Add(new ReviewSize("min", window.MinWidth, window.MinHeight));
        }

        sizes.Add(Large);
        _sizes[window] = sizes;
        return sizes;
    }

    /// <summary>
    /// Shows <paramref name="window"/> off-screen and, for each of <paramref name="sizes"/> (or
    /// <see cref="SizesFor"/>), lays it out at that size, runs <paramref name="prepare"/> (to reach a
    /// state that only exists once shown), checks it and saves <c>{name}-{theme}-{size}.png</c>.
    /// </summary>
    public void Review(Window window, string name, LayoutCheckOptions? options = null, IReadOnlyList<ReviewSize>? sizes = null, Action<Window>? prepare = null)
    {
        sizes ??= SizesFor(window);
        options = WithTheme(options);
        Show(window, sizes[0]);

        foreach (var size in sizes)
        {
            Resize(window, size);
            Settle(window);
            prepare?.Invoke(window);
            Settle(window);

            var label = $"{name} [{_theme}, {size.Name} {size.Width}x{size.Height}]";
            Save(window, Path.Combine(Directory, $"{name}-{_theme}-{size.Name}.png"));
            var problems = WpfLayoutAssert.Find(window, options);
            if (problems.Count > 0)
            {
                _failures.Add(WpfLayoutAssert.Format(problems, label));
            }
        }
    }

    /// <summary>
    /// Shows <paramref name="window"/> off-screen and frameless (so a capture is its client area) at
    /// <paramref name="size"/>, unless it's already shown. The window's own minimum is cleared: the
    /// review sizes enforce it instead, since a frameless window's client area is the whole window.
    /// Call <see cref="SizesFor"/> first - it reads that minimum.
    /// </summary>
    public static void Show(Window window, ReviewSize size)
    {
        if (window.IsVisible)
        {
            return;
        }

        window.MinWidth = 0;
        window.MinHeight = 0;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.Left = -10000;
        window.Top = -10000;
        Resize(window, size);
        window.Show();
        Settle(window);
    }

    /// <summary>Sizes the window's client area to what a real framed window of <paramref name="size"/> has; a 0 dimension keeps the window's own SizeToContent.</summary>
    private static void Resize(Window window, ReviewSize size)
    {
        if (size.Width > 0 && size.Height > 0)
        {
            window.SizeToContent = SizeToContent.Manual;
        }

        if (size.Width > 0)
        {
            window.Width = size.Width - Frame.Left - Frame.Right;
        }

        if (size.Height > 0)
        {
            window.Height = size.Height - Frame.Top - Frame.Bottom;
        }
    }

    /// <summary>Opens <paramref name="item"/>'s drop-down, checks it and saves <c>{name}-{theme}.png</c> of the popup's content.</summary>
    public void ReviewMenu(MenuItem item, string name, LayoutCheckOptions? options = null)
    {
        item.IsSubmenuOpen = true;
        Settle(item);
        var popup = item.Template.FindName("PART_Popup", item) as Popup;
        Assert.IsNotNull(popup, $"No PART_Popup in {name}'s template.");
        var content = (FrameworkElement)popup.Child;
        content.UpdateLayout();
        StaTestRunner.DoEvents();

        Save(content, Path.Combine(Directory, $"{name}-{_theme}.png"));
        var problems = WpfLayoutAssert.Find(content, WithTheme(options));
        if (problems.Count > 0)
        {
            _failures.Add(WpfLayoutAssert.Format(problems, $"{name} [{_theme}]"));
        }
    }

    /// <summary>Fails with every problem found by every <see cref="Review"/> so far.</summary>
    public void AssertClean()
    {
        if (_failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, _failures));
        }
    }

    public static void Settle(FrameworkElement element)
    {
        StaTestRunner.DoEvents();
        element.UpdateLayout();
        StaTestRunner.DoEvents();
        element.UpdateLayout();
    }

    private LayoutCheckOptions WithTheme(LayoutCheckOptions? options) => new()
    {
        IsDarkTheme = IsDark,
        Allowed = options?.Allowed ?? [],
        MinContrast = options?.MinContrast ?? new LayoutCheckOptions().MinContrast,
    };

    private static void Save(FrameworkElement element, string path)
    {
        var width = (int)Math.Max(1, Math.Ceiling(element.ActualWidth));
        var height = (int)Math.Max(1, Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not find the repo root (DevTerm.slnx) above '{AppContext.BaseDirectory}'.");
    }
}
