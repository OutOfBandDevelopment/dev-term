using DevTerm.Core.StreamContent;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// Runs the registered-tool converter against a real Ghostscript install (<c>gswin64c.exe</c> under
/// <c>Program Files\gs\*\bin</c>). Inconclusive when it isn't installed, so it never fails a machine without it.
/// See docs/user-guide/ghostscript-conversion.md.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestClass]
[DoNotParallelize]
public sealed class RealGhostscriptConversionTests
{
    private const string _postScript = "%!PS-Adobe-3.0\n%%BoundingBox: 0 0 200 100\n%%Pages: 1\n%%EndComments\n"
        + "0 0 1 setrgbcolor 10 10 180 80 rectfill\n1 1 1 setrgbcolor /Helvetica findfont 24 scalefont setfont 20 40 moveto (dev-term) show\nshowpage\n%%EOF\n";

    private static string? FindGhostscript()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs");
        return Directory.Exists(root)
            ? Directory.GetDirectories(root).OrderDescending().Select(d => Path.Combine(d, "bin", "gswin64c.exe")).FirstOrDefault(File.Exists)
            : null;
    }

    [TestMethod]
    public async Task Auto_ConvertsAPostScriptCaptureToAPngWithRealGhostscript()
    {
        var gs = FindGhostscript();
        if (gs is null)
        {
            Assert.Inconclusive("Ghostscript (gswin64c.exe) isn't installed under Program Files gs folder.");
            return;
        }

        var directory = Directory.CreateTempSubdirectory("devterm-gs-").FullName;
        try
        {
            var data = System.Text.Encoding.ASCII.GetBytes(_postScript);
            var path = Path.Combine(directory, "capture.ps");
            File.WriteAllBytes(path, data);
            var capture = new StreamMonitorCapture(new StreamCapture(StreamContentKind.PostScript, data, DateTimeOffset.Now, StreamCaptureEnd.Complete, WasDeclared: false), "gs-test", DateTimeOffset.Now, path, null);
            var options = new StreamCaptureConverterOptions
            {
                Mode = StreamConversionMode.Auto,
                Tools = [new StreamConvertToolOptions { Name = "gs", Path = gs, Arguments = "-dBATCH -dNOPAUSE -dSAFER -sDEVICE=png16m -r{dpi} -sOutputFile={output} {input}", Formats = "ps, eps", OutputExtension = "png", Dpi = 100 }],
            };

            var result = await new StreamCaptureConverter(Options.Create(options)).ConvertAsync(capture);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(Path.Combine(directory, "capture.png"), result.OutputPath);
            var png = File.ReadAllBytes(result.OutputPath!);
            Assert.AreEqual(StreamContentKind.Png, StreamContentSniffer.Identify(png));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string? FindGhostPcl()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("DEVTERM_GPCL");
        if (fromEnvironment is not null && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        var root = Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:", "repo", "tools");
        return Directory.Exists(root)
            ? Directory.GetDirectories(root, "ghostpcl-*").OrderDescending().Select(d => Path.Combine(d, "gpcl6win64.exe")).FirstOrDefault(File.Exists)
            : null;
    }

    [TestMethod]
    public async Task Auto_ConvertsAPclCaptureToAPngWithRealGhostPcl()
    {
        var gpcl = FindGhostPcl();
        if (gpcl is null)
        {
            Assert.Inconclusive("GhostPCL (gpcl6win64.exe) wasn't found; set DEVTERM_GPCL to its path.");
            return;
        }

        var directory = Directory.CreateTempSubdirectory("devterm-gpcl-").FullName;
        try
        {
            var data = System.Text.Encoding.ASCII.GetBytes("\u001bE\u001b&l0O\u001b(s0p16.66h8.5v0s0b3T\u001b&a10c10R dev-term PCL test\r\n\u001bE");
            var path = Path.Combine(directory, "capture.pcl");
            File.WriteAllBytes(path, data);
            var capture = new StreamMonitorCapture(new StreamCapture(StreamContentKind.Pcl, data, DateTimeOffset.Now, StreamCaptureEnd.Complete, WasDeclared: false), "gpcl-test", DateTimeOffset.Now, path, null);
            var options = new StreamCaptureConverterOptions
            {
                Mode = StreamConversionMode.Auto,
                Tools = [new StreamConvertToolOptions { Name = "gpcl", Path = gpcl, Arguments = "-dNOPAUSE -sDEVICE=png16m -r{dpi} -sOutputFile={output} {input}", Formats = "pcl", OutputExtension = "png", Dpi = 100 }],
            };

            var result = await new StreamCaptureConverter(Options.Create(options)).ConvertAsync(capture);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(StreamContentKind.Png, StreamContentSniffer.Identify(File.ReadAllBytes(result.OutputPath!)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
