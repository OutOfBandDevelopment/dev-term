using DevTerm.Configuration;
using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>The WPF Routing window (<see cref="RoutingWindow"/>); constructed, never shown.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class RoutingWindowTests
{
    private static SessionTab Tab() => SessionTab.Build(new CliOptions { Transport = "loopback" });

    [TestMethod]
    public void AddingARuleAndTestingASample_ShowsTheTopicAndPayload()
    {
        StaTestRunner.Run(() =>
        {
            var window = new RoutingWindow(new RoutingViewModel(Tab()));
            StaTestRunner.DoEvents();
            Assert.AreEqual("Broker: Stopped", window.StatusText);

            window.AddRule();
            window.SetRule(@"^T=(?<t>\d+)$", "temp/${t}");
            window.SetSample("T=21");
            window.RunTest();

            Assert.AreEqual(1, window.RuleCount);
            Assert.Contains("matches -> topic temp/21", window.TestResultText);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Apply_WithAnInvalidRule_SaysWhy_AndAWorkingDraftTurnsRoutingOn()
    {
        StaTestRunner.Run(async () =>
        {
            var tab = Tab();
            var window = new RoutingWindow(new RoutingViewModel(tab));
            StaTestRunner.DoEvents();
            window.SetHost("broker");
            window.AddRule();
            window.SetRule("(", "x");
            await window.ApplyForTestAsync();
            StaTestRunner.DoEvents();
            Assert.Contains("Rule 1", window.MessageText);
            Assert.IsFalse(tab.HasRouting);

            window.SetRule(@"^T=(\d+)$", "temp");
            await window.ApplyForTestAsync();
            StaTestRunner.DoEvents();
            Assert.IsTrue(tab.HasRouting);
        });
    }
}
