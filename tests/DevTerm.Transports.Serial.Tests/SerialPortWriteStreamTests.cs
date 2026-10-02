using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Transports.Serial.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Serial)]
[TestClass]
public sealed class SerialPortWriteStreamTests
{
    [TestMethod]
    public async Task WriteAsync_Memory_WritesThroughToThePort()
    {
        var port = new Mock<ISerialPort>();
        using var stream = new SerialPortWriteStream(port.Object);

        await stream.WriteAsync(new byte[] { 1, 2, 3 });

        port.Verify(p => p.Write(new byte[] { 1, 2, 3 }, 0, 3), Times.Once);
    }

    [TestMethod]
    public void Write_WritesThroughToThePort()
    {
        var port = new Mock<ISerialPort>();
        using var stream = new SerialPortWriteStream(port.Object);
        var buffer = new byte[] { 9, 8, 7 };

        stream.Write(buffer, 0, buffer.Length);

        port.Verify(p => p.Write(buffer, 0, 3), Times.Once);
    }

    [TestMethod]
    public void CanRead_CanSeek_AreFalse_CanWrite_IsTrue()
    {
        var port = new Mock<ISerialPort>();
        using var stream = new SerialPortWriteStream(port.Object);

        Assert.IsFalse(stream.CanRead);
        Assert.IsFalse(stream.CanSeek);
        Assert.IsTrue(stream.CanWrite);
    }

    [TestMethod]
    public void Read_Throws()
    {
        var port = new Mock<ISerialPort>();
        using var stream = new SerialPortWriteStream(port.Object);

        Assert.ThrowsExactly<NotSupportedException>(() => stream.Read(new byte[1], 0, 1));
    }
}
