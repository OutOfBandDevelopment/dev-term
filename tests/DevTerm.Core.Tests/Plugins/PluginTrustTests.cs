using System.Buffers;
using System.Diagnostics;
using System.Text;
using DevTerm.Core.Plugins;
using DevTerm.Core.Presenters;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Core.Tests.Plugins;

/// <summary>Out-of-process plugins found by <see cref="PluginLoader"/> run only after approval (<see cref="PluginTrust"/>).</summary>
[TestCategory(TestCategories.Unit)]
[DoNotParallelize]
[TestClass]
public sealed class PluginTrustTests
{
    private string _home = "";
    private string _plugins = "";
    private string? _previousHome;

    [TestInitialize]
    public void Init()
    {
        _previousHome = Environment.GetEnvironmentVariable(DevTermHome.EnvironmentVariable);
        _home = Path.Combine(Path.GetTempPath(), "devterm-trust-" + Guid.NewGuid().ToString("N"));
        _plugins = Path.Combine(_home, "plugins");
        Directory.CreateDirectory(_plugins);
        Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, _home);
        PluginTrust.Approver = null;
        LivePlugins.Reset();
    }

    [TestCleanup]
    public void Cleanup()
    {
        PluginTrust.Approver = null;
        LivePlugins.Reset();
        Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, _previousHome);
        Directory.Delete(_home, true);
    }

    private string WriteProcessPlugin(string script = "print('x')", string command = "python")
    {
        var folder = Path.Combine(_plugins, "shout");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "shout.py"), script);
        File.WriteAllText(
            Path.Combine(folder, PluginManifest.FileName),
            $$"""{ "name": "py-shout", "version": "1.2.0", "contract": 1, "process": { "command": "{{command}}", "arguments": ["{folder}/shout.py"] } }""");
        return folder;
    }

    private PluginLoadResult Load(out ServiceCollection services)
    {
        services = [];
        return PluginLoader.LoadAll(_plugins, services).Single();
    }

    private sealed class Approver(PluginApprovalChoice choice) : IPluginApprover
    {
        public List<PluginApprovalRequest> Asked { get; } = [];

        public PluginApprovalChoice Ask(PluginApprovalRequest request)
        {
            Asked.Add(request);
            return choice;
        }
    }

    [TestMethod]
    public void WithNoApprover_AProcessPluginDoesNotLoad_AndSaysWhy()
    {
        WriteProcessPlugin();

        var result = Load(out var services);

        Assert.IsFalse(result.Loaded);
        Assert.Contains("needs your approval", result.Message);
        Assert.Contains("shout.py", result.Message);
        Assert.IsEmpty(services);
    }

    [TestMethod]
    public void AnUnapprovedPlugin_Waits_AndApprovingItMakesItsPresenterResolvableAtOnce()
    {
        WriteProcessPlugin();
        Load(out _);
        var catalog = new PresenterCatalog([]);
        Assert.IsFalse(catalog.TryGet("py-shout", out _));
        Assert.AreEqual("py-shout", LivePlugins.Pending.Single().Request.Name);

        Assert.IsTrue(LivePlugins.Approve("py-shout", PluginApprovalChoice.Once));

        Assert.IsEmpty(LivePlugins.Pending);
        Assert.IsTrue(catalog.TryGet("py-shout", out var presenter));
        Assert.AreEqual("py-shout", presenter.Name);
        CollectionAssert.Contains(catalog.Names.ToList(), "py-shout");
        Assert.IsFalse(PluginTrust.IsApproved("py-shout", PluginHash.Compute(Path.Combine(_plugins, "shout"))), "Once is not remembered.");
    }

    [TestMethod]
    public void ApprovingAlways_RemembersIt_AndDenyChangesNothing()
    {
        WriteProcessPlugin();
        Load(out _);

        Assert.IsFalse(LivePlugins.Approve("py-shout", PluginApprovalChoice.Deny));
        Assert.HasCount(1, LivePlugins.Pending);
        Assert.IsTrue(LivePlugins.Approve("py-shout", PluginApprovalChoice.Always));

        Assert.IsTrue(PluginTrust.IsApproved("py-shout", PluginHash.Compute(Path.Combine(_plugins, "shout"))));
        Assert.IsFalse(LivePlugins.Approve("nope", PluginApprovalChoice.Once));
    }

    [TestMethod]
    public void ReviewPending_AsksAboutEachWaitingPlugin_AndApprovesOnlyTheOnesAnsweredYes()
    {
        WriteProcessPlugin();
        Load(out _);
        var asked = new List<string>();

        var live = LivePlugins.ReviewPending(p =>
        {
            asked.Add(p.Request.Name);
            return PluginApprovalChoice.Once;
        });

        Assert.AreEqual(1, live);
        CollectionAssert.AreEqual(new[] { "py-shout" }, asked);
        Assert.AreEqual(0, LivePlugins.ReviewPending(_ => PluginApprovalChoice.Once), "Nothing is left to ask about.");
    }

    [TestMethod]
    public void Review_ForgetsTheApprovalsSaidForget_KeepsTheRest_AndStopsOnStop()
    {
        PluginTrust.Remember("a", "1");
        PluginTrust.Remember("b", "2");
        PluginTrust.Remember("c", "3");

        var forgotten = PluginTrust.Review(approval => approval.Name switch
        {
            "a" => ApprovalReviewChoice.Forget,
            "b" => ApprovalReviewChoice.Keep,
            _ => ApprovalReviewChoice.Stop,
        });

        Assert.AreEqual(1, forgotten);
        CollectionAssert.AreEqual(new[] { "b", "c" }, PluginTrust.Approvals().Select(a => a.Name).ToArray());
    }

    [TestMethod]
    public void Deny_DoesNotLoad_AndIsNotRemembered()
    {
        WriteProcessPlugin();
        var approver = new Approver(PluginApprovalChoice.Deny);
        PluginTrust.Approver = approver;

        Assert.IsFalse(Load(out _).Loaded);
        Assert.IsFalse(Load(out _).Loaded);

        Assert.HasCount(2, approver.Asked);
    }

    [TestMethod]
    public void Once_Loads_ButAsksAgainNextTime()
    {
        WriteProcessPlugin();
        var approver = new Approver(PluginApprovalChoice.Once);
        PluginTrust.Approver = approver;

        Assert.IsTrue(Load(out var services).Loaded);
        Assert.IsTrue(Load(out _).Loaded);

        Assert.HasCount(2, approver.Asked);
        Assert.AreEqual("py-shout", services.BuildServiceProvider().GetServices<IPresenter>().Single().Name);
        Assert.AreEqual("1.2.0", approver.Asked[0].Version);
        Assert.Contains("shout.py", approver.Asked[0].CommandLine);
    }

    [TestMethod]
    public void Always_IsRemembered_SoAnUnchangedPluginIsNotAskedAgain_ButAnEditedOneIs()
    {
        var folder = WriteProcessPlugin();
        var approver = new Approver(PluginApprovalChoice.Always);
        PluginTrust.Approver = approver;

        Assert.IsTrue(Load(out _).Loaded);
        Assert.HasCount(1, approver.Asked);
        Assert.IsTrue(File.Exists(PluginTrust.StorePath));

        // Unchanged, and even with nobody to ask: runs on the remembered approval.
        PluginTrust.Approver = null;
        Assert.IsTrue(Load(out _).Loaded);

        // A changed script is different content, so the old approval no longer applies.
        File.WriteAllText(Path.Combine(folder, "shout.py"), "print('changed')");
        Assert.IsFalse(Load(out _).Loaded);

        PluginTrust.Approver = approver;
        Assert.IsTrue(Load(out _).Loaded);
        Assert.HasCount(2, approver.Asked);
        Assert.AreNotEqual(approver.Asked[0].Hash, approver.Asked[1].Hash);
    }

    [TestMethod]
    public void Forget_RemovesARememberedApproval()
    {
        WriteProcessPlugin();
        PluginTrust.Approver = new Approver(PluginApprovalChoice.Always);
        Assert.IsTrue(Load(out _).Loaded);

        PluginTrust.Forget("py-shout");
        PluginTrust.Approver = null;

        Assert.IsFalse(Load(out _).Loaded);
    }

    [TestMethod]
    public void ACorruptApprovalsFile_IsTreatedAsNothingApproved()
    {
        WriteProcessPlugin();
        File.WriteAllText(PluginTrust.StorePath, "{ not json");

        Assert.IsFalse(Load(out _).Loaded);
    }

    [TestMethod]
    public void AManifestWithNeitherAnAssemblyNorACommand_IsRejected()
    {
        var folder = Path.Combine(_plugins, "empty");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, PluginManifest.FileName), """{ "name": "x", "contract": 1 }""");

        var result = Load(out _);

        Assert.IsFalse(result.Loaded);
        Assert.Contains("process command", result.Message);
    }

    [TestMethod]
    public void TheLazyPresenter_DoesNotStartAProgramUntilItRenders_AndAProgramThatWontStartRendersNothing()
    {
        var presenter = new LazyExternalPresenter("gone", Path.Combine(_home, "no-such-program.exe"), [], TimeSpan.FromSeconds(1));

        Assert.IsFalse(presenter.Faulted);
        Assert.IsEmpty(presenter.Render(new ReadOnlySequence<byte>("abc"u8.ToArray())));
        Assert.IsTrue(presenter.Faulted);
        Assert.Contains("no-such-program", presenter.FaultReason!);
    }
}

