using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Web.Tests;

[TestClass]
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Web)]
public class WebRolesTests
{
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<(WebHost.Built Built, string Base)> StartAsync(string? panel = null, string? scheme = null, string? certificatePath = null)
    {
        var port = FreePort();
        var built = WebHost.Build(
            new CliOptions { Transport = "loopback", Presenter = ["ascii"], Tui = false, Cli = true },
            new WebOptions { Urls = $"{scheme ?? "http"}://127.0.0.1:{port}", Token = "secret", ReadOnlyToken = "watch", Panel = panel, CertificatePath = certificatePath },
            []);
        await built.Hub.StartAsync();
        await built.App.StartAsync();
        return (built, $"{scheme ?? "http"}://127.0.0.1:{port}");
    }

    private static HttpClient Client(string token, HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task BlazorPanelPage_RendersTheUiDefinitionGenerically_AndDisablesControlsForAReadOnlyViewer()
    {
        var (built, url) = await StartAsync("busylight");
        await using (built.Hub)
        {
            using var operatorClient = Client("secret");
            using var watcher = Client("watch");
            var full = await operatorClient.GetStringAsync(url + "/panel");
            var readOnly = await watcher.GetStringAsync(url + "/panel");

            StringAssert.Contains(full, "<fieldset>");
            StringAssert.Contains(full, "data-control=");
            Assert.IsFalse(full.Contains("Read-only viewer", StringComparison.Ordinal));
            StringAssert.Contains(readOnly, "Read-only viewer");
            StringAssert.Contains(readOnly, "disabled");
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task BlazorPanelPage_WithoutAPanel_SaysSo()
    {
        var (built, url) = await StartAsync();
        await using (built.Hub)
        {
            using var operatorClient = Client("secret");
            StringAssert.Contains(await operatorClient.GetStringAsync(url + "/panel"), "No control panel is configured");
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task ReadOnlyToken_IsAccepted_AndReportedAsReadOnly()
    {
        var (built, url) = await StartAsync();
        await using (built.Hub)
        {
            using var watcher = Client("watch");
            using var operatorClient = Client("secret");
            StringAssert.Contains(await watcher.GetStringAsync(url + "/api/status"), "\"readOnly\":true");
            StringAssert.Contains(await operatorClient.GetStringAsync(url + "/api/status"), "\"readOnly\":false");
            using var stranger = Client("nope");
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await stranger.GetAsync(url + "/api/status")).StatusCode);
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task ReadOnlyViewer_CannotSendOverTheWebSocket()
    {
        var (built, url) = await StartAsync();
        await using (built.Hub)
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer watch");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(new Uri(url.Replace("http", "ws", StringComparison.Ordinal) + "/ws"), timeout.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, true, timeout.Token);

            var buffer = new byte[4096];
            var seen = new List<string>();
            while (!seen.Any(l => l.Contains("read-only", StringComparison.Ordinal)))
            {
                var result = await socket.ReceiveAsync(buffer, timeout.Token);
                seen.Add(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }

            Assert.IsFalse(seen.Any(l => l.Contains("From Loopback test", StringComparison.Ordinal)), "the line must not have reached the device");
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task Panel_IsServedAsAUiDefinition_AndAReadOnlyViewerCannotInvoke()
    {
        var (built, url) = await StartAsync(panel: "busylight");
        await using (built.Hub)
        {
            using var watcher = Client("watch");
            var panel = await watcher.GetStringAsync(url + "/api/panel");
            StringAssert.Contains(panel, "\"Name\"");
            StringAssert.Contains(panel, "\"kind\": \"button\"");

            var blocked = await watcher.PostAsJsonAsync(url + "/api/invoke", new InvokeRequest("anything", null));
            Assert.AreEqual(HttpStatusCode.Forbidden, blocked.StatusCode);

            using var operatorClient = Client("secret");
            var empty = await operatorClient.PostAsJsonAsync(url + "/api/invoke", new InvokeRequest(string.Empty, null));
            Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public async Task Panel_IsNotFound_WhenNoneIsConfigured()
    {
        var (built, url) = await StartAsync();
        await using (built.Hub)
        {
            using var client = Client("secret");
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync(url + "/api/panel")).StatusCode);
            await built.App.StopAsync();
        }
    }

    [TestMethod]
    public void Validate_RejectsAReadOnlyTokenEqualToTheMainToken_AndAnUnknownPanel()
    {
        Assert.IsNotNull(AccessPolicy.Validate(new WebOptions { Token = "x", ReadOnlyToken = "x" }));
        Assert.IsNotNull(AccessPolicy.Validate(new WebOptions { Panel = "oscilloscope" }));
        Assert.IsNull(AccessPolicy.Validate(new WebOptions { Token = "x", ReadOnlyToken = "y", Panel = "K8055" }));
    }

    [TestMethod]
    public async Task Https_WithACertificate_ServesOverTls_AndTheClientSeesThatCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = Path.Combine(Path.GetTempPath(), "devterm-web-" + Guid.NewGuid().ToString("N") + ".pfx");
        await File.WriteAllBytesAsync(pfx, certificate.Export(X509ContentType.Pfx));
        try
        {
            var (built, url) = await StartAsync(scheme: "https", certificatePath: pfx);
            await using (built.Hub)
            {
                string? seenThumbprint = null;
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                    {
                        seenThumbprint = cert?.Thumbprint;
                        return cert?.Thumbprint == certificate.Thumbprint;
                    },
                };
                using var client = Client("secret", handler);
                StringAssert.Contains(await client.GetStringAsync(url + "/api/status"), "Open");
                Assert.AreEqual(certificate.Thumbprint, seenThumbprint);
                await built.App.StopAsync();
            }
        }
        finally
        {
            File.Delete(pfx);
        }
    }

    [TestMethod]
    public async Task Https_WithACaIssuedCertificate_IsTrustedOnlyByAClientThatTrustsTheCa()
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=DevTerm Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
        leafRequest.CertificateExtensions.Add(san.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var issued = leafRequest.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(8));
        using var leaf = issued.CopyWithPrivateKey(leafKey);
        var pfx = Path.Combine(Path.GetTempPath(), "devterm-web-ca-" + Guid.NewGuid().ToString("N") + ".pfx");
        await File.WriteAllBytesAsync(pfx, leaf.Export(X509ContentType.Pfx));
        try
        {
            var (built, url) = await StartAsync(scheme: "https", certificatePath: pfx);
            await using (built.Hub)
            {
                // A client trusting only this CA validates the chain for real (no thumbprint shortcut).
                using var trusting = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                    {
                        using var chain = new X509Chain();
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(ca);
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        return cert is not null && chain.Build(cert);
                    },
                };
                using (var client = Client("secret", trusting))
                {
                    StringAssert.Contains(await client.GetStringAsync(url + "/api/status"), "Open");
                }

                // The default trust store has never heard of the test CA, so the same server is refused.
                using var untrusting = Client("secret", new HttpClientHandler());
                await Assert.ThrowsExactlyAsync<HttpRequestException>(() => untrusting.GetStringAsync(url + "/api/status"));
                await built.App.StopAsync();
            }
        }
        finally
        {
            File.Delete(pfx);
        }
    }
}
