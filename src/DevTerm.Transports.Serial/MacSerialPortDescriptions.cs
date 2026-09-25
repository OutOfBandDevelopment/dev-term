using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace DevTerm.Transports.Serial;

/// <summary>
/// Derives a description for each macOS serial port from the IOKit registry, via the stock
/// <c>ioreg</c> tool rather than P/Invoking IOKit/CoreFoundation. <c>ioreg -a -l -r -c IOUSBHostDevice</c>
/// prints every USB device as a plist dict, with its descendants nested under
/// <c>IORegistryEntryChildren</c>; each serial port shows up as an <c>IOSerialBSDClient</c>
/// somewhere below its USB device (device → <c>IOUSBHostInterface</c> → driver, e.g.
/// <c>AppleUSBFTDI</c>/<c>AppleUSBACMData</c> → <c>IOSerialBSDClient</c>), carrying both BSD paths
/// (<c>IOCalloutDevice</c> = <c>/dev/cu.usbserial-A50285BI</c>, <c>IODialinDevice</c> =
/// <c>/dev/tty.usbserial-A50285BI</c>). The nearest USB device ancestor supplies
/// <c>"USB Vendor Name"</c>/<c>"USB Product Name"</c>/<c>"USB Serial Number"</c> (or their
/// <c>kUSB*String</c> twins) and <c>idVendor</c>/<c>idProduct</c>.
///
/// Both BSD paths get the same description — <see cref="System.IO.Ports.SerialPort.GetPortNames"/>
/// lists both on macOS. Anything not under a USB device (<c>/dev/cu.Bluetooth-Incoming-Port</c>)
/// gets no entry. Best-effort throughout: a missing <c>ioreg</c>, a non-zero exit, a timeout (the
/// process is killed, never waited on unboundedly) or unparseable output all yield no descriptions.
/// </summary>
internal static class MacSerialPortDescriptions
{
    public const string IoregPath = "/usr/sbin/ioreg";

    public static readonly IReadOnlyList<string> IoregArguments = ["-a", "-l", "-r", "-c", "IOUSBHostDevice"];

    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(3);

    private static readonly string[] _vendorKeys = ["USB Vendor Name", "kUSBVendorString"];
    private static readonly string[] _productKeys = ["USB Product Name", "kUSBProductString"];
    private static readonly string[] _serialKeys = ["USB Serial Number", "kUSBSerialNumberString"];

    public static IReadOnlyDictionary<string, string> Read()
    {
        var output = RunProcess(IoregPath, IoregArguments, _defaultTimeout);
        return output is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : Parse(output);
    }

    /// <summary>Parses <c>ioreg -a</c> plist output. Never throws; malformed input yields no entries.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string ioregPlist)
    {
        ArgumentNullException.ThrowIfNull(ioregPlist);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        XDocument document;
        try
        {
            // ioreg's plist carries a <!DOCTYPE>; the default reader settings reject any DTD.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(ioregPlist), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return result;
        }

        var top = document.Root?.Elements().FirstOrDefault();
        if (top is null)
        {
            return result;
        }

        // -r prints an <array> of root dicts; be lenient about a single bare <dict> too.
        var roots = top.Name.LocalName == "array" ? top.Elements("dict") : top.Name.LocalName == "dict" ? [top] : [];
        foreach (var root in roots)
        {
            Walk(root, usbDevice: null, result);
        }

        return result;
    }

    /// <summary>
    /// Runs a process and returns its stdout, or null if it can't be started, exits non-zero, or
    /// doesn't finish within <paramref name="timeout"/> (in which case it's killed).
    /// </summary>
    internal static string? RunProcess(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            // Drain both pipes concurrently so a chatty process can't block on a full pipe.
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                TryKill(process);
                return null;
            }

            return stdout.Wait(timeout) && process.ExitCode == 0 ? stdout.Result : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static void Walk(XElement dict, XElement? usbDevice, Dictionary<string, string> result)
    {
        var properties = ReadDict(dict);
        var objectClass = properties.TryGetValue("IOObjectClass", out var classElement) ? classElement.Value : null;

        // Only the device node itself: its IOUSBHostInterface children carry idVendor/idProduct
        // too, but not the device's string descriptors, so they must not replace it.
        if (objectClass is "IOUSBHostDevice" or "IOUSBDevice" || HasAny(properties, _productKeys))
        {
            usbDevice = dict;
        }

        if (usbDevice is not null && objectClass == "IOSerialBSDClient")
        {
            var description = Describe(ReadDict(usbDevice));
            if (description is not null)
            {
                foreach (var key in (string[])["IOCalloutDevice", "IODialinDevice"])
                {
                    if (properties.TryGetValue(key, out var path) && path.Value.StartsWith("/dev/", StringComparison.Ordinal))
                    {
                        result.TryAdd(path.Value, description);
                    }
                }
            }
        }

        if (properties.TryGetValue("IORegistryEntryChildren", out var children) && children.Name.LocalName == "array")
        {
            foreach (var child in children.Elements("dict"))
            {
                Walk(child, usbDevice, result);
            }
        }
    }

    private static string? Describe(Dictionary<string, XElement> usbDevice)
    {
        var ids = TryInteger(usbDevice, "idVendor", out var vendorId) && TryInteger(usbDevice, "idProduct", out var productId)
            ? SerialPortDescriptionFormat.Ids(vendorId, productId)
            : null;
        return SerialPortDescriptionFormat.Format(
            FirstString(usbDevice, _vendorKeys),
            FirstString(usbDevice, _productKeys),
            FirstString(usbDevice, _serialKeys),
            ids);
    }

    /// <summary>A plist <c>&lt;dict&gt;</c>'s alternating <c>&lt;key&gt;</c>/value children, as a lookup.</summary>
    private static Dictionary<string, XElement> ReadDict(XElement dict)
    {
        var properties = new Dictionary<string, XElement>(StringComparer.Ordinal);
        string? key = null;
        foreach (var element in dict.Elements())
        {
            if (element.Name.LocalName == "key")
            {
                key = element.Value;
            }
            else if (key is not null)
            {
                properties.TryAdd(key, element);
                key = null;
            }
        }

        return properties;
    }

    private static bool HasAny(Dictionary<string, XElement> properties, string[] keys) =>
        keys.Any(properties.ContainsKey);

    private static string? FirstString(Dictionary<string, XElement> properties, string[] keys) =>
        keys.Select(key => properties.TryGetValue(key, out var value) && value.Name.LocalName == "string" ? value.Value.Trim() : null)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

    private static bool TryInteger(Dictionary<string, XElement> properties, string key, out long value)
    {
        value = 0;
        return properties.TryGetValue(key, out var element)
            && element.Name.LocalName == "integer"
            && long.TryParse(element.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
        }
    }
}
