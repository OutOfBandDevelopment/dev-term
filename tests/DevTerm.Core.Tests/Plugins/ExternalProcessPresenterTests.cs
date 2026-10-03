using System.Buffers;
using System.Diagnostics;
using System.Text;
using DevTerm.Core.Plugins;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.Plugins;

/// <summary>
/// Out-of-process presenters written in Python, Java and Go (examples/{language}/out-of-process-plugin). Each language's case is Inconclusive when
/// that toolchain is not installed. Real child processes, so Integration.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestClass]
public sealed class ExternalProcessPresenterTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public required TestContext TestContext { get; set; }

    private static string ExamplePath(string language, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevTerm.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine([dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "examples", language, "out-of-process-plugin", file]);
    }

    private static bool Works(string fileName, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, argument) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (process is null || !process.WaitForExit(15000))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static ReadOnlySequence<byte> Bytes(string text) => new(Encoding.ASCII.GetBytes(text));

    [TestMethod]
    public async Task Python_Plugin_RendersThroughTheHost()
    {
        if (!Works("python", "--version"))
        {
            Assert.Inconclusive("python is not installed.");
        }

        await using var plugin = await ExternalProcessPresenter.StartAsync("python", [ExamplePath("python", "shout.py")], _timeout, TestContext.CancellationToken);

        Assert.AreEqual("py-shout", plugin.Name);
        CollectionAssert.AreEqual(new[] { "HELLO" }, plugin.Render(Bytes("hello")).ToArray());
    }

    [TestMethod]
    public async Task Java_Plugin_RendersThroughTheHost()
    {
        if (!Works("java", "-version"))
        {
            Assert.Inconclusive("java is not installed.");
        }

        await using var plugin = await ExternalProcessPresenter.StartAsync("java", [ExamplePath("java", "Shout.java")], _timeout, TestContext.CancellationToken);

        Assert.AreEqual("java-count", plugin.Name);
        CollectionAssert.AreEqual(new[] { "5 bytes" }, plugin.Render(Bytes("hello")).ToArray());
    }

    [TestMethod]
    public async Task Go_Plugin_RendersThroughTheHost()
    {
        if (!Works("go", "version"))
        {
            Assert.Inconclusive("go is not installed (or does not run here).");
        }

        await using var plugin = await ExternalProcessPresenter.StartAsync("go", ["run", ExamplePath("go", "main.go")], TimeSpan.FromMinutes(2), TestContext.CancellationToken);

        Assert.AreEqual("go-reverse", plugin.Name);
        CollectionAssert.AreEqual(new[] { "olleh" }, plugin.Render(Bytes("hello")).ToArray());
    }

    [TestMethod]
    public async Task SilentPlugin_FailsTheHandshake_InsteadOfHanging()
    {
        if (!Works("python", "--version"))
        {
            Assert.Inconclusive("python is not installed.");
        }

        var started = Stopwatch.StartNew();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await ExternalProcessPresenter.StartAsync("python", ["-c", "import time; time.sleep(60)"], TimeSpan.FromMilliseconds(500), TestContext.CancellationToken));

        Assert.IsLessThan(10000, started.ElapsedMilliseconds);
    }

    [TestMethod]
    public async Task PluginThatDiesAfterHello_IsFaulted_AndRendersNothing()
    {
        if (!Works("python", "--version"))
        {
            Assert.Inconclusive("python is not installed.");
        }

        const string Script = "import sys; sys.stdin.readline(); print('{\"type\":\"hello\",\"name\":\"dies\",\"protocol\":1}', flush=True); sys.stdin.readline()";
        await using var plugin = await ExternalProcessPresenter.StartAsync("python", ["-c", Script], _timeout, TestContext.CancellationToken);

        Assert.AreEqual(0, plugin.Render(Bytes("x")).Count);
        Assert.IsTrue(plugin.Faulted);
        StringAssert.Contains(plugin.FaultReason, "exited");
        Assert.AreEqual(0, plugin.Render(Bytes("y")).Count);
    }
}
