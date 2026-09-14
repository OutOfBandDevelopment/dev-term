namespace DevTerm.Transports.Serial;

/// <summary>
/// Thin abstraction over <see cref="System.IO.Ports.SerialPort"/> so <see cref="SerialTransport"/>
/// can be unit tested with a fake/mock instead of a real serial port.
/// </summary>
public interface ISerialPort : IDisposable
{
    bool IsOpen { get; }

    /// <summary>The readable/writable stream <see cref="SerialTransport"/> pumps into a pipe (see <see cref="DevTerm.Core.Transports.StreamToPipePump"/>).</summary>
    Stream BaseStream { get; }

    void Open();

    void Close();

    void Write(byte[] buffer, int offset, int count);
}
