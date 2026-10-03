using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>The TUI Routing window (<see cref="RoutingMode"/>) driven headlessly against a real <see cref="RoutingViewModel"/>.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class RoutingModeTests
{
    [TestMethod]
    public void SampleTest_ShowsTheTopicTheRuleWouldPublish()
    {
        var tab = SessionTab.Build(new CliOptions { Transport = "loopback" });
        var dump = "";
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var vm = new RoutingViewModel(tab);
            var parts = RoutingMode.BuildWindow(app, vm);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                Assert.AreEqual(" Broker: Stopped", parts.Status.Text);
                vm.AddRule();
                vm.Draft.Rules[0].Match = @"^T=(?<t>\d+)$";
                vm.Draft.Rules[0].Topic = "temp/${t}";
                parts.Sample.Text = "T=21";
                parts.RunTest();
                Assert.Contains("matches -> topic temp/21", parts.TestResult.Text);
                app.LayoutAndDraw(true);
                dump = TuiTestRunner.DumpBuffer();
            }
            finally
            {
                app.End(token);
                parts.Window.Dispose();
            }
        });

        Assert.Contains("Host:", dump);
        Assert.Contains("Unmatched: 0", dump);
    }
}
