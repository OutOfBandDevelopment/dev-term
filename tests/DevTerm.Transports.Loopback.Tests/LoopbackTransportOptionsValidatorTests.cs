using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Loopback.Tests;

[TestCategory(TestCategories.Unit)]
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
