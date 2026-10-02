using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// See docs/bugs/resolved/033-non-atomic-writes.md - <see cref="AtomicFile.WriteAllText"/> replaces a
/// bare <c>File.WriteAllText(path, ...)</c> (which overwrites in place, so a crash or power loss
/// mid-write leaves a truncated file at <c>path</c>) with a write to a temporary file followed by
/// an atomic <see cref="File.Move(string, string, bool)"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class AtomicFileTests
{
    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void WriteAllText_OnSuccess_WritesContentsAndLeavesNoTemporaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"devterm-atomicfile-{Guid.NewGuid():N}.json");
        try
        {
            AtomicFile.WriteAllText(path, "{\"a\":1}");

            Assert.AreEqual("{\"a\":1}", File.ReadAllText(path));
            Assert.IsFalse(File.Exists($"{path}.tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void WriteAllText_WhenTheTemporaryFileCannotBeWritten_LeavesTheExistingFileUntouched()
    {
        var path = Path.Combine(Path.GetTempPath(), $"devterm-atomicfile-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "original");
        var temporary = $"{path}.tmp";

        // Holding the temp path open with no sharing simulates the write-to-temp step failing
        // partway - the real-world equivalent of the crash/power-loss scenario in the bug report,
        // which a unit test can't otherwise trigger. The old, non-atomic code wrote straight to
        // `path`, so a failure here would have already started clobbering "original"; the fix
        // never touches `path` until the temp file is fully written and ready to move.
        try
        {
            using (new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Assert.ThrowsExactly<IOException>(() => AtomicFile.WriteAllText(path, "new"));
            }

            Assert.AreEqual("original", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
            File.Delete(temporary);
        }
    }
}
