using DevTerm.Core.Hosting;
using DevTerm.Devices.Busylight;
using DevTerm.Devices.K8055;
using DevTerm.Devices.Scpi;
using DevTerm.Presenters.Text;
using DevTerm.Transports.Hid;
using DevTerm.Transports.Loopback;
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
        services.AddDevTermCore();
        services.AddTextPresenters();
        services.AddK8055Presenter();
        services.AddBusylightPresenter();
        services.AddScpiPresenter();
        services.Configure<AsciiPresenterOptions>(o => o.MaxLineLength = cliOptions.AsciiMaxLineLength);

        if (string.Equals(cliOptions.Transport, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddTcpTransport();
            services.Configure<TcpTransportOptions>(o =>
            {
                o.Mode = cliOptions.Listen ? TcpTransportMode.Listener : TcpTransportMode.Client;
                o.Host = cliOptions.Host;
                o.Port = int.TryParse(cliOptions.Port, out var tcpPort) ? tcpPort : 0;
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
                o.WriteTimeoutMs = cliOptions.WriteTimeoutMs;
                o.ReadTimeoutMs = cliOptions.ReadTimeoutMs;
            });
        }
        else if (string.Equals(cliOptions.Transport, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            services.AddLoopbackTransport();
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
            });
        }

        return services;
    }
}
