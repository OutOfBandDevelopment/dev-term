using System.Net;
using System.Text;
using DevTerm.Configuration.Discovery;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Tcp;

namespace DevTerm.Configuration.Tests;

[TestClass]
[TestCategory(TestCategories.Unit)]
public sealed class NetworkDiscoveryTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public void BuildPtrQuery_EncodesLabelsAndTheUnicastBit()
    {
        var query = DnsMessage.BuildPtrQuery(["_mqtt._tcp.local"]);

        Assert.AreEqual(1, query[5]);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("\u0005_mqtt"), query[12..18]);
        CollectionAssert.AreEqual(new byte[] { 0, 12, 0x80, 1 }, query[^4..]);
    }

    [TestMethod]
    public async Task MdnsProbe_TurnsPtrSrvAndAIntoAHit()
    {
        var probe = new MdnsProbe(new CannedExchange(new UdpReply(IPAddress.Parse("10.0.0.9"), MdnsAnswer())));

        var hits = await probe.ProbeAsync(TimeSpan.FromMilliseconds(10), TestContext.CancellationToken);

        Assert.HasCount(1, hits);
        Assert.AreEqual("10.0.0.5", hits[0].Address);
        Assert.AreEqual(1883, hits[0].Port);
        Assert.AreEqual("mqtt", hits[0].Kind);
        Assert.AreEqual("broker", hits[0].DisplayName);
        Assert.AreEqual("mdns", hits[0].Source);
    }

    [TestMethod]
    public void ParseRecords_NeverThrowsOnGarbageOrTruncatedInput()
    {
        var answer = MdnsAnswer();
        for (var length = 0; length < answer.Length; length++)
        {
            _ = DnsMessage.ParseRecords(answer.AsSpan(0, length));
        }

        Assert.IsEmpty(DnsMessage.ParseRecords([0xFF, 0xFF, 0xFF]));
    }

    [TestMethod]
    public async Task SsdpProbe_TakesThePortFromLocationAndIgnoresNon200()
    {
        var ok = "HTTP/1.1 200 OK\r\nLOCATION: http://10.0.0.7:49152/desc.xml\r\nSERVER: Linux UPnP/1.0 Widget/2\r\n\r\n";
        var search = "M-SEARCH * HTTP/1.1\r\n\r\n";
        var probe = new SsdpProbe(new CannedExchange(
            new UdpReply(IPAddress.Parse("10.0.0.7"), Encoding.ASCII.GetBytes(ok)),
            new UdpReply(IPAddress.Parse("10.0.0.8"), Encoding.ASCII.GetBytes(search))));

        var hits = await probe.ProbeAsync(TimeSpan.FromMilliseconds(10), TestContext.CancellationToken);

        Assert.HasCount(1, hits);
        Assert.AreEqual(49152, hits[0].Port);
        Assert.AreEqual("upnp", hits[0].Kind);
        Assert.AreEqual("Linux UPnP/1.0 Widget/2", hits[0].DisplayName);
    }

    [TestMethod]
    public async Task LxiProbe_SuggestsTcpWhenARawPortAnsweredAndVxi11Otherwise()
    {
        var probe = new LxiProbe((_, _, _) => Task.FromResult<IReadOnlyList<LxiDevice>>(
            [new LxiDevice("10.0.0.2", 111, 5555, "RIGOL,DG1062Z,X,1"), new LxiDevice("10.0.0.3", 9010, 0, string.Empty)]));

        var hits = await probe.ProbeAsync(TimeSpan.FromMilliseconds(10), TestContext.CancellationToken);

        Assert.AreEqual(("tcp", 5555), (hits[0].Transport, hits[0].Port));
        Assert.AreEqual(("vxi11", 9010), (hits[1].Transport, hits[1].Port));
    }

    [TestMethod]
    public void Merge_CollapsesSameEndpointPreferringAKnownKindAndOrdersByAddress()
    {
        var merged = NetworkDiscovery.Merge(
        [
            new NetworkDeviceHit("10.0.0.10", 80, "tcp", "upnp", "x", "ssdp"),
            new NetworkDeviceHit("10.0.0.2", 5555, "tcp", "unknown", "?", "mdns"),
            new NetworkDeviceHit("10.0.0.2", 5555, "tcp", "lxi", "RIGOL", "lxi"),
        ]);

        Assert.HasCount(2, merged);
        Assert.AreEqual("lxi", merged[0].Kind);
        Assert.AreEqual("10.0.0.10", merged[1].Address);
    }

    [TestMethod]
    public async Task DiscoverAsync_ASingleFailingProbeDoesNotLoseTheOthers()
    {
        var discovery = new NetworkDiscovery([new ThrowingProbe(), new LxiProbe((_, _, _) => Task.FromResult<IReadOnlyList<LxiDevice>>([new LxiDevice("10.0.0.2", 111, 5025, "ACME")]))]);

        var hits = await discovery.DiscoverAsync(TimeSpan.FromMilliseconds(10), TestContext.CancellationToken);

        Assert.HasCount(1, hits);
    }

    private static byte[] MdnsAnswer()
    {
        var message = new List<byte> { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 2 };
        message.AddRange(Name("_mqtt._tcp.local"));
        message.AddRange(Record(12, Name("broker._mqtt._tcp.local")));
        message.AddRange(Name("broker._mqtt._tcp.local"));
        message.AddRange(Record(33, [0, 0, 0, 0, 0x07, 0x5B, .. Name("host.local")]));
        message.AddRange(Name("host.local"));
        message.AddRange(Record(1, [10, 0, 0, 5]));
        message[7] = 1;
        message[11] = 2;
        return [.. message];
    }

    private static byte[] Name(string name)
    {
        var bytes = new List<byte>();
        foreach (var label in name.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }

        bytes.Add(0);
        return [.. bytes];
    }

    private static byte[] Record(int type, byte[] data) => [0, (byte)type, 0, 1, 0, 0, 0, 120, (byte)(data.Length >> 8), (byte)data.Length, .. data];

    private sealed class CannedExchange(params UdpReply[] replies) : IUdpExchange
    {
        public Task<IReadOnlyList<UdpReply>> ExchangeAsync(IPEndPoint target, byte[] request, TimeSpan listenFor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UdpReply>>(replies);
    }

    private sealed class ThrowingProbe : INetworkDeviceProbe
    {
        public string Name => "boom";

        public Task<IReadOnlyList<NetworkDeviceHit>> ProbeAsync(TimeSpan listenFor, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
