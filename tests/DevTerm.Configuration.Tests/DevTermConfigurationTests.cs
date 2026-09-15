using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// Verifies the precedence a saved "profile" relies on: a settings file provides defaults,
/// environment variables override the file, and command-line arguments override everything.
/// Exercises the real Microsoft.Extensions.Configuration extensions directly rather than
/// re-deriving the behavior, since that's the actual mechanism <see cref="DevTermConfiguration"/>
/// composes.
/// </summary>
// Environment variables are process-global; run these in isolation so they can't race with
// (or be raced by) any other test that also mutates them via Environment.SetEnvironmentVariable.
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class DevTermConfigurationTests
{
    [TestMethod]
    public void CommandLine_OverridesJsonFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """{"Port":"COM1","Baud":9600,"Presenter":"hex"}""");

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(tempFile, optional: false)
                .AddCommandLine(["--baud", "4800"])
                .Build();

            var options = new CliOptions();
            configuration.Bind(options);

            Assert.AreEqual("COM1", options.Port, "Untouched settings should still come from the file.");
            Assert.AreEqual(4800, options.Baud, "Command line should override the file.");
            Assert.AreEqual("hex", options.Presenter);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void EnvironmentVariables_OverrideJsonFile_ButLoseToCommandLine()
    {
        var tempFile = Path.GetTempFileName();
        const string variable = DevTermConfiguration.EnvironmentVariablePrefix + "BAUD";
        try
        {
            File.WriteAllText(tempFile, """{"Port":"COM1","Baud":9600}""");
            Environment.SetEnvironmentVariable(variable, "19200");

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(tempFile, optional: false)
                .AddEnvironmentVariables(DevTermConfiguration.EnvironmentVariablePrefix)
                .AddCommandLine(["--port", "COM9"])
                .Build();

            var options = new CliOptions();
            configuration.Bind(options);

            Assert.AreEqual("COM9", options.Port, "Command line should override both the file and the environment.");
            Assert.AreEqual(19200, options.Baud, "The environment variable should override the file.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void ToProfileJson_SerialTransport_BindsBackToTheSameConnectionFields()
    {
        var options = new CliOptions
        {
            Transport = "serial",
            Port = "COM3",
            Baud = 4800,
            Handshake = System.IO.Ports.Handshake.RequestToSend,
            Presenter = "ascii",
            LineEnding = LineEnding.Cr,
            ManifestName = "tek-2230",
            Tui = false,
            ListPorts = true,
        };

        var json = DevTermConfiguration.ToProfileJson(options);

        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))).Build();
        var roundTripped = new CliOptions();
        configuration.Bind(roundTripped);

        Assert.AreEqual("COM3", roundTripped.Port);
        Assert.AreEqual(4800, roundTripped.Baud);
        Assert.AreEqual(System.IO.Ports.Handshake.RequestToSend, roundTripped.Handshake);
        Assert.AreEqual("ascii", roundTripped.Presenter);
        Assert.AreEqual(LineEnding.Cr, roundTripped.LineEnding);
        Assert.AreEqual("tek-2230", roundTripped.ManifestName);
        Assert.IsTrue(roundTripped.Tui, "One-shot/mode flags shouldn't be projected into the saved profile.");
        Assert.IsFalse(roundTripped.ListPorts);
    }

    [TestMethod]
    public void SaveLocalProfile_WritesAppsettingsLocalJsonNextToTheApp()
    {
        var path = Path.Combine(AppContext.BaseDirectory, DevTermConfiguration.LocalSettingsFileName);
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            DevTermConfiguration.SaveLocalProfile(new CliOptions { Transport = "serial", Port = "COM7", Baud = 19200 });

            var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
            var options = new CliOptions();
            configuration.Bind(options);

            Assert.AreEqual("COM7", options.Port);
            Assert.AreEqual(19200, options.Baud);
        }
        finally
        {
            if (original is null)
            {
                File.Delete(path);
            }
            else
            {
                File.WriteAllText(path, original);
            }
        }
    }

    [TestMethod]
    public void EnvironmentVariables_WithoutThePrefix_AreIgnored()
    {
        const string unprefixed = "BAUD";
        try
        {
            Environment.SetEnvironmentVariable(unprefixed, "31250");

            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables(DevTermConfiguration.EnvironmentVariablePrefix)
                .Build();

            var options = new CliOptions();
            configuration.Bind(options);

            Assert.AreEqual(9600, options.Baud, "An env var without the DEVTERM_ prefix must not bind.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(unprefixed, null);
        }
    }
}
