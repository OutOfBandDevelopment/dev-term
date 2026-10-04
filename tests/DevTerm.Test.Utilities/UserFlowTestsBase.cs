namespace DevTerm.Test.Utilities;

/// <summary>
/// The user flows from docs/user-guide, written once and run against every front end: each front end's test project derives
/// a <c>[TestClass]</c> from this and supplies <see cref="RunAsync"/>, which starts that front end against a loopback device
/// and hands the flow an <see cref="IFrontEndDriver"/>. A flow a front end has no way to perform is reported Inconclusive,
/// never silently passed.
/// </summary>
public abstract class UserFlowTestsBase
{
    private static readonly TimeSpan _replyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _quietPeriod = TimeSpan.FromMilliseconds(750);

    public TestContext TestContext { get; set; } = null!;

    /// <summary>Starts the front end under test, connected to a loopback device, and runs <paramref name="flow"/> against it.</summary>
    protected abstract Task RunAsync(Func<IFrontEndDriver, Task> flow);

    private static async Task ExpectAsync(IFrontEndDriver driver, string text)
    {
        if (!await driver.WaitForOutputAsync(text, _replyTimeout))
        {
            Assert.Fail($"{driver.Name}: expected the output to contain \"{text}\" but it was:{Environment.NewLine}{await driver.OutputAsync()}");
        }
    }

    private static int CountOf(string text, string value) => text.Split(value).Length - 1;

    [TestMethod]
    public Task Connected_OnStartup() => RunAsync(async driver =>
    {
        Assert.IsTrue(await driver.IsConnectedAsync(), $"{driver.Name}: expected to start connected.");
    });

    [TestMethod]
    public Task Send_ShowsTheDeviceReply() => RunAsync(async driver =>
    {
        await driver.SendAsync("hello");
        await ExpectAsync(driver, "From Loopback test");
    });

    [TestMethod]
    public Task Send_AnUnknownCommand_ShowsTheDevicesComplaintAndStaysConnected() => RunAsync(async driver =>
    {
        await driver.SendAsync("nonsense");
        await ExpectAsync(driver, "Unrecognized: nonsense");
        Assert.IsTrue(await driver.IsConnectedAsync(), $"{driver.Name}: an unknown command must not drop the connection.");
    });

    [TestMethod]
    public Task Send_ManyLinesInARow_NoReplyIsDropped() => RunAsync(async driver =>
    {
        const int count = 15;
        for (var i = 0; i < count; i++)
        {
            await driver.SendAsync("hello");
            await Task.Delay(40, TestContext.CancellationToken);
        }

        var output = string.Empty;
        for (var attempt = 0; attempt < 150 && CountOf(output, "From Loopback test") < count; attempt++)
        {
            await Task.Delay(100, TestContext.CancellationToken);
            output = await driver.OutputAsync();
        }

        Assert.AreEqual(count, CountOf(output, "From Loopback test"), $"{driver.Name}: expected {count} replies.");
    });

    [TestMethod]
    public Task Logging_ARecordedSession_ContainsTheDeviceReply() => RunAsync(async driver =>
    {
        if (!driver.CanLog)
        {
            Assert.Inconclusive($"{driver.Name} cannot start a session log while running.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"devterm-flow-{Guid.NewGuid():N}.jsonl");
        try
        {
            await driver.StartLoggingAsync(path);
            await driver.SendAsync("hello");
            await ExpectAsync(driver, "From Loopback test");
            await driver.StopLoggingAsync();
            StringAssert.Contains(File.ReadAllText(path), Convert.ToBase64String("From Loopback test"u8.ToArray())[..20], $"{driver.Name}: the log should hold the reply (rx data is base64).");
        }
        finally
        {
            File.Delete(path);
        }
    });

    [TestMethod]
    public Task SwitchProfile_TheNewSessionIsConnectedAndAnswers() => RunAsync(async driver =>
    {
        if (!driver.CanSwitchProfile)
        {
            Assert.Inconclusive($"{driver.Name} cannot switch profile while running.");
        }

        await driver.SendAsync("hello");
        await ExpectAsync(driver, "From Loopback test");
        await driver.SwitchToLoopbackProfileAsync();
        Assert.IsTrue(await driver.IsConnectedAsync(), $"{driver.Name}: should be connected after the switch.");
        await driver.SendAsync("afterswitch");
        await ExpectAsync(driver, "Unrecognized: afterswitch");
    });

    [TestMethod]
    public Task Panel_SettingTheControlsAndPressingApply_SendsTheCommandToTheDevice() => RunAsync(async driver =>
    {
        if (!driver.CanUsePanel)
        {
            Assert.Inconclusive($"{driver.Name} has no control panel.");
        }

        await driver.ApplyDemoPanelAsync();
        await ExpectAsync(driver, "Unrecognized: SET LED=1 LEVEL=7");
    });

    [TestMethod]
    public Task Send_SeveralLines_EachGetsItsOwnReply() => RunAsync(async driver =>
    {
        await driver.SendAsync("hello");
        await ExpectAsync(driver, "From Loopback test");
        await driver.SendAsync("again");
        await ExpectAsync(driver, "Unrecognized: again");
        await driver.SendAsync("hello");
        var output = string.Empty;
        for (var attempt = 0; attempt < 100 && CountOf(output, "From Loopback test") < 2; attempt++)
        {
            await Task.Delay(100, TestContext.CancellationToken);
            output = await driver.OutputAsync();
        }

        Assert.AreEqual(2, CountOf(output, "From Loopback test"), $"{driver.Name}: expected two replies to hello.{Environment.NewLine}{output}");
        StringAssert.Contains(output, "Unrecognized: again");
    });

    [TestMethod]
    public Task Send_AnEmptyLine_DoesNotBreakTheSession() => RunAsync(async driver =>
    {
        await driver.SendAsync(string.Empty);
        await driver.SendAsync("hello");
        await ExpectAsync(driver, "From Loopback test");
        Assert.IsTrue(await driver.IsConnectedAsync());
    });

    [TestMethod]
    public Task Disconnect_ThenReconnect_TheSessionWorksAgain() => RunAsync(async driver =>
    {
        if (!driver.CanDisconnect)
        {
            Assert.Inconclusive($"{driver.Name} has no Connect/Disconnect action.");
        }

        await driver.DisconnectAsync();
        Assert.IsFalse(await driver.IsConnectedAsync(), $"{driver.Name}: expected disconnected after Disconnect.");
        await driver.ReconnectAsync();
        Assert.IsTrue(await driver.IsConnectedAsync(), $"{driver.Name}: expected connected after Connect.");
        await driver.SendAsync("hello");
        await ExpectAsync(driver, "From Loopback test");
    });

    [TestMethod]
    public Task Send_WhileDisconnected_GetsNoReply() => RunAsync(async driver =>
    {
        if (!driver.CanDisconnect)
        {
            Assert.Inconclusive($"{driver.Name} has no Connect/Disconnect action.");
        }

        await driver.DisconnectAsync();
        await driver.SendAsync("hello");
        await Task.Delay(_quietPeriod, TestContext.CancellationToken);
        var output = await driver.OutputAsync();
        Assert.IsFalse(output.Contains("From Loopback test", StringComparison.Ordinal), $"{driver.Name}: a line sent while disconnected must not be answered.{Environment.NewLine}{output}");
    });
}
