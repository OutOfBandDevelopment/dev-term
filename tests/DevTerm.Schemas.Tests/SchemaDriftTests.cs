using System.Text.Json.Nodes;
using DevTerm.Test.Utilities;

namespace DevTerm.Schemas.Tests;

[TestClass]
[TestCategory(TestCategories.Unit)]
public class SchemaDriftTests
{
    private static string SchemasFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevTerm.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("DevTerm.slnx not found."), "schemas");
    }

    [TestMethod]
    public void CommittedSchemas_MatchWhatTheModelGenerates()
    {
        foreach (var (name, expected) in SchemaGenerator.Generate())
        {
            var path = Path.Combine(SchemasFolder(), name);
            Assert.IsTrue(File.Exists(path), $"{name} is missing; build DevTerm.Schemas (Debug) to regenerate it.");
            Assert.AreEqual(expected, File.ReadAllText(path).ReplaceLineEndings("\n"), $"{name} is stale; build DevTerm.Schemas (Debug) and commit the result.");
        }
    }

    [TestMethod]
    public void UiDefinitionSchema_DescribesEveryControlKindWithADiscriminator()
    {
        var schema = JsonNode.Parse(SchemaGenerator.Generate()["ui-definition.schema.json"])!;
        var kinds = schema["properties"]!["Sections"]!["items"]!["properties"]!["Controls"]!["items"]!["anyOf"]!.AsArray()
            .Select(branch => branch!["properties"]!["kind"]!["const"]!.GetValue<string>())
            .ToList();
        CollectionAssert.IsSubsetOf(new[] { "button", "toggle", "slider", "numeric", "choice", "textField", "indicator" }, kinds);
    }

    /// <summary>The connection's own password is never a profile field; only the Routing section's broker password is (plain text by decision, 2026-10-03).</summary>
    [TestMethod]
    public void ConnectionProfileSchema_HasNoTopLevelPassword()
    {
        var schema = JsonNode.Parse(SchemaGenerator.Generate()["connection-profile.schema.json"])!;
        Assert.IsNull(schema["properties"]!["Password"]);
    }
}
