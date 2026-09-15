using System.IO.Pipelines;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Hid;

/// <summary>
/// <see cref="ITransport"/> for a USB HID device connection. See docs/design/transports.md.
/// </summary>
public sealed class HidTransport : ITransport
{
    private readonly IHidDeviceFactory _deviceFactory;
    private readonly IOptions<HidTransportOptions> _options;
    private IHidDevice? _device;
    private ConnectionState _state = ConnectionState.Closed;
    private Pipe? _pipe;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    public HidTransport(IHidDeviceFactory deviceFactory, IOptions<HidTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(deviceFactory);
        ArgumentNullException.ThrowIfNull(options);

        _deviceFactory = deviceFactory;
        _options = options;
    }

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            var previous = _state;
            _state = value;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previous, value));
        }
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public PipeReader Input => _pipe?.Reader ?? throw new InvalidOperationException("The HID transport has not been opened.");

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Open or ConnectionState.Opening)
        {
            return Task.CompletedTask;
        }

        State = ConnectionState.Opening;

        var device = _deviceFactory.Create(_options.Value);

        try
        {
            device.Open();
        }
        catch
        {
            device.Dispose();
            State = ConnectionState.Faulted;
            throw;
        }

        _device = device;
        _pipe = new Pipe();
        _pumpCts = new CancellationTokenSource();

        var pipe = _pipe;
        _pumpTask = Task.Run(() => StreamToPipePump.RunAsync(device.BaseStream, pipe.Writer, _pumpCts.Token), CancellationToken.None);
        _ = _pumpTask.ContinueWith(
            _ =>
            {
                // The pump ends either because we're deliberately closing (State already moved
                // past Open by then) or because the device faulted/disconnected underneath us.
                if (State == ConnectionState.Open)
                {
                    State = ConnectionState.Closed;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        State = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_device is null)
        {
            return;
        }

        State = ConnectionState.Closing;

        _pumpCts?.Cancel();
        if (_pumpTask is not null)
        {
            await _pumpTask.ConfigureAwait(false);
        }

        _pumpCts?.Dispose();
        _pumpCts = null;
        _pumpTask = null;
        _pipe = null;

        _device.Close();
        _device.Dispose();
        _device = null;

        State = ConnectionState.Closed;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_device is null || State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The HID transport is not open.");
        }

        // Unlike serial/TCP, a zero-length write is invalid at the Windows HID API level (a
        // report is a fixed, non-zero, device-defined length) rather than a harmless no-op -
        // confirmed against real hardware, where this threw a raw Win32 IOException. Matching
        // serial/TCP's actual behavior for the same input is the least surprising fix; a wrong
        // *non-zero* length is still a real device-specific framing error and correctly still
        // throws (see HidTransport's callers for how that's reported instead of crashing).
        if (data.IsEmpty)
        {
            return Task.CompletedTask;
        }

        var buffer = data.ToArray();
        _device.Write(buffer, 0, buffer.Length);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }
}
