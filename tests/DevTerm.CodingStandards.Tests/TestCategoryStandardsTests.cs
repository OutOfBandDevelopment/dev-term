using System.Reflection;
using DevTerm.Test.Utilities;

namespace DevTerm.CodingStandards.Tests;

/// <summary>
/// Enforces the "every test carries a real <c>[TestCategory]</c>" standard from
/// docs/coding-standards.md — a class missing one, or carrying a typo'd category name, doesn't fail
/// a build (MSTest treats it as just not matching any <c>--filter TestCategory=...</c>), so nothing
/// else would ever catch it. This project exists purely to reflect over every other test assembly
/// (see the csproj's <c>ProjectReference</c>s) and check that directly, the same "declared AND
/// enforced, not just written down" discipline the rest of docs/coding-standards.md already follows.
///
/// Every existing test class in this repo puts <c>[TestCategory]</c> once, at the class level, never
/// per-method (confirmed directly by inspection before writing this) — but what MSTest actually
/// resolves per test is the union of class-level and method-level categories, so
/// <see cref="EveryTestMethod_HasAnEffectiveRecognizedTestCategory"/> checks that resolved set
/// directly rather than just assuming the class-level convention holds everywhere forever.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class TestCategoryStandardsTests
{
    /// <summary>The only values docs/coding-standards.md recognizes — add a new one here in the same change that starts using it.</summary>
    private static readonly string[] _knownCategories = [TestCategories.Unit, TestCategories.Integration];

    private static readonly Assembly[] _testAssemblies =
    [
        typeof(DevTerm.Configuration.Tests.CliOptionsBindingTests).Assembly,
        typeof(DevTerm.Console.Tests.ConfigureModeTests).Assembly,
        typeof(DevTerm.Core.Tests.Presenters.PipelineTests).Assembly,
        typeof(DevTerm.DeviceManifests.Tests.DeviceManifestTests).Assembly,
        typeof(DevTerm.Presenters.Text.Tests.AsciiPresenterTests).Assembly,
        typeof(DevTerm.Transports.Hid.Tests.HidTransportTests).Assembly,
        typeof(DevTerm.Transports.Serial.Tests.SerialTransportTests).Assembly,
        typeof(DevTerm.Transports.Tcp.Tests.TcpTransportOptionsValidatorTests).Assembly,
        typeof(DevTerm.UiDefinitions.Tests.UiDefinitionSerializerTests).Assembly,
        typeof(DevTerm.Wpf.Tests.DeviceProfilesWindowTests).Assembly,
    ];

    private static IEnumerable<Type> TestClasses() =>
        _testAssemblies.SelectMany(a => a.GetTypes()).Where(t => t.GetCustomAttribute<TestClassAttribute>() is not null);

    [TestMethod]
    public void EveryTestClass_DeclaresARecognizedTestCategory()
    {
        var problems = new List<string>();
        foreach (var type in TestClasses())
        {
            var categories = type.GetCustomAttributes<TestCategoryAttribute>().SelectMany(a => a.TestCategories).ToList();
            if (categories.Count == 0)
            {
                problems.Add($"{type.FullName}: no [TestCategory] at all");
                continue;
            }

            foreach (var category in categories.Where(c => !_knownCategories.Contains(c, StringComparer.Ordinal)))
            {
                problems.Add($"{type.FullName}: unrecognized category '{category}'");
            }
        }

        Assert.IsEmpty(
            problems,
            $"Every [TestClass] needs a [TestCategory(...)] from {{{string.Join(", ", _knownCategories)}}} (see docs/coding-standards.md). Problems: {string.Join("; ", problems)}");
    }

    [TestMethod]
    public void EveryTestMethod_HasAnEffectiveRecognizedTestCategory()
    {
        var problems = new List<string>();
        foreach (var type in TestClasses())
        {
            var classCategories = type.GetCustomAttributes<TestCategoryAttribute>().SelectMany(a => a.TestCategories).ToList();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<TestMethodAttribute>() is null)
                {
                    continue;
                }

                var methodCategories = method.GetCustomAttributes<TestCategoryAttribute>().SelectMany(a => a.TestCategories);
                var effective = classCategories.Concat(methodCategories).ToList();
                if (effective.Count == 0)
                {
                    problems.Add($"{type.FullName}.{method.Name}: no effective [TestCategory]");
                    continue;
                }

                foreach (var category in effective.Where(c => !_knownCategories.Contains(c, StringComparer.Ordinal)))
                {
                    problems.Add($"{type.FullName}.{method.Name}: unrecognized category '{category}'");
                }
            }
        }

        Assert.IsEmpty(
            problems,
            $"Every [TestMethod] needs an effective (class- or method-level) [TestCategory(...)] from {{{string.Join(", ", _knownCategories)}}} (see docs/coding-standards.md). Problems: {string.Join("; ", problems)}");
    }
}
