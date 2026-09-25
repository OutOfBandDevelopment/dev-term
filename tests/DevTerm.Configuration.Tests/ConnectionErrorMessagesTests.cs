using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ConnectionErrorMessagesTests
{
    [TestMethod]
    public void For_SerialTransport_SuggestsListPorts()
    {
        var message = ConnectionErrorMessages.For("serial", new IOException("Could not find file 'COM9'."));

        Assert.Contains("--listports", message);
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

        Assert.Contains("Could not find file 'COM9'.", message);
    }

    [TestMethod]
    public void IsConnectionFailure_TreatsATimeoutAsAConnectionFailure_NotACrash() => Assert.IsTrue(ConnectionErrorMessages.IsConnectionFailure(new TimeoutException("The operation has timed out.")));

    [TestMethod]
    public void IsConnectionFailure_StillRejectsAnUnrelatedBug()
    {
        Assert.IsFalse(ConnectionErrorMessages.IsConnectionFailure(new NullReferenceException()));
        Assert.IsFalse(ConnectionErrorMessages.IsConnectionFailure(new NotSupportedException()));
    }

    [TestMethod]
    public void For_ATimeout_ReportsItLikeAnyOtherConnectionFailure()
    {
        var message = ConnectionErrorMessages.For("hid", new TimeoutException("The operation has timed out."));

        Assert.Contains("The operation has timed out.", message);
        Assert.Contains("--listhiddevices", message);
    }

    [TestMethod]
    public void For_TransportNameIsCaseInsensitive()
    {
        var message = ConnectionErrorMessages.For("SERIAL", new UnauthorizedAccessException("Access denied"));

        Assert.Contains("--listports", message);
    }
}
