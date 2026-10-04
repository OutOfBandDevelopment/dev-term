using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Web.Tests;

[TestClass]
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Web)]
public class WebHostTests
{
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<(WebHost.Built Built, string Base)> StartAsync()
    {
        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "secret" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        return (built, $"http://127.0.0.1:{port}");
    }

    [TestMethod]
    public async Task ApiProject_ListsTheProjectFilesConnections()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devterm-web-project-{Guid.NewGuid():N}.json");
        ProjectFile.From("Bench", [("Scope", new CliOptions { Transport = "tcp", Host = "10.0.0.5", Port = "23" })]).Save(file);
        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, Project = file },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "secret" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
            var json = await client.GetStringAsync($"http://127.0.0.1:{port}/api/project");
            StringAssert.Contains(json, "\"name\":\"Scope\"");
            StringAssert.Contains(json, "10.0.0.5");
            await built.App.StopAsync();
        }

        File.Delete(file);
    }

    [TestMethod]
    public async Task Request_WithoutToken_IsUnauthorized()
    {
        var (built, url) = await StartAsync();
        await using (built.Hub)
        {
            using var client = new HttpClient();
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(url + "/")).StatusCode);
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task Request_WithBearerToken_ServesThePageAndStatus()
    {
        var (built, url) = await StartAsync();
        await using (built.Hub)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
            Assert.IsTrue((await client.GetStringAsync(url + "/")).Contains("dev-term", StringComparison.Ordinal));
            Assert.IsTrue((await client.GetStringAsync(url + "/api/status")).Contains("Open", StringComparison.Ordinal));
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task WebSocket_SendsALine_AndReceivesTheDeviceReply()
    {
        var (built, url) = await StartAsync();
        await using (built.Hub)
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer secret");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(new Uri(url.Replace("http", "ws", StringComparison.Ordinal) + "/ws"), timeout.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, true, timeout.Token);

            var buffer = new byte[4096];
            var seen = new List<string>();
            while (!seen.Any(l => l.Contains("From Loopback test", StringComparison.Ordinal)))
            {
                var result = await socket.ReceiveAsync(buffer, timeout.Token);
                seen.Add(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }

            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public void Build_WithUnsafeOptions_Throws() =>
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            WebHost.Build(new CliOptions { Transport = "loopback" }, new WebOptions { Urls = "http://0.0.0.0:5080" }, []));
}
