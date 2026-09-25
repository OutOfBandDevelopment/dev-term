namespace DevTerm.Transports.Serial;

/// <summary>
/// Derives a description for each Linux serial port from sysfs, the same attributes udev's
/// <c>ID_VENDOR</c>/<c>ID_MODEL</c>/<c>ID_SERIAL_SHORT</c> (and so <c>/dev/serial/by-id/</c>) are
/// built from. <c>/sys/class/tty/&lt;name&gt;/device</c> is a symlink to the port's device node:
/// the usb-serial port directory (<c>.../1-2/1-2:1.0/ttyUSB0</c>, FTDI/Prolific/CP210x/CH34x) or the
/// USB interface itself (<c>.../1-3/1-3:1.0</c>, <c>cdc_acm</c>'s <c>ttyACM0</c>). Walking up from
/// there, the first directory holding <c>idVendor</c> is the USB device, whose <c>manufacturer</c>,
/// <c>product</c> and <c>serial</c> files carry the device's own string descriptors.
///
/// Keys are <c>"/dev/&lt;name&gt;"</c>, exactly what <see cref="System.IO.Ports.SerialPort.GetPortNames"/>
/// reports on Linux. A port with no USB ancestor (a motherboard <c>ttyS0</c>, a platform UART) gets
/// no entry, as does anything whose sysfs can't be read — best-effort throughout, never throws.
///
/// The sysfs root and the single-level <c>readlink</c> are injectable so tests can build a fake
/// tree in a temp directory (with the links supplied as data, since creating real symlinks on
/// Windows needs a privilege tests can't count on).
/// </summary>
internal sealed class LinuxSerialPortDescriptions
{
    // Same bound Linux itself puts on symlink resolution (ELOOP after 40 links).
    private const int _maxLinkHops = 40;

    // /sys/devices/pci0000:00/0000:00:14.0/usb1/1-2/1-2.4/1-2.4:1.0/ttyUSB0 is about as deep as a
    // hub chain realistically goes below the port; stop well before walking the whole tree.
    private const int _maxAncestorLevels = 12;

    private readonly string _sysfsRoot;
    private readonly Func<string, string?> _readLink;

    public LinuxSerialPortDescriptions(string sysfsRoot, Func<string, string?> readLink)
    {
        ArgumentNullException.ThrowIfNull(sysfsRoot);
        ArgumentNullException.ThrowIfNull(readLink);
        _sysfsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sysfsRoot));
        _readLink = readLink;
    }

    /// <summary>Reads the real <c>/sys</c>.</summary>
    public static IReadOnlyDictionary<string, string> Read() =>
        new LinuxSerialPortDescriptions("/sys", DefaultReadLink).ReadAll();

    public IReadOnlyDictionary<string, string> ReadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(Path.Combine(_sysfsRoot, "class", "tty")).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            var description = Describe(name);
            if (description is not null)
            {
                result.TryAdd("/dev/" + name, description);
            }
        }

        return result;
    }

    /// <summary>The description for one tty (<c>"ttyUSB0"</c>), or null when it has no USB device behind it.</summary>
    public string? Describe(string ttyName)
    {
        ArgumentNullException.ThrowIfNull(ttyName);
        try
        {
            var device = RealPath(Path.Combine(_sysfsRoot, "class", "tty", ttyName, "device"));
            if (device is null || !Directory.Exists(device))
            {
                return null;
            }

            var usbDevice = FindUsbDevice(device);
            return usbDevice is null ? null : FormatDescription(usbDevice);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Formats a USB device directory's string descriptors: <c>"FTDI FT232R USB UART (0403:6001, serial A50285BI)"</c>.
    /// The manufacturer is dropped when the product already starts with it (<c>"Arduino"</c> +
    /// <c>"Arduino Uno"</c>). With no strings at all it falls back to the vendor/product id.
    /// </summary>
    internal static string? FormatDescription(string usbDeviceDirectory)
    {
        var manufacturer = ReadAttribute(usbDeviceDirectory, "manufacturer");
        var product = ReadAttribute(usbDeviceDirectory, "product");
        var serial = ReadAttribute(usbDeviceDirectory, "serial");
        var vendorId = ReadAttribute(usbDeviceDirectory, "idVendor");
        var productId = ReadAttribute(usbDeviceDirectory, "idProduct");
        var ids = vendorId is not null && productId is not null ? $"{vendorId}:{productId}" : null;
        return SerialPortDescriptionFormat.Format(manufacturer, product, serial, ids);
    }

    private string? FindUsbDevice(string deviceDirectory)
    {
        var current = deviceDirectory;
        for (var level = 0; level < _maxAncestorLevels && current is not null; level++)
        {
            if (!IsUnderRoot(current))
            {
                return null;
            }

            if (File.Exists(Path.Combine(current, "idVendor")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private bool IsUnderRoot(string path) =>
        path.Length > _sysfsRoot.Length
        && path.StartsWith(_sysfsRoot, StringComparison.Ordinal)
        && (path[_sysfsRoot.Length] == Path.DirectorySeparatorChar || path[_sysfsRoot.Length] == Path.AltDirectorySeparatorChar);

    /// <summary>
    /// A physical <c>realpath</c> built on a single-level <c>readlink</c>: resolves every symlinked
    /// component in turn, so a relative target (<c>"../../../ttyUSB0"</c>) is taken relative to the
    /// directory the link actually lives in, not the lexical path used to reach it — which is what
    /// sysfs needs, since <c>/sys/class/tty/ttyUSB0</c> is itself a link. Null after too many hops.
    /// </summary>
    internal string? RealPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        var pending = new Stack<string>(Split(fullPath[root.Length..]).Reverse());
        var current = root;
        var hops = 0;
        while (pending.Count > 0)
        {
            var component = pending.Pop();
            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            var candidate = Path.Combine(current, component);
            var target = _readLink(candidate);
            if (string.IsNullOrEmpty(target))
            {
                current = candidate;
                continue;
            }

            if (++hops > _maxLinkHops)
            {
                return null;
            }

            if (Path.IsPathRooted(target))
            {
                // sysfs's own links are all relative; an absolute one restarts from the root.
                var targetRoot = Path.GetPathRoot(target) ?? string.Empty;
                current = root;
                target = target[targetRoot.Length..];
            }

            foreach (var part in Split(target).Reverse())
            {
                pending.Push(part);
            }
        }

        return current;
    }

    private static IEnumerable<string> Split(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    private static string? ReadAttribute(string directory, string name)
    {
        try
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();
            return value.Length == 0 ? null : value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? DefaultReadLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
