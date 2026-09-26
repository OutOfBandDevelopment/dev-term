using DevTerm.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace DevTerm.Console.Tests;

/// <summary>
/// The harness behind the TUI layout-review tests: builds one real screen headlessly at a given
/// terminal size and theme, runs <see cref="TuiLayoutAssert"/> over it, and saves a PNG of it
/// (<see cref="TuiScreenshot"/>) into the untracked <c>artifacts/ui-review/tui/</c> folder at the repo
/// root for a person to look through - never into docs/user-guide/images, which only holds doc images.
/// </summary>
internal static class TuiReview
{
    /// <summary>The terminal sizes every screen is checked at: the minimum supported, a common one, and a large one.</summary>
    public static readonly (int Width, int Height)[] Sizes = [(80, 25), (120, 30), (200, 60)];

    public static readonly string Directory = Path.Combine(FindRepoRoot(), "artifacts", "ui-review", "tui");

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

    /// <summary>Selects <paramref name="theme"/> ("light" or "dark") for the next window built, the way the app does at startup. Undo with <see cref="ResetTheme"/>.</summary>
    public static void UseTheme(string theme)
    {
        ActiveTheme.Select(theme, persist: false);
        TuiTheme.Apply(ActiveTheme.Current);
    }

    /// <summary>Back to the default theme and Terminal.Gui's own schemes - process-wide state (see CLAUDE.md), so every test class using <see cref="UseTheme"/> calls this in its cleanup.</summary>
    public static void ResetTheme()
    {
        ActiveTheme.Reset();
        TuiTheme.Restore();
    }

    /// <summary>
    /// Builds a screen with <paramref name="build"/> in a fresh headless app sized
    /// <paramref name="width"/>x<paramref name="height"/>, lets <paramref name="arrange"/> put it in the
    /// state to review, then checks and captures it as <c>{name}-{width}x{height}-{theme}.png</c>.
    /// </summary>
    public static void Screen(string name, int width, int height, string theme, Func<IApplication, View> build, Action<IApplication, View>? arrange = null, Func<IApplication, View, TuiLayoutOptions>? options = null)
    {
        UseTheme(theme);
        TuiTestRunner.RunHeadlessApp(app =>
        {
            app.Driver!.SetScreenSize(width, height);
            var root = build(app);
            var token = app.Begin((IRunnable)root) ?? throw new NotSupportedException();
            try
            {
                app.LayoutAndDraw(true);
                arrange?.Invoke(app, root);
                app.LayoutAndDraw(true);
                Capture(app, root, name, theme, options?.Invoke(app, root));
            }
            finally
            {
                app.End(token);
                root.Dispose();
            }
        });
    }

    /// <summary>Checks and captures whatever is on screen now - for a test that drives its own app (a looped run, say).</summary>
    public static void Capture(IApplication app, View root, string name, string theme, TuiLayoutOptions? options = null) =>
        Fail(Check(app, root, name, theme, options));

    /// <summary>Saves the PNG and the problem list (if any) for what's on screen now; returns the failure message, or null when the layout is clean.</summary>
    public static string? Check(IApplication app, View root, string name, string theme, TuiLayoutOptions? options = null)
    {
        var baseName = $"{name}-{app.Screen.Width}x{app.Screen.Height}-{theme}";
        System.IO.Directory.CreateDirectory(Directory);
        TuiScreenshot.Save(Path.Combine(Directory, baseName + ".png"));
        var problems = TuiLayoutAssert.FindProblems(app, root, options);
        var problemsFile = Path.Combine(Directory, baseName + ".problems.txt");
        if (problems.Count == 0)
        {
            File.Delete(problemsFile);
            return null;
        }

        File.WriteAllLines(problemsFile, problems);
        return $"{problems.Count} layout problem(s) in {baseName}:\n  " + string.Join("\n  ", problems);
    }

    private static void Fail(string? message)
    {
        if (message is not null)
        {
            Assert.Fail(message);
        }
    }

    /// <summary>
    /// Checks and captures a real modal dialog: <paramref name="open"/> runs it (a nested
    /// <c>Application.Run</c>, exactly as the app does) over the window <paramref name="build"/> makes,
    /// under a real run loop; once it's on top it's captured, then closed.
    /// </summary>
    public static void Modal(string name, int width, int height, string theme, Func<IApplication, View> build, Action<IApplication> open, Func<IApplication, View, TuiLayoutOptions>? options = null)
    {
        UseTheme(theme);
        string? failure = null;
        using var captured = new ManualResetEventSlim(false);

        // Everything happens on the loop thread, from timers: a nested Run started inside an
        // Application.Invoke callback never drained later Invokes (a test waiting on one hung), but
        // the nested loop does keep firing timers - so one timer opens the dialog and another,
        // firing inside the dialog's own loop, captures and closes it.
        TuiTestRunner.RunWithLoopApp(
            app => app.Driver!.SetScreenSize(width, height),
            app =>
            {
                var root = build(app);
                app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                {
                    app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                    {
                        if (app.TopRunnableView is not { } dialog || dialog == root)
                        {
                            return true;
                        }

                        try
                        {
                            app.LayoutAndDraw(true);
                            failure = Check(app, dialog, name, theme, options?.Invoke(app, dialog));
                        }
                        catch (Exception ex)
                        {
                            failure = $"{name}: checking the dialog threw {ex}";
                        }
                        finally
                        {
                            app.RequestStop();
                            captured.Set();
                        }

                        return false;
                    });
                    open(app);
                    return false;
                });
                return root;
            },
            (app, root) => Assert.IsTrue(captured.Wait(TimeSpan.FromSeconds(10)), $"{name}: the dialog never opened."));
        Fail(failure);
    }
}
