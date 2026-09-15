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

        switch (options.Transport.ToLowerInvariant())
        {
            case "serial":
                if (string.IsNullOrWhiteSpace(options.Port))
                {
                    return ValidateOptionsResult.Fail("Missing required '--port' for the serial transport.");
                }

                break;

            case "tcp":
                if (options.TcpPort is < 1 or > 65535)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--tcpport' for the TCP transport (expected 1-65535).");
                }

                if (!options.Listen && string.IsNullOrWhiteSpace(options.Host))
                {
                    return ValidateOptionsResult.Fail("The TCP transport requires '--host' unless '--listen true' is set.");
                }

                break;

            case "hid":
                if (options.HidVendorId is < 1 or > 0xFFFF)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--hidvendorid' for the HID transport (expected 1-65535, decimal).");
                }

                if (options.HidProductId is < 1 or > 0xFFFF)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--hidproductid' for the HID transport (expected 1-65535, decimal).");
                }

                break;

            default:
                return ValidateOptionsResult.Fail($"Unknown transport '{options.Transport}'. Expected 'serial', 'tcp', or 'hid'.");
        }

        return ValidateOptionsResult.Success;
    }
}
