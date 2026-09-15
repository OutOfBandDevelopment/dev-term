namespace DevTerm.Transports.Hid;

/// <summary>
/// Thin abstraction over a HidSharp device/stream so <see cref="HidTransport"/> can be unit
/// tested with a fake/mock instead of a real HID device.
/// </summary>
public interface IHidDevice : IDisposable
{
    bool IsOpen { get; }

    /// <summary>The readable stream <see cref="HidTransport"/> pumps into a pipe (see <see cref="DevTerm.Core.Transports.StreamToPipePump"/>).</summary>
    Stream BaseStream { get; }

    void Open();

    void Close();

    void Write(byte[] buffer, int offset, int count);
}
