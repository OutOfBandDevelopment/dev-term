using DevTerm.Core.Hosting;
using DevTerm.Devices.Busylight;
using DevTerm.Devices.De5000;
using DevTerm.Devices.K8055;
using DevTerm.Devices.Nmea;
using DevTerm.Devices.RadexOne;
using DevTerm.Devices.Scpi;
using DevTerm.Devices.ZoomH4n;
using DevTerm.Core.Plugins;
using DevTerm.Observability;
using DevTerm.Presenters.Text;
using DevTerm.Transports.Ble;
using DevTerm.Transports.Hid;
using DevTerm.Transports.Loopback;
using DevTerm.Transports.Brokers;
using DevTerm.Transports.Mqtt;
using DevTerm.Transports.Rfc2217;
using DevTerm.Transports.Serial;
using DevTerm.Transports.Tcp;
using DevTerm.Transports.Usbtmc;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core engine, text presenters, and whichever transport <paramref name="cliOptions"/>
    /// selects, configured from it. The one piece every front end (console CLI/TUI, WPF) calls so
    /// they all wire up identically from the same bound options instead of duplicating this
    /// per front end.
    /// </summary>
    public static IServiceCollection AddDevTermFrontEnd(this IServiceCollection services, CliOptions cliOptions)
    {
        services.AddDevTermPresenters(cliOptions);
        StartTelemetry(cliOptions);

        if (string.Equals(cliOptions.Transport, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddTcpTransport();
            services.Configure<TcpTransportOptions>(o =>
            {
                o.Mode = cliOptions.Listen ? TcpTransportMode.Listener : TcpTransportMode.Client;
                o.Host = cliOptions.Host;
                o.Port = int.TryParse(cliOptions.Port, out var tcpPort) ? tcpPort : 0;
                o.WriteTimeoutMs = cliOptions.WriteTimeoutMs;
                o.WriteByteDelayMs = cliOptions.WriteByteDelayMs;
            });
        }
        else if (string.Equals(cliOptions.Transport, "hid", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHidTransport();
            services.Configure<HidTransportOptions>(o =>
            {
                o.VendorId = cliOptions.VendorId;
                o.ProductId = cliOptions.ProductId;
                o.SerialNumber = cliOptions.SerialNumber;
                o.DevicePath = cliOptions.DevicePath;
            });
        }
        else if (string.Equals(cliOptions.Transport, "usbtmc", StringComparison.OrdinalIgnoreCase))
        {
            services.AddUsbtmcTransport();
            services.Configure<UsbtmcTransportOptions>(o =>
            {
                o.VendorId = cliOptions.VendorId;
                o.ProductId = cliOptions.ProductId;
                o.SerialNumber = cliOptions.SerialNumber;
                o.DevicePath = cliOptions.DevicePath;
                o.WriteTimeoutMs = cliOptions.WriteTimeoutMs;
                o.ReadTimeoutMs = cliOptions.ReadTimeoutMs;
            });
        }
        else if (string.Equals(cliOptions.Transport, "ble", StringComparison.OrdinalIgnoreCase))
        {
            services.AddBleTransport();
            services.Configure<BleTransportOptions>(o =>
            {
                o.DeviceId = cliOptions.BleDeviceId ?? string.Empty;
                if (cliOptions.BleServiceUuid is { Length: > 0 } serviceUuid)
                {
                    o.ServiceUuid = serviceUuid;
                }

                if (cliOptions.BleWriteCharacteristicUuid is { Length: > 0 } writeUuid)
                {
                    o.WriteCharacteristicUuid = writeUuid;
                }

                if (cliOptions.BleNotifyCharacteristicUuid is { Length: > 0 } notifyUuid)
                {
                    o.NotifyCharacteristicUuid = notifyUuid;
                }

                o.WriteTimeoutMs = cliOptions.WriteTimeoutMs;
            });

            // Must come after AddBleTransport() - see BlePlatformAdapterLoader's doc comment for why
            // its registrations (added via plain AddSingleton) need to be the last ones added.
            BlePlatformAdapterLoader.TryRegisterPlatformAdapter(services);
        }
        else if (string.Equals(cliOptions.Transport, "rfc2217", StringComparison.OrdinalIgnoreCase))
        {
            services.AddRfc2217Transport();
            services.Configure<Rfc2217TransportOptions>(o =>
            {
                o.Host = cliOptions.Host ?? string.Empty;
                o.Port = int.TryParse(cliOptions.Port, out var rfc2217Port) ? rfc2217Port : 0;
                o.BaudRate = cliOptions.Baud;
                o.DataBits = cliOptions.DataBits;
                o.Parity = cliOptions.Parity;
                o.StopBits = cliOptions.StopBits;
                o.DtrEnable = cliOptions.Dtr;
                o.RtsEnable = cliOptions.Rts;
                o.WriteTimeoutMs = cliOptions.WriteTimeoutMs;
                o.WriteByteDelayMs = cliOptions.WriteByteDelayMs;
            });
        }
        else if (string.Equals(cliOptions.Transport, "mqtt", StringComparison.OrdinalIgnoreCase))
        {
            services.AddMqttTransport();
            services.Configure<MqttTransportOptions>(o =>
            {
                o.Host = cliOptions.Host ?? string.Empty;
                o.Port = int.TryParse(cliOptions.Port, out var mqttPort) ? mqttPort : 0;
                o.SubscribeTopics = [.. (cliOptions.Subscribe ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                o.PublishTopic = cliOptions.Publish;
                o.Username = cliOptions.Username;
                o.Password = cliOptions.Password;
                o.TimeoutMs = cliOptions.WriteTimeoutMs;
            });
        }
        else if (cliOptions.Transport is { } brokerName && brokerName.ToLowerInvariant() is "amqp" or "stomp")
        {
            if (brokerName.Equals("amqp", StringComparison.OrdinalIgnoreCase))
            {
                services.AddAmqpTransport();
            }
            else
            {
                services.AddStompTransport();
            }

            services.Configure<BrokerTransportOptions>(o =>
            {
                o.Host = cliOptions.Host ?? string.Empty;
                o.Port = int.TryParse(cliOptions.Port, out var brokerPort) ? brokerPort : 0;
                o.SubscribeTopics = [.. (cliOptions.Subscribe ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                o.PublishTopic = cliOptions.Publish;
                o.Username = cliOptions.Username;
                o.Password = cliOptions.Password;
                o.TimeoutMs = cliOptions.WriteTimeoutMs;
            });
        }
        else if (string.Equals(cliOptions.Transport, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            services.AddLoopbackTransport();
            services.Configure<LoopbackTransportOptions>(o => o.SampleIntervalMs = cliOptions.LoopbackSampleIntervalMs);
        }
        else
        {
            services.AddSerialTransport();
            services.Configure<SerialTransportOptions>(o =>
            {
                o.PortName = cliOptions.Port!;
                o.BaudRate = cliOptions.Baud;
                o.DataBits = cliOptions.DataBits;
                o.Parity = cliOptions.Parity;
                o.StopBits = cliOptions.StopBits;
                o.Handshake = cliOptions.Handshake;
                o.WriteTimeoutMs = cliOptions.WriteTimeoutMs;
                o.ReadTimeoutMs = cliOptions.ReadTimeoutMs;
                o.DtrEnable = cliOptions.Dtr;
                o.RtsEnable = cliOptions.Rts;
                o.WriteByteDelayMs = cliOptions.WriteByteDelayMs;
            });
        }

        return services;
    }

    /// <summary>Loads plugins from <see cref="CliOptions.Plugins"/> (default: <c>plugins</c> next to the app) and registers their <see cref="PluginLoadResult"/>s.</summary>
    public static IServiceCollection AddPlugins(this IServiceCollection services, CliOptions cliOptions)
    {
        var directory = string.IsNullOrWhiteSpace(cliOptions.Plugins) ? Path.Combine(AppContext.BaseDirectory, "plugins") : cliOptions.Plugins;
        var results = PluginLoader.LoadAll(directory, services);
        services.AddSingleton<IReadOnlyList<PluginLoadResult>>(results);
        return services;
    }

    // Opt-in (--otlp). Started here rather than as a hosted service because the WPF and console front ends build their
    // host but never start it; flushed when the process exits.
    private static void StartTelemetry(CliOptions cliOptions)
    {
        if (TelemetryExporter.ParseEndpoint(cliOptions.Otlp) is not { } endpoint)
        {
            return;
        }

        var exporter = TelemetryExporter.Start(endpoint, "devterm");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => exporter.Dispose();
    }

    /// <summary>
    /// The core engine and every presenter, configured from <paramref name="cliOptions"/> — but no
    /// transport. What <see cref="AddDevTermFrontEnd"/> builds on, and all playback composes
    /// (<see cref="PlaybackPresenters"/>), so replaying a log can't reach a real device.
    /// </summary>
    public static IServiceCollection AddDevTermPresenters(this IServiceCollection services, CliOptions cliOptions)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        services.AddDevTermCore();
        services.AddTextPresenters();
        services.AddK8055Presenter();
        services.AddBusylightPresenter();
        services.AddScpiPresenter();
        services.AddRadexOnePresenter();
        services.AddZoomH4nPresenter();
        services.AddDe5000Presenter();
        services.AddNmeaGpsPresenter();
        services.AddPlugins(cliOptions);
        services.Configure<AsciiPresenterOptions>(o => o.MaxLineLength = cliOptions.AsciiMaxLineLength);
        services.AddStreamCaptureConverter(cliOptions);
        return services;
    }

    /// <summary>
    /// Registers <see cref="StreamCaptureConverter"/> and its options, bound from <paramref name="cliOptions"/>'s
    /// <c>StreamConvert*</c> properties.
    /// </summary>
    public static IServiceCollection AddStreamCaptureConverter(this IServiceCollection services, CliOptions cliOptions)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        services.Configure<StreamCaptureConverterOptions>(o => StreamCaptureConverterOptions.CopyFrom(cliOptions, o));
        services.AddSingleton<StreamCaptureConverter>();
        return services;
    }
}
