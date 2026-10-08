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
    public required TestContext TestContext { get; set; }

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
    public async Task UnconfiguredStart_ComesUpWithoutAPortAndServesPing()
    {
        var port = FreePort();
        var built = WebHost.Build(new CliOptions(), new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "secret" }, []);
        await using (built.Hub)
        {
            await built.Hub.StartAsync();
            await built.App.StartAsync();
            using var client = new HttpClient();
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/", TestContext.CancellationToken);
            Assert.AreNotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            await built.App.StopAsync(TestContext.CancellationToken);
        }
    }

    [TestMethod]
    public async Task ControlHttp_SendsToTheSharedSession_AndNeedsItsOwnToken()
    {
        var webPort = FreePort();
        var controlPort = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, ControlHttp = controlPort, ControlToken = "ctl" },
            new WebOptions { Urls = $"http://127.0.0.1:{webPort}", Token = "secret" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            Assert.IsNotNull(built.ControlHttp);
            using var client = new HttpClient();
            var url = $"http://127.0.0.1:{controlPort}/";
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(url + "ping")).StatusCode);
            using var request = new HttpRequestMessage(HttpMethod.Post, url + "command") { Content = new StringContent("ping") };
            request.Headers.Authorization = new("Bearer", "ctl");
            using var response = await client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task OpenedConnection_WithControlHttp_GetsItsOwnControlServerClosedWithIt()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devterm-web-ctl-{Guid.NewGuid():N}.json");
        var controlPort = FreePort();
        var project = new ProjectFile { Name = "Bench" };
        project.Connections.Add(new ProjectConnection("Sim", $$"""{"Transport":"loopback","Presenter":["ascii"],"ControlHttp":{{controlPort}},"ControlToken":"ctl"}"""));
        project.Save(file);
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
            var baseUrl = $"http://127.0.0.1:{port}";
            using var opened = await client.PostAsync(baseUrl + "/api/connections?name=Sim", null, TestContext.CancellationToken);
            var json = System.Text.Json.JsonDocument.Parse(await opened.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
            Assert.AreEqual(controlPort, json.GetProperty("controlPort").GetInt32());

            using var control = new HttpClient();
            control.DefaultRequestHeaders.Authorization = new("Bearer", "ctl");
            using var ping = await control.GetAsync($"http://127.0.0.1:{controlPort}/ping", TestContext.CancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, ping.StatusCode);

            await client.DeleteAsync($"{baseUrl}/api/connections/{json.GetProperty("id").GetString()}", TestContext.CancellationToken);
            await Assert.ThrowsAsync<HttpRequestException>(() => control.GetAsync($"http://127.0.0.1:{controlPort}/ping", TestContext.CancellationToken));
            await built.App.StopAsync(TestContext.CancellationToken);
        }
    }

    [TestMethod]
    public async Task ApiEvents_StreamsConnectionOpenedAndClosed()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devterm-web-events-{Guid.NewGuid():N}.json");
        ProjectFile.From("Bench", [("Sim", new CliOptions { Transport = "loopback", Presenter = ["ascii"] })]).Save(file);
        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, Project = file },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "secret" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
            var baseUrl = $"http://127.0.0.1:{port}";
            using var stream = await client.GetStreamAsync(baseUrl + "/api/events");
            using var reader = new StreamReader(stream);
            var seen = new System.Text.StringBuilder();
            var pump = Task.Run(async () =>
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    lock (seen)
                    {
                        seen.AppendLine(line);
                    }
                }
            });

            var opened = await client.PostAsync(baseUrl + "/api/connections?name=Sim", null);
            var id = System.Text.Json.JsonDocument.Parse(await opened.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
            await client.DeleteAsync($"{baseUrl}/api/connections/{id}");

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (seen)
                {
                    if (seen.ToString().Contains("event: connection-closed", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                await Task.Delay(20);
            }

            string text;
            lock (seen)
            {
                text = seen.ToString();
            }

            StringAssert.Contains(text, "event: connection-opened");
            StringAssert.Contains(text, $"\"id\":\"{id}\"");
            StringAssert.Contains(text, "event: connection-closed");
            await built.App.StopAsync();
        }

        File.Delete(file);
    }

    [TestMethod]
    public async Task ApiDocs_OpenApiScalarAndAsyncApi_AreServedBehindTheToken()
    {
        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "secret" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            using var client = new HttpClient();
            var baseUrl = $"http://127.0.0.1:{port}";
            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync(baseUrl + "/openapi/v1.json")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");

            var openApi = await client.GetStringAsync(baseUrl + "/openapi/v1.json");
            StringAssert.Contains(openApi, "/api/devices");
            StringAssert.Contains(openApi, "/api/connections");
            StringAssert.Contains(await client.GetStringAsync(baseUrl + "/scalar/v1"), "dev-term web host");
            StringAssert.Contains(await client.GetStringAsync(baseUrl + "/asyncapi"), "/asyncapi.json");
            var asyncApi = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(baseUrl + "/asyncapi.json"));
            Assert.AreEqual("3.0.0", asyncApi.RootElement.GetProperty("asyncapi").GetString());
            Assert.IsTrue(asyncApi.RootElement.GetProperty("channels").TryGetProperty("events", out _));
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task ApiDevices_RequiresTheTokenAndReturnsTheThreeLists()
    {
        var (built, baseUrl) = await StartAsync();
        await using (built.Hub)
        {
            using var client = new HttpClient();
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(baseUrl + "/api/devices")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
            var json = await client.GetStringAsync(baseUrl + "/api/devices");
            using var document = System.Text.Json.JsonDocument.Parse(json);
            foreach (var key in new[] { "serial", "hid", "usbtmc" })
            {
                Assert.AreEqual(System.Text.Json.JsonValueKind.Array, document.RootElement.GetProperty(key).ValueKind, key);
            }

            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task ApiDiscover_RequiresTheTokenAndReturnsAJsonArray()
    {
        var (built, baseUrl) = await StartAsync();
        await using (built.Hub)
        {
            using var client = new HttpClient();
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(baseUrl + "/api/discover", TestContext.CancellationToken)).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
            using var document = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(baseUrl + "/api/discover?seconds=1", TestContext.CancellationToken));
            Assert.AreEqual(System.Text.Json.JsonValueKind.Array, document.RootElement.ValueKind);
            await built.App.StopAsync(TestContext.CancellationToken);
        }
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
    public async Task ApiProject_PutAndDeleteAConnection_EditsTheProjectFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devterm-web-project-{Guid.NewGuid():N}.json");
        ProjectFile.From("Bench", [("Echo", new CliOptions { Transport = "loopback", Presenter = ["ascii"], Parser = "ascii" })]).Save(file);
        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, Project = file },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "secret", ReadOnlyToken = "watch" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            var baseUrl = $"http://127.0.0.1:{port}";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
            using var watcher = new HttpClient();
            watcher.DefaultRequestHeaders.Authorization = new("Bearer", "watch");
            static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");

            Assert.AreEqual(HttpStatusCode.Forbidden, (await watcher.PutAsync(baseUrl + "/api/project/connections/Lab", Json("{\"Transport\":\"loopback\"}"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PutAsync(baseUrl + "/api/project/connections/Bad", Json("{\"Transport\":\"tcp\"}"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PutAsync(baseUrl + "/api/project/connections/Bad", Json("not json"))).StatusCode);

            Assert.AreEqual(HttpStatusCode.NoContent, (await client.PutAsync(baseUrl + "/api/project/connections/Lab", Json("{\"Transport\":\"tcp\",\"Host\":\"10.0.0.5\",\"Port\":23}"))).StatusCode);
            var listed = await client.GetStringAsync(baseUrl + "/api/project");
            StringAssert.Contains(listed, "Lab");
            StringAssert.Contains(listed, "Echo");
            Assert.AreEqual(2, ProjectFile.Load(file).Connections.Count);

            Assert.AreEqual(HttpStatusCode.NoContent, (await client.DeleteAsync(baseUrl + "/api/project/connections/Lab")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NoContent, (await client.DeleteAsync(baseUrl + "/api/project/connections/Lab")).StatusCode);
            Assert.AreEqual(1, ProjectFile.Load(file).Connections.Count);

            File.WriteAllText(file, "{ not a project");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PutAsync(baseUrl + "/api/project/connections/Lab", Json("{\"Transport\":\"loopback\"}"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.NoContent, (await client.DeleteAsync(baseUrl + "/api/project/connections/Echo")).StatusCode);
            await built.App.StopAsync();
        }

        File.Delete(file);
    }

    [TestMethod]
    public async Task ApiConnections_OpenFromTheProject_ThenTunnelAndClose()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devterm-web-project-{Guid.NewGuid():N}.json");
        ProjectFile.From("Bench", [("Echo", new CliOptions { Transport = "loopback", Presenter = ["ascii"], Parser = "ascii" })]).Save(file);
        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true, Project = file },
            new WebOptions { Urls = $"http://127.0.0.1:{port}", Token = "secret", ReadOnlyToken = "watch" },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        await using (built.Hub)
        {
            var baseUrl = $"http://127.0.0.1:{port}";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
            using var watcher = new HttpClient();
            watcher.DefaultRequestHeaders.Authorization = new("Bearer", "watch");

            Assert.AreEqual(HttpStatusCode.Forbidden, (await watcher.PostAsync(baseUrl + "/api/connections?name=Echo", null)).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsync(baseUrl + "/api/connections?name=Nope", null)).StatusCode);

            var opened = await client.PostAsync(baseUrl + "/api/connections?name=Echo", null);
            Assert.AreEqual(HttpStatusCode.OK, opened.StatusCode);
            var id = System.Text.Json.JsonDocument.Parse(await opened.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
            StringAssert.Contains(await client.GetStringAsync(baseUrl + "/api/connections"), id);

            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer secret");
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/{id}"), CancellationToken.None);
            await socket.SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, true, CancellationToken.None);
            var buffer = new byte[4096];
            var seen = new StringBuilder();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!seen.ToString().Contains("[ascii]", StringComparison.Ordinal))
            {
                var result = await socket.ReceiveAsync(buffer, timeout.Token);
                seen.Append(Encoding.UTF8.GetString(buffer, 0, result.Count)).Append(' ');
            }

            Assert.AreEqual(HttpStatusCode.NoContent, (await client.DeleteAsync($"{baseUrl}/api/connections/{id}")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NoContent, (await client.DeleteAsync($"{baseUrl}/api/connections/{id}")).StatusCode);
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
