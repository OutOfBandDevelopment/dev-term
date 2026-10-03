using DevTerm.Test.Utilities;

namespace DevTerm.Observability.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class TelemetryExporterTests
{
    [TestMethod]
    public void ParseEndpoint_TrueMeansTheLocalCollector()
    {
        Assert.AreEqual(new Uri("http://localhost:4317"), TelemetryExporter.ParseEndpoint("true"));
        Assert.AreEqual(new Uri("https://otel.example:4317"), TelemetryExporter.ParseEndpoint("https://otel.example:4317"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("false")]
    [DataRow("localhost:4317")]
    [DataRow("ftp://host/")]
    public void ParseEndpoint_RejectsAnythingThatIsNotAnHttpUrl(string? text)
    {
        Assert.IsNull(TelemetryExporter.ParseEndpoint(text));
    }

    [TestMethod]
    public void Start_AgainstAnUnreachableCollector_DoesNotThrowAndDisposesCleanly()
    {
        using var exporter = TelemetryExporter.Start(new Uri("http://127.0.0.1:1"), "devterm-test");

        Assert.IsNotNull(exporter);
    }
}
