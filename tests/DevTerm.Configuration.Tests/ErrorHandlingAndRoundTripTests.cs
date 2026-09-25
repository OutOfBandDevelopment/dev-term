using System.IO.Ports;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// Covers the shared (front-end-agnostic) pieces of "never crash, reject invalid input, report
/// errors": <see cref="TypedInput"/>, <see cref="ConnectionErrorMessages.ForDisconnect"/>, the
/// <see cref="SendHistory"/> consecutive-duplicate rule, and the Connection Editor round-tripping a
/// loaded profile unchanged (which is also what lets the window title find the profile's name).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ErrorHandlingAndRoundTripTests
{
    [TestMethod]
    public void TypedInput_NonHexTextWithTheHexParser_IsRejectedWithAMessageNotAnException()
    {
        var ok = TypedInput.TryEncode(new HexPresenter(), "hex", "OUTPut?", LineEnding.Lf, out var payload, out var error);

        Assert.IsFalse(ok);
        Assert.IsEmpty(payload);
        StringAssert.Contains(error, "OUTPut?");
        StringAssert.Contains(error, "hex");
    }

    [TestMethod]
    public void TypedInput_OutOfRangeDecimalByte_IsRejected()
    {
        var ok = TypedInput.TryEncode(new DecimalPresenter(), "decimal", "300", LineEnding.None, out _, out var error);

        Assert.IsFalse(ok);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void TypedInput_ValidLine_EncodesAndAppendsTheLineEnding()
    {
        var ascii = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));

        var ok = TypedInput.TryEncode(ascii, "ascii", "*IDN?", LineEnding.Lf, out var payload, out var error);

        Assert.IsTrue(ok);
        Assert.IsNull(error);
        Assert.AreSequenceEqual("*IDN?\n"u8.ToArray(), payload);
    }

    [TestMethod]
    public void ForDisconnect_TimeoutOnSerial_MentionsFlowControl_ButNotOnOtherTransports()
    {
        var timeout = new TimeoutException("no reply within 1000 ms");

        StringAssert.Contains(ConnectionErrorMessages.ForDisconnect("serial", timeout), "CTS");
        var usbtmc = ConnectionErrorMessages.ForDisconnect("usbtmc", timeout);
        Assert.DoesNotContain("CTS", usbtmc);
        StringAssert.Contains(usbtmc, "no reply within 1000 ms");
    }

    [TestMethod]
    public void ForDisconnect_NoError_SaysTheDeviceClosedIt() =>
        StringAssert.Contains(ConnectionErrorMessages.ForDisconnect("tcp", null), "closed the connection");

    [TestMethod]
    public void SendHistory_SameLineTwiceInARow_IsRecordedOnce()
    {
        var history = new SendHistory();

        history.Add("*IDN?");
        history.Add("*IDN?");
        history.Add("MEAS?");
        history.Add("*IDN?");

        Assert.AreSequenceEqual(["*IDN?", "MEAS?", "*IDN?"], history.Items);
    }

    [TestMethod]
    public void Editor_LoadThenConnect_KeepsSettingsTheFormDoesNotShow_AndTheTitleFindsTheProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-roundtrip-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ConnectionProfileStore(directory);
            var saved = new CliOptions
            {
                Transport = "serial",
                Port = "COM7",
                Baud = 115200,
                Parity = Parity.Even,
                Presenter = ["hex", "ascii"],
                Parser = "ascii",
                LineEnding = LineEnding.CrLf,
                Dtr = false,
                Rts = false,
                ReadTimeoutMs = 2500,
                WriteTimeoutMs = 750,
                AsciiMaxLineLength = 512,
            };
            store.Save("bench meter", saved);

            var vm = new ConnectionEditorViewModel(store, new CliOptions { Transport = "tcp" })
            {
                SelectedProfileName = "bench meter",
            };
            vm.LoadCommand.Execute(null);
            vm.ConnectCommand.Execute(null);

            var result = vm.Result;
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Dtr);
            Assert.IsFalse(result.Rts);
            Assert.AreEqual(2500, result.ReadTimeoutMs);
            Assert.AreEqual(750, result.WriteTimeoutMs);
            Assert.AreEqual(512, result.AsciiMaxLineLength);
            Assert.AreSequenceEqual(["hex", "ascii"], result.EffectivePresenters, "The saved presenter order is kept.");

            Assert.AreEqual("bench meter", store.FindName(result));
            StringAssert.Contains(ConnectionDescription.WindowTitle(result, result.EffectiveParser, store), "bench meter");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
