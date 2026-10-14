using DevTerm.Core;
using DevTerm.Core.Plugins;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives the TUI's real plugin approval dialog and the per-approval picker (<c>TuiMode.PromptPendingPlugins</c>,
/// <c>ReviewPluginApprovals</c>) under a run loop: each dialog is captured to a PNG for the user guide, then a button
/// is invoked and the effect on <see cref="LivePlugins"/> and <see cref="PluginTrust"/> is asserted.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class PluginApprovalDialogTests
{
    private static string ImagesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "docs", "user-guide", "images");
    }

    private static Button? Find(View root, string text)
    {
        foreach (var sub in root.SubViews)
        {
            if (sub is Button button && button.Text.Replace("_", string.Empty).Replace("▶", string.Empty).Trim() == text)
            {
                return button;
            }

            if (Find(sub, text) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    [TestMethod]
    public void ApprovalDialog_ThenPicker_AreCapturedAndTheirChoicesTakeEffect()
    {
        var home = Path.Combine(Path.GetTempPath(), "devterm-tui-plugins-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(home, "plugins", "shout");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "shout.py"), "print('x')");
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "name": "py-shout", "version": "1.2.0", "contract": 1, "process": { "command": "python", "arguments": ["{folder}/shout.py"] } }""");
        var previous = Environment.GetEnvironmentVariable(DevTermHome.EnvironmentVariable);
        Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, home);
        PluginTrust.Approver = null;
        LivePlugins.Reset();
        PluginLoader.LoadAll(Path.Combine(home, "plugins"), new ServiceCollection());
        Assert.AreEqual(1, LivePlugins.Pending.Count, "The plugin should be waiting for approval.");

        var images = ImagesDirectory();
        Directory.CreateDirectory(images);
        string? failure = null;
        using var done = new ManualResetEventSlim(false);

        // Click the named button of whatever dialog is on top, after saving it as <image>. Runs from a timer inside the dialog's own loop.
        void Answer(IApplication app, View root, string image, string button)
        {
            app.AddTimeout(TimeSpan.FromMilliseconds(30), () =>
            {
                if (app.TopRunnableView is not { } dialog || dialog == root)
                {
                    return true;
                }

                try
                {
                    app.LayoutAndDraw(true);
                    TuiScreenshot.Save(Path.Combine(images, image));
                    var target = (dialog is Dialog d ? d.Buttons.FirstOrDefault(b => b.Text.Replace("_", string.Empty).Trim() == button) : null) ?? Find(dialog, button) ?? throw new InvalidOperationException($"No '{button}' button on the dialog.");
                    target.InvokeCommand(Command.Accept);
                }
                catch (Exception ex)
                {
                    failure = ex.ToString();
                    app.RequestStop();
                }

                return false;
            });
        }

        try
        {
            TuiTestRunner.RunWithLoopApp(
                app => app.Driver!.SetScreenSize(100, 24),
                app =>
                {
                    var root = new Window { Title = "dev-term" };
                    app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
                    {
                        try
                        {
                            Answer(app, root, "tui-plugin-approval.png", "Always for this version");
                            TuiMode.PromptPendingPlugins(app);
                            Answer(app, root, "tui-plugin-approvals.png", "Forget");
                            TuiMode.ReviewPluginApprovals(app);
                        }
                        catch (Exception ex)
                        {
                            failure ??= ex.ToString();
                        }
                        finally
                        {
                            done.Set();
                        }

                        return false;
                    });
                    return root;
                },
                (app, root) => Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(20)), "The dialogs never finished."));

            Assert.IsNull(failure, failure);
            Assert.IsTrue(File.Exists(Path.Combine(images, "tui-plugin-approval.png")));
            Assert.IsTrue(File.Exists(Path.Combine(images, "tui-plugin-approvals.png")));
            CollectionAssert.Contains(LivePlugins.ActiveNames.ToList(), "py-shout", "Always should have made the plugin live at once.");
            Assert.AreEqual(0, PluginTrust.Approvals().Count, "Forget in the picker should have removed the remembered approval.");
        }
        finally
        {
            PluginTrust.Approver = null;
            LivePlugins.Reset();
            Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, previous);
            Directory.Delete(home, true);
        }
    }
}
