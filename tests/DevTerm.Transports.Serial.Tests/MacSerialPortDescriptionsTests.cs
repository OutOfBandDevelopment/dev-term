using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Serial.Tests;

/// <summary>
/// Parses sample <c>ioreg -a -l -r -c IOUSBHostDevice</c> output. The sample follows ioreg's real
/// archive format (an XML plist <c>&lt;array&gt;</c> of <c>IOUSBHostDevice</c> dicts, descendants
/// under <c>IORegistryEntryChildren</c>) and real property names/values — the FTDI entry's
/// <c>idVendor</c> 1027 / <c>idProduct</c> 24577, <c>IOCalloutDevice</c>
/// <c>/dev/cu.usbserial-*</c>, <c>IODialinDevice</c> <c>/dev/tty.usbserial-*</c> and <c>IOTTYDevice</c>
/// match ioreg output posted in pololu/libusbp issue #8. It is trimmed to the properties that matter
/// (a real dump has dozens more per entry), not captured from a Mac here.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Serial)]
[TestClass]
public sealed class MacSerialPortDescriptionsTests
{
    private const string _sample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <array>
        	<dict>
        		<key>IOClass</key>
        		<string>IOUSBHostDevice</string>
        		<key>IOObjectClass</key>
        		<string>IOUSBHostDevice</string>
        		<key>IORegistryEntryName</key>
        		<string>FT232R USB UART</string>
        		<key>USB Product Name</key>
        		<string>FT232R USB UART</string>
        		<key>USB Vendor Name</key>
        		<string>FTDI</string>
        		<key>USB Serial Number</key>
        		<string>AE01EB7D</string>
        		<key>kUSBProductString</key>
        		<string>FT232R USB UART</string>
        		<key>kUSBVendorString</key>
        		<string>FTDI</string>
        		<key>kUSBSerialNumberString</key>
        		<string>AE01EB7D</string>
        		<key>idVendor</key>
        		<integer>1027</integer>
        		<key>idProduct</key>
        		<integer>24577</integer>
        		<key>locationID</key>
        		<integer>336592896</integer>
        		<key>IORegistryEntryChildren</key>
        		<array>
        			<dict>
        				<key>IOObjectClass</key>
        				<string>IOUSBHostInterface</string>
        				<key>IORegistryEntryName</key>
        				<string>FT232R USB UART</string>
        				<key>bInterfaceNumber</key>
        				<integer>0</integer>
        				<key>bInterfaceClass</key>
        				<integer>255</integer>
        				<key>idVendor</key>
        				<integer>1027</integer>
        				<key>idProduct</key>
        				<integer>24577</integer>
        				<key>IORegistryEntryChildren</key>
        				<array>
        					<dict>
        						<key>IOClass</key>
        						<string>AppleUSBFTDI</string>
        						<key>IOObjectClass</key>
        						<string>AppleUSBFTDI</string>
        						<key>IORegistryEntryChildren</key>
        						<array>
        							<dict>
        								<key>IOClass</key>
        								<string>IOSerialBSDClient</string>
        								<key>IOObjectClass</key>
        								<string>IOSerialBSDClient</string>
        								<key>IOCalloutDevice</key>
        								<string>/dev/cu.usbserial-AE01EB7D</string>
        								<key>IODialinDevice</key>
        								<string>/dev/tty.usbserial-AE01EB7D</string>
        								<key>IOSerialBSDClientType</key>
        								<string>IORS232SerialStream</string>
        								<key>IOTTYBaseName</key>
        								<string>usbserial-</string>
        								<key>IOTTYDevice</key>
        								<string>usbserial-AE01EB7D</string>
        								<key>IOTTYSuffix</key>
        								<string>AE01EB7D</string>
        							</dict>
        						</array>
        					</dict>
        				</array>
        			</dict>
        		</array>
        	</dict>
        	<dict>
        		<key>IOObjectClass</key>
        		<string>IOUSBHostDevice</string>
        		<key>USB Product Name</key>
        		<string>Pico</string>
        		<key>USB Vendor Name</key>
        		<string>Raspberry Pi</string>
        		<key>USB Serial Number</key>
        		<string>E66138935F3C2A2C</string>
        		<key>idVendor</key>
        		<integer>11914</integer>
        		<key>idProduct</key>
        		<integer>10</integer>
        		<key>IORegistryEntryChildren</key>
        		<array>
        			<dict>
        				<key>IOObjectClass</key>
        				<string>IOUSBHostInterface</string>
        				<key>bInterfaceNumber</key>
        				<integer>0</integer>
        				<key>idVendor</key>
        				<integer>11914</integer>
        				<key>idProduct</key>
        				<integer>10</integer>
        				<key>IORegistryEntryChildren</key>
        				<array>
        					<dict>
        						<key>IOObjectClass</key>
        						<string>AppleUSBACMControl</string>
        					</dict>
        				</array>
        			</dict>
        			<dict>
        				<key>IOObjectClass</key>
        				<string>IOUSBHostInterface</string>
        				<key>bInterfaceNumber</key>
        				<integer>1</integer>
        				<key>idVendor</key>
        				<integer>11914</integer>
        				<key>idProduct</key>
        				<integer>10</integer>
        				<key>IORegistryEntryChildren</key>
        				<array>
        					<dict>
        						<key>IOObjectClass</key>
        						<string>AppleUSBACMData</string>
        						<key>IORegistryEntryChildren</key>
        						<array>
        							<dict>
        								<key>IOObjectClass</key>
        								<string>IOSerialBSDClient</string>
        								<key>IOCalloutDevice</key>
        								<string>/dev/cu.usbmodem14101</string>
        								<key>IODialinDevice</key>
        								<string>/dev/tty.usbmodem14101</string>
        								<key>IOTTYBaseName</key>
        								<string>usbmodem</string>
        								<key>IOTTYDevice</key>
        								<string>usbmodem14101</string>
        							</dict>
        						</array>
        					</dict>
        				</array>
        			</dict>
        		</array>
        	</dict>
        	<dict>
        		<key>IOObjectClass</key>
        		<string>IOUSBHostDevice</string>
        		<key>USB Product Name</key>
        		<string>USB Receiver</string>
        		<key>USB Vendor Name</key>
        		<string>Logitech</string>
        		<key>idVendor</key>
        		<integer>1133</integer>
        		<key>idProduct</key>
        		<integer>50475</integer>
        		<key>IORegistryEntryChildren</key>
        		<array>
        			<dict>
        				<key>IOObjectClass</key>
        				<string>IOUSBHostInterface</string>
        				<key>bInterfaceClass</key>
        				<integer>3</integer>
        			</dict>
        		</array>
        	</dict>
        </array>
        </plist>
        """;
    private static readonly string[] _expected = ["-a", "-l", "-r", "-c", "IOUSBHostDevice"];

    [TestMethod]
    public void Parse_DescribesBothBsdPathsOfAnFtdiAdapter_FromItsUsbDevice_NotItsInterface()
    {
        var descriptions = MacSerialPortDescriptions.Parse(_sample);

        Assert.AreEqual("FTDI FT232R USB UART (0403:6001, serial AE01EB7D)", descriptions["/dev/cu.usbserial-AE01EB7D"]);
        Assert.AreEqual("FTDI FT232R USB UART (0403:6001, serial AE01EB7D)", descriptions["/dev/tty.usbserial-AE01EB7D"]);
    }

    [TestMethod]
    public void Parse_DescribesACdcAcmDevice_OnItsDataInterface()
    {
        var descriptions = MacSerialPortDescriptions.Parse(_sample);

        Assert.AreEqual("Raspberry Pi Pico (2e8a:000a, serial E66138935F3C2A2C)", descriptions["/dev/cu.usbmodem14101"]);
        Assert.AreEqual("Raspberry Pi Pico (2e8a:000a, serial E66138935F3C2A2C)", descriptions["/dev/tty.usbmodem14101"]);
    }

    [TestMethod]
    public void Parse_OnlyListsSerialPorts_NotEveryUsbDevice() =>
        Assert.HasCount(4, MacSerialPortDescriptions.Parse(_sample));

    [TestMethod]
    public void Parse_FallsBackToTheKUsbStringKeys()
    {
        var plist = Plist("""
            <dict>
            	<key>IOObjectClass</key><string>IOUSBHostDevice</string>
            	<key>kUSBVendorString</key><string>Silicon Labs</string>
            	<key>kUSBProductString</key><string>CP2102 USB to UART Bridge Controller</string>
            	<key>idVendor</key><integer>4292</integer>
            	<key>idProduct</key><integer>60000</integer>
            	<key>IORegistryEntryChildren</key>
            	<array>
            		<dict>
            			<key>IOObjectClass</key><string>IOSerialBSDClient</string>
            			<key>IOCalloutDevice</key><string>/dev/cu.SLAB_USBtoUART</string>
            		</dict>
            	</array>
            </dict>
            """);

        Assert.AreEqual(
            "Silicon Labs CP2102 USB to UART Bridge Controller (10c4:ea60)",
            MacSerialPortDescriptions.Parse(plist)["/dev/cu.SLAB_USBtoUART"]);
    }

    [TestMethod]
    public void Parse_SerialClientWithNoUsbDeviceAbove_GetsNoDescription()
    {
        var plist = Plist("""
            <dict>
            	<key>IOObjectClass</key><string>IOSerialBSDClient</string>
            	<key>IOCalloutDevice</key><string>/dev/cu.Bluetooth-Incoming-Port</string>
            	<key>IODialinDevice</key><string>/dev/tty.Bluetooth-Incoming-Port</string>
            </dict>
            """);

        Assert.IsEmpty(MacSerialPortDescriptions.Parse(plist));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not xml at all")]
    [DataRow("<plist version=\"1.0\"><array></array></plist>")]
    [DataRow("<plist version=\"1.0\"><array><dict><key>IOObjectClass</key>")]
    public void Parse_EmptyOrMalformedOutput_ReturnsEmptyRatherThanThrowing(string output) =>
        Assert.IsEmpty(MacSerialPortDescriptions.Parse(output));

    [TestMethod]
    public void IoregInvocation_IsTheArchiveOfEveryUsbDeviceSubtree() =>
        CollectionAssert.AreEqual(_expected, MacSerialPortDescriptions.IoregArguments.ToArray());

    private static string Plist(string rootDict) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <array>
        {rootDict}
        </array>
        </plist>
        """;
}
