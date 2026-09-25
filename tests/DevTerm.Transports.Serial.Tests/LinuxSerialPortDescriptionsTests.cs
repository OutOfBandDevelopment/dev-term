using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Serial.Tests;

/// <summary>
/// Drives <see cref="LinuxSerialPortDescriptions"/> against a fake sysfs tree built in a temp
/// directory, laid out like a real one (<c>/sys/class/tty/&lt;name&gt;</c> → a link into
/// <c>/sys/devices/...</c>, whose <c>device</c> is another relative link to the usb-serial port or
/// the USB interface). Two unavoidable deviations from the real thing: the symlinks are supplied as
/// data (an injected <c>readlink</c>), since creating real symlinks on Windows needs a privilege a
/// test can't count on; and sysfs's <c>:</c> separators (<c>pci0000:00</c>, <c>1-2:1.0</c>) are
/// written as <c>_</c>, since NTFS doesn't allow <c>:</c> in a file name.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Serial)]
[TestClass]
public sealed class LinuxSerialPortDescriptionsTests
{
    private const string _hostController = "devices/pci0000_00/0000_00_14.0/usb1";

    private string _root = null!;
    private Dictionary<string, string> _links = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "devterm-sysfs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The xHCI root hub is itself a USB device (Linux Foundation 1d6b:0002): the walk has to
        // stop at the nearest idVendor, never climb to this one.
        WriteAttributes(_hostController, ("idVendor", "1d6b"), ("idProduct", "0002"), ("manufacturer", "Linux 6.8.0 xhci-hcd"), ("product", "xHCI Host Controller"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public void UsbSerialAdapter_ttyUSB_IsDescribedFromTheUsbDeviceTwoLevelsAboveThePort()
    {
        AddFtdiTtyUsb0();

        Assert.AreEqual("FTDI FT232R USB UART (0403:6001, serial A50285BI)", Create().Describe("ttyUSB0"));
    }

    [TestMethod]
    public void CdcAcmDevice_ttyACM_IsDescribedFromTheUsbDeviceAboveTheInterface()
    {
        AddPicoTtyAcm0();

        Assert.AreEqual("Raspberry Pi Pico (2e8a:000a, serial E66138935F3C2A2C)", Create().Describe("ttyACM0"));
    }

    [TestMethod]
    public void AdapterBehindAHub_IsDescribedFromItsOwnDevice_NotTheHub()
    {
        // 1-4 is a hub (with its own idVendor), 1-4.2 the CP2102 plugged into it.
        WriteAttributes($"{_hostController}/1-4", ("idVendor", "05e3"), ("idProduct", "0610"), ("product", "USB2.0 Hub"));
        var usbDevice = $"{_hostController}/1-4/1-4.2";
        WriteAttributes(usbDevice, ("idVendor", "10c4"), ("idProduct", "ea60"), ("manufacturer", "Silicon Labs"), ("product", "CP2102 USB to UART Bridge Controller"), ("serial", "0001"));
        AddTty("ttyUSB2", $"{usbDevice}/1-4.2_1.0/ttyUSB2/tty/ttyUSB2", "../../../ttyUSB2");

        Assert.AreEqual("Silicon Labs CP2102 USB to UART Bridge Controller (10c4:ea60, serial 0001)", Create().Describe("ttyUSB2"));
    }

    [TestMethod]
    public void UsbDeviceWithNoStringDescriptors_FallsBackToItsIds()
    {
        // A CH340 clone that reports no manufacturer/product/serial strings at all.
        var usbDevice = $"{_hostController}/1-5";
        WriteAttributes(usbDevice, ("idVendor", "1a86"), ("idProduct", "7523"));
        AddTty("ttyUSB1", $"{usbDevice}/1-5_1.0/ttyUSB1/tty/ttyUSB1", "../../../ttyUSB1");

        Assert.AreEqual("USB device 1a86:7523", Create().Describe("ttyUSB1"));
    }

    [TestMethod]
    public void OnboardUart_ttyS_HasNoUsbAncestor_AndGetsNoDescription()
    {
        AddTty("ttyS0", "devices/pnp0/00_04/tty/ttyS0", "../../../00_04");

        Assert.IsNull(Create().Describe("ttyS0"));
    }

    [TestMethod]
    public void VirtualTtyWithNoDeviceLink_GetsNoDescription()
    {
        Directory.CreateDirectory(Path.Combine(_root, "devices", "virtual", "tty", "tty"));
        Directory.CreateDirectory(Path.Combine(_root, "class", "tty", "tty"));
        _links[Path.Combine(_root, "class", "tty", "tty")] = "../../devices/virtual/tty/tty";

        Assert.IsNull(Create().Describe("tty"));
    }

    [TestMethod]
    public void UnknownTty_GetsNoDescription() =>
        Assert.IsNull(Create().Describe("ttyUSB9"));

    [TestMethod]
    public void SymlinkLoop_GivesUpInsteadOfHanging()
    {
        Directory.CreateDirectory(Path.Combine(_root, "class", "tty", "ttyLOOP"));
        _links[Path.Combine(_root, "class", "tty", "ttyLOOP")] = "ttyLOOP";

        Assert.IsNull(Create().Describe("ttyLOOP"));
    }

    [TestMethod]
    public void ReadAll_KeysByTheDevPathGetPortNamesReports_AndSkipsPortsWithoutADescription()
    {
        AddFtdiTtyUsb0();
        AddPicoTtyAcm0();
        AddTty("ttyS0", "devices/pnp0/00_04/tty/ttyS0", "../../../00_04");

        var descriptions = Create().ReadAll();

        Assert.HasCount(2, descriptions);
        Assert.AreEqual("FTDI FT232R USB UART (0403:6001, serial A50285BI)", descriptions["/dev/ttyUSB0"]);
        Assert.AreEqual("Raspberry Pi Pico (2e8a:000a, serial E66138935F3C2A2C)", descriptions["/dev/ttyACM0"]);
        Assert.IsFalse(descriptions.ContainsKey("/dev/ttyS0"));
    }

    [TestMethod]
    public void ReadAll_WhenSysfsIsMissing_ReturnsEmpty()
    {
        var missing = new LinuxSerialPortDescriptions(Path.Combine(_root, "nope"), _ => null);

        Assert.IsEmpty(missing.ReadAll());
    }

    [TestMethod]
    public void RealPath_ResolvesARelativeLinkAgainstTheDirectoryTheLinkActuallyLivesIn()
    {
        AddFtdiTtyUsb0();

        var resolved = Create().RealPath(Path.Combine(_root, "class", "tty", "ttyUSB0", "device"));

        Assert.AreEqual(Full($"{_hostController}/1-2/1-2_1.0/ttyUSB0"), resolved);
    }

    private void AddFtdiTtyUsb0()
    {
        // /sys/devices/pci0000:00/0000:00:14.0/usb1/1-2 (USB device) / 1-2:1.0 (interface) /
        // ttyUSB0 (usb-serial port, what /sys/class/tty/ttyUSB0/device points at) / tty/ttyUSB0.
        var usbDevice = $"{_hostController}/1-2";
        WriteAttributes(usbDevice, ("idVendor", "0403"), ("idProduct", "6001"), ("manufacturer", "FTDI"), ("product", "FT232R USB UART"), ("serial", "A50285BI"), ("busnum", "1"), ("devnum", "5"));
        WriteAttributes($"{usbDevice}/1-2_1.0", ("bInterfaceNumber", "00"), ("bInterfaceClass", "ff"), ("interface", "FT232R USB UART"));
        WriteAttributes($"{usbDevice}/1-2_1.0/ttyUSB0", ("port_number", "0"));
        AddTty("ttyUSB0", $"{usbDevice}/1-2_1.0/ttyUSB0/tty/ttyUSB0", "../../../ttyUSB0");
    }

    private void AddPicoTtyAcm0()
    {
        // cdc_acm: /sys/class/tty/ttyACM0/device points straight at the interface (1-3:1.0).
        var usbDevice = $"{_hostController}/1-3";
        WriteAttributes(usbDevice, ("idVendor", "2e8a"), ("idProduct", "000a"), ("manufacturer", "Raspberry Pi"), ("product", "Pico"), ("serial", "E66138935F3C2A2C"));
        WriteAttributes($"{usbDevice}/1-3_1.0", ("bInterfaceNumber", "00"), ("bInterfaceClass", "02"));
        AddTty("ttyACM0", $"{usbDevice}/1-3_1.0/tty/ttyACM0", "../../../1-3_1.0");
    }

    /// <summary>
    /// <c>/sys/class/tty/&lt;name&gt;</c> → <c>../../&lt;ttyDirectory&gt;</c>, and that directory's
    /// <c>device</c> → <paramref name="deviceLink"/> (relative, as the kernel writes it).
    /// </summary>
    private void AddTty(string name, string ttyDirectory, string deviceLink)
    {
        Directory.CreateDirectory(Full(ttyDirectory));
        Directory.CreateDirectory(Path.Combine(_root, "class", "tty", name));
        _links[Path.Combine(_root, "class", "tty", name)] = "../../" + ttyDirectory;
        _links[Path.Combine(Full(ttyDirectory), "device")] = deviceLink;
    }

    private void WriteAttributes(string directory, params (string Name, string Value)[] attributes)
    {
        var full = Full(directory);
        Directory.CreateDirectory(full);
        foreach (var (name, value) in attributes)
        {
            // sysfs attribute files end in a newline.
            File.WriteAllText(Path.Combine(full, name), value + "\n");
        }
    }

    private string Full(string relative) =>
        Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private LinuxSerialPortDescriptions Create() =>
        new(_root, path => _links.TryGetValue(Path.GetFullPath(path), out var target) ? target : null);
}
