namespace DevTerm.Configuration.Tests;

[TestClass]
public sealed class ConnectionErrorMessagesTests
{
    [TestMethod]
    public void For_SerialTransport_SuggestsListPorts()
    {
        var message = ConnectionErrorMessages.For("serial", new IOException("Could not find file 'COM9'."));

        StringAssert.Contains(message, "--listports");
    }

    [TestMethod]
    public void For_TcpTransport_DoesNotSuggestListPorts()
    {
        var message = ConnectionErrorMessages.For("tcp", new IOException("Connection refused"));

        Assert.DoesNotContain("--listports", message);
    }

    [TestMethod]
    public void For_IncludesTheOriginalExceptionMessage()
    {
        var message = ConnectionErrorMessages.For("serial", new IOException("Could not find file 'COM9'."));

        StringAssert.Contains(message, "Could not find file 'COM9'.");
    }

    [TestMethod]
    public void For_TransportNameIsCaseInsensitive()
    {
        var message = ConnectionErrorMessages.For("SERIAL", new UnauthorizedAccessException("Access denied"));

        StringAssert.Contains(message, "--listports");
    }
}
