using Microsoft.Extensions.Options;

namespace DevTerm.Configuration;

public sealed class CliOptionsValidator : IValidateOptions<CliOptions>
{
    public ValidateOptionsResult Validate(string? name, CliOptions options)
    {
        if (options.AsciiMaxLineLength < 0)
        {
            return ValidateOptionsResult.Fail("'--asciimaxlinelength' must be 0 (unbounded) or a positive maximum length.");
        }

        if (options.ScpiAutoDetectTimeoutMs is < 100 or > 60000)
        {
            return ValidateOptionsResult.Fail("'--scpiautodetecttimeoutms' must be between 100 and 60000.");
        }

        switch (options.Transport.ToLowerInvariant())
        {
            case "serial":
                if (string.IsNullOrWhiteSpace(options.Port))
                {
                    return ValidateOptionsResult.Fail("Missing required '--port' for the serial transport.");
                }

                break;

            case "tcp":
                if (!int.TryParse(options.Port, out var tcpPort) || tcpPort is < 1 or > 65535)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--port' for the TCP transport (expected 1-65535).");
                }

                if (!options.Listen && string.IsNullOrWhiteSpace(options.Host))
                {
                    return ValidateOptionsResult.Fail("The TCP transport requires '--host' unless '--listen true' is set.");
                }

                break;

            case "hid":
                if (options.VendorId is < 1 or > 0xFFFF)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--vendorid' for the HID transport (expected 1-65535, decimal).");
                }

                if (options.ProductId is < 1 or > 0xFFFF)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--productid' for the HID transport (expected 1-65535, decimal).");
                }

                break;

            case "usbtmc":
                if (options.VendorId is < 1 or > 0xFFFF)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--vendorid' for the USBTMC transport (expected 1-65535, decimal).");
                }

                if (options.ProductId is < 1 or > 0xFFFF)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--productid' for the USBTMC transport (expected 1-65535, decimal).");
                }

                break;

            case "ble":
                if (string.IsNullOrWhiteSpace(options.BleDeviceId))
                {
                    return ValidateOptionsResult.Fail("Missing required '--bledeviceid' for the BLE transport.");
                }

                break;

            case "loopback":
                break;

            default:
                return ValidateOptionsResult.Fail($"Unknown transport '{options.Transport}'. Expected 'serial', 'tcp', 'hid', 'usbtmc', 'ble', or 'loopback'.");
        }

        return ValidateOptionsResult.Success;
    }
}
