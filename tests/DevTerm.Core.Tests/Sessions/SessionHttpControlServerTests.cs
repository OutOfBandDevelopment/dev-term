using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Core.Tests.Sessions;

/// <summary>A browser or script drives a session over the loopback HTTP control channel.</summary>
[TestCategory(TestCategories.Integration)]
[TestClass]
public sealed class SessionHttpControlServerTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Commands_NeedTheToken_AndReachTheTransport_AndEventsStream()
    {
        var pipe = new Pipe();
        var written = new List<byte>();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        transport.Setup(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((data, _) => { lock (written) { written.AddRange(data.ToArray()); } })
            .Returns(Task.CompletedTask);
        await using var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        await using var server = new SessionHttpControlServer(session, 0, text => text == "bad" ? (null, "nope") : (Encoding.ASCII.GetBytes(text + "\n"), null), "secret");
        using var registration = session.AddObserver(server);
        var baseUri = $"http://127.0.0.1:{server.Port}";
        using var http = new HttpClient();

        using var anonymous = await http.GetAsync(baseUri + "/ping", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");
        using var events = await http.GetAsync(baseUri + "/events", HttpCompletionOption.ResponseHeadersRead, TestContext.CancellationToken);
        using var eventReader = new StreamReader(await events.Content.ReadAsStreamAsync(TestContext.CancellationToken));

        Assert.AreEqual("ok", await Post(http, baseUri, "send *IDN?", HttpStatusCode.OK));
        Assert.AreEqual("ok", await Post(http, baseUri, "sendhex 41 42", HttpStatusCode.OK));
        StringAssert.StartsWith(await Post(http, baseUri, "send bad", HttpStatusCode.BadRequest), "error nope");
        StringAssert.StartsWith(await Post(http, baseUri, "frobnicate", HttpStatusCode.BadRequest), "error unknown command");

        string[] sent;
        lock (written)
        {
            sent = [Encoding.ASCII.GetString([.. written])];
        }

        Assert.AreEqual("*IDN?\nAB", sent[0]);

        var seen = new List<string>();
        while (!seen.Contains("data: tx 4142") && await eventReader.ReadLineAsync(TestContext.CancellationToken) is { } line)
        {
            seen.Add(line);
        }

        CollectionAssert.Contains(seen, "data: tx 4142");
    }

    private async Task<string> Post(HttpClient http, string baseUri, string command, HttpStatusCode expected)
    {
        using var response = await http.PostAsync(baseUri + "/command", new StringContent(command), TestContext.CancellationToken);
        Assert.AreEqual(expected, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.CancellationToken);
    }
}
