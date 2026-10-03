using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class RoutingViewModelTests
{
    private static SessionTab Tab() => SessionTab.Build(new CliOptions { Transport = "loopback" });

    private static RoutingViewModel Draft(SessionTab tab)
    {
        var vm = new RoutingViewModel(tab);
        vm.Draft.Host = "broker";
        vm.AddRule();
        vm.Draft.Rules[0].Match = @"^T=(?<t>\d+)$";
        vm.Draft.Rules[0].Topic = "temp/${t}";
        return vm;
    }

    [TestMethod]
    public void Test_ReportsMatchNoMatchAndInvalidPerRule()
    {
        var vm = Draft(Tab());
        vm.AddRule();
        vm.Draft.Rules[1].Match = "(";
        vm.Draft.Rules[1].Topic = "x";

        var lines = vm.Test("T=21");
        Assert.Contains("matches -> topic temp/21", lines[0]);
        Assert.Contains("invalid", lines[1]);
        Assert.Contains("no match", vm.Test("zzz")[0]);
    }

    [TestMethod]
    public async Task Apply_RejectsAnInvalidDraft_AndAcceptsAValidOne()
    {
        var tab = Tab();
        var vm = Draft(tab);
        vm.Draft.Rules[0].Match = "(";
        Assert.IsNotNull(await vm.ApplyAsync());
        Assert.IsFalse(tab.HasRouting);

        vm.Draft.Rules[0].Match = @"^T=(?<t>\d+)$";
        Assert.IsNull(await vm.ApplyAsync());
        Assert.IsTrue(tab.HasRouting);
    }

    [TestMethod]
    public void Start_WithoutAnOpenSession_SaysToConnectFirst() =>
        Assert.Contains("Connect", new RoutingViewModel(Tab()).Start()!);

    [TestMethod]
    public void MoveAndRemove_ReorderTheDraft()
    {
        var vm = Draft(Tab());
        vm.AddRule();
        vm.Draft.Rules[1].Topic = "second";
        vm.MoveRule(1, -1);
        Assert.AreEqual("second", vm.Draft.Rules[0].Topic);
        vm.RemoveRule(0);
        Assert.HasCount(1, vm.Draft.Rules);
    }
}
