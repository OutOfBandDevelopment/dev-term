namespace DevTerm.Transports.Loopback.Tests;

[TestCategory("UNIT")]
[TestClass]
public sealed class LoopbackTransportOptionsValidatorTests
{
    [TestMethod]
    public void Validate_AlwaysSucceeds()
    {
        var result = new LoopbackTransportOptionsValidator().Validate(null, new LoopbackTransportOptions());

        Assert.IsTrue(result.Succeeded);
    }
}
