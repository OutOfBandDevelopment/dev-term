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

        if (options.ReadTimeoutMs < -1)
        {
            return ValidateOptionsResult.Fail("'--readtimeoutms' must be -1 (infinite) or a non-negative timeout in milliseconds.");
        }

        if (options.WriteTimeoutMs < -1)
        {
            return ValidateOptionsResult.Fail("'--writetimeoutms' must be -1 (infinite) or a non-negative timeout in milliseconds.");
        }

        if (options.PlaybackSpeed < 0)
        {
            return ValidateOptionsResult.Fail("'--playbackspeed' must be 0 or greater.");
        }

        if (options.WriteByteDelayMs < -1)
        {
            return ValidateOptionsResult.Fail("'--writebytedelayms' must be -1 (disabled), 0 (no delay), or a positive number of milliseconds.");
        }

        if (options.LoopbackSampleIntervalMs < 0)
        {
            return ValidateOptionsResult.Fail("'--loopbacksampleintervalms' must be 0 or greater.");
        }

        if (options.StreamConvertDpi <= 0)
        {
            return ValidateOptionsResult.Fail("'--streamconvertdpi' must be a positive number.");
        }

        var streamConvertMode = (options.StreamConvertMode ?? "none").Trim().ToLowerInvariant();
        switch (streamConvertMode)
        {
            case "none":
                break;

            case "externaltool":
                if (string.IsNullOrWhiteSpace(options.StreamConvertExternalToolPath))
                {
                    return ValidateOptionsResult.Fail("'--streamconvertexternaltoolpath' is required when '--streamconvertmode' is 'externaltool'.");
                }

                break;

            case "webservice":
                if (string.IsNullOrWhiteSpace(options.StreamConvertWebServiceUrl) ||
                    !Uri.TryCreate(options.StreamConvertWebServiceUrl, UriKind.Absolute, out _))
                {
                    return ValidateOptionsResult.Fail("'--streamconvertwebserviceurl' must be a valid absolute URL when '--streamconvertmode' is 'webservice'.");
                }

                break;

            case "internalhpgltosvg":
            case "auto":
                break;

            case var named when named.StartsWith("tool:", StringComparison.Ordinal):
                var toolName = named["tool:".Length..].Trim();
                if (!options.StreamConvertTools.Any(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)))
                {
                    return ValidateOptionsResult.Fail($"'--streamconvertmode' names the tool '{toolName}', which is not in StreamConvertTools.");
                }

                break;

            default:
                return ValidateOptionsResult.Fail($"Unknown '--streamconvertmode' '{options.StreamConvertMode}'. Expected 'none', 'externaltool', 'webservice', 'internalhpgltosvg', 'auto', or 'tool:<name>'.");
        }

        foreach (var tool in options.StreamConvertTools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.Path))
            {
                return ValidateOptionsResult.Fail("Every StreamConvertTools entry needs a Name and a Path.");
            }
        }

        if (options.StreamConvertTools.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            return ValidateOptionsResult.Fail("StreamConvertTools names must be unique.");
        }

        switch (options.Transport.ToLowerInvariant())
        {
            case "serial":
                if (string.IsNullOrWhiteSpace(options.Port))
                {
                    return ValidateOptionsResult.Fail("Missing required '--port' for the serial transport.");
                }

                if (options.Baud <= 0)
                {
                    return ValidateOptionsResult.Fail("'--baud' must be a positive number.");
                }

                if (options.DataBits is < 5 or > 8)
                {
                    return ValidateOptionsResult.Fail("'--databits' must be between 5 and 8.");
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

            case "rfc2217":
                if (!int.TryParse(options.Port, out var rfc2217Port) || rfc2217Port is < 1 or > 65535)
                {
                    return ValidateOptionsResult.Fail("Missing or invalid '--port' for the RFC 2217 transport (expected 1-65535).");
                }

                if (string.IsNullOrWhiteSpace(options.Host))
                {
                    return ValidateOptionsResult.Fail("Missing required '--host' for the RFC 2217 transport.");
                }

                if (options.Baud <= 0)
                {
                    return ValidateOptionsResult.Fail("'--baud' must be a positive number.");
                }

                if (options.DataBits is < 5 or > 8)
                {
                    return ValidateOptionsResult.Fail("'--databits' must be between 5 and 8.");
                }

                break;

            case "loopback":
                break;

            default:
                return ValidateOptionsResult.Fail($"Unknown transport '{options.Transport}'. Expected 'serial', 'tcp', 'hid', 'usbtmc', 'ble', 'rfc2217', or 'loopback'.");
        }

        return ValidateOptionsResult.Success;
    }
}
