using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class RetentionRuleTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static string Folder(params (string Name, int AgeDays)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "devterm-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (name, age) in files)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, _now.UtcDateTime.AddDays(-age));
        }

        return dir;
    }

    [TestMethod]
    public void NoLimits_KeepsEverything()
    {
        var dir = Folder(("a.jsonl", 900), ("b.jsonl", 1));

        Assert.AreEqual(0, new RetentionRule().Apply(dir, "*", _now).Count);
        Assert.AreEqual(2, Directory.GetFiles(dir).Length);
    }

    [TestMethod]
    public void MaxAge_RemovesOnlyOlderFiles()
    {
        var dir = Folder(("old.jsonl", 40), ("new.jsonl", 5));

        var removed = new RetentionRule { MaxAgeDays = 30 }.Apply(dir, "*", _now);

        Assert.AreEqual(1, removed.Count);
        Assert.IsTrue(File.Exists(Path.Combine(dir, "new.jsonl")));
    }

    [TestMethod]
    public void MaxFiles_KeepsTheNewest()
    {
        var dir = Folder(("a.jsonl", 3), ("b.jsonl", 2), ("c.jsonl", 1));

        new RetentionRule { MaxFiles = 2 }.Apply(dir, "*", _now);

        CollectionAssert.AreEquivalent(new[] { "b.jsonl", "c.jsonl" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public void Pattern_LeavesOtherFilesAlone()
    {
        var dir = Folder(("a.jsonl", 100), ("notes.txt", 100));

        new RetentionRule { MaxAgeDays = 1 }.Apply(dir, "*.jsonl", _now);

        Assert.IsTrue(File.Exists(Path.Combine(dir, "notes.txt")));
    }

    [TestMethod]
    public void MissingFolder_IsANoOp() =>
        Assert.AreEqual(0, new RetentionRule { MaxFiles = 1 }.Apply(Path.Combine(Path.GetTempPath(), "devterm-none-" + Guid.NewGuid().ToString("N")), "*", _now).Count);

    [TestMethod]
    public void Sweeper_AppliesLogAndExportRulesToTheirOwnFolders()
    {
        var logs = Folder(("a.jsonl", 90), ("b.jsonl", 1));
        var exports = Folder(("x.png", 90), ("y.png", 1));

        var removed = RetentionSweeper.Sweep(
            new AppPreferences { LogRetention = new RetentionRule { MaxAgeDays = 30 }, ExportRetention = new RetentionRule { MaxFiles = 1 } },
            _now, logs, exports);

        Assert.AreEqual(2, removed.Count);
        Assert.IsTrue(File.Exists(Path.Combine(logs, "b.jsonl")));
        Assert.IsTrue(File.Exists(Path.Combine(exports, "y.png")));
    }
}