/// <summary>An approved plugin found in a plugins folder runs end to end (needs python; Inconclusive without it).</summary>
[TestCategory(TestCategories.Integration)]
[DoNotParallelize]
[TestClass]
public sealed class PluginTrustProcessTests
{
    [TestMethod]
    public async Task AnApprovedPythonPlugin_FoundByTheLoader_RendersThroughTheCatalog()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("python", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (probe is null || !probe.WaitForExit(15000) || probe.ExitCode != 0)
            {
                Assert.Inconclusive("python is not installed.");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Inconclusive("python is not installed.");
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DevTerm.slnx")))
        {
            root = root.Parent;
        }

        var home = Path.Combine(Path.GetTempPath(), "devterm-trust-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(DevTermHome.EnvironmentVariable);
        var folder = Path.Combine(home, "plugins", "shout");
        Directory.CreateDirectory(folder);
        try
        {
            Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, home);
            File.Copy(Path.Combine(root!.FullName, "examples", "python", "out-of-process-plugin", "shout.py"), Path.Combine(folder, "shout.py"));
            File.WriteAllText(
                Path.Combine(folder, PluginManifest.FileName),
                """{ "name": "py-shout", "version": "1.0.0", "contract": 1, "process": { "command": "python", "arguments": ["{folder}/shout.py"], "replyTimeoutMs": 10000 } }""");
            PluginTrust.Approver = new AlwaysApprover();

            var services = new ServiceCollection();
            Assert.IsTrue(PluginLoader.LoadAll(Path.Combine(home, "plugins"), services).Single().Loaded);
            await using var provider = services.BuildServiceProvider();
            var presenter = new PresenterCatalog(provider.GetServices<IPresenter>()).Get("py-shout");

            CollectionAssert.AreEqual(new[] { "HELLO" }, presenter.Render(new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes("hello"))).ToArray());
        }
        finally
        {
            PluginTrust.Approver = null;
            Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, previous);
            try
            {
                Directory.Delete(home, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [TestMethod]
    public void Approvals_ListsWhatWasRemembered_AndForgetRemovesIt()
    {
        var home = Path.Combine(Path.GetTempPath(), "devterm-trust-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(DevTermHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, home);
            Assert.AreEqual(0, PluginTrust.Approvals().Count);

            PluginTrust.Remember("shout", "AB12");
            PluginTrust.Remember("whisper", "CD34");

            CollectionAssert.AreEqual(new[] { "shout", "whisper" }, PluginTrust.Approvals().Select(a => a.Name).ToArray());
            PluginTrust.Forget("shout");
            CollectionAssert.AreEqual(new[] { "whisper" }, PluginTrust.Approvals().Select(a => a.Name).ToArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, previous);
            try
            {
                Directory.Delete(home, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class AlwaysApprover : IPluginApprover
    {
        public PluginApprovalChoice Ask(PluginApprovalRequest request) => PluginApprovalChoice.Once;
    }
}
