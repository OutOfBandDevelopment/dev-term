using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Loopback.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Loopback)]
[TestClass]
public sealed class LoopbackTransportOptionsValidatorTests
{
    [TestMethod]
    public void Validate_WithDefaultOptions_Succeeds()
    {
        var result = new LoopbackTransportOptionsValidator().Validate(null, new LoopbackTransportOptions());

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_WithPositiveSampleIntervalMs_Succeeds()
    {
        var result = new LoopbackTransportOptionsValidator().Validate(null, new LoopbackTransportOptions { SampleIntervalMs = 50 });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_WithNegativeSampleIntervalMs_Fails()
    {
        var result = new LoopbackTransportOptionsValidator().Validate(null, new LoopbackTransportOptions { SampleIntervalMs = -1 });

        Assert.IsFalse(result.Succeeded);
    }
}
