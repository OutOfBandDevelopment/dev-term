using DevTerm.Test.Utilities;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// <see cref="Expression"/>'s string values and text functions (<c>matches</c>, <c>contains</c>, <c>has</c>, ...), the
/// <c>?:</c> ternary and <c>!</c> — see docs/design/features/expression-picker-paths-and-cel.md.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ExpressionTextTests
{
    private static readonly Dictionary<string, double> _none = [];

    private static double Eval(string text, Dictionary<string, string>? strings = null, Dictionary<string, double>? numbers = null) =>
        Expression.Parse(text).Evaluate(numbers ?? _none, strings);

    [TestMethod]
    [DataRow(@"matches({model}, '^KA(\d+)P')", "KA3005P", 1.0)]
    [DataRow(@"matches({model}, '^KA(\d+)P')", "RIGOL", 0.0)]
    [DataRow("matches({model}, \"^KA\")", "KA3005P", 1.0)]
    [DataRow("contains({model}, '300')", "KA3005P", 1.0)]
    [DataRow("startsWith({model}, 'KA')", "KA3005P", 1.0)]
    [DataRow("endsWith({model}, 'P')", "KA3005P", 1.0)]
    [DataRow("endsWith({model}, 'X')", "KA3005P", 0.0)]
    [DataRow("size({model})", "KA3005P", 7.0)]
    [DataRow("{model} == 'KA3005P'", "KA3005P", 1.0)]
    [DataRow("{model} != 'KA3005P'", "KA3005P", 0.0)]
    [DataRow("number({reading}) * 2", "21.5", 43.0)]
    public void TextFunctions_AndComparisons(string expression, string value, double expected)
    {
        var strings = new Dictionary<string, string> { ["model"] = value, ["reading"] = value };
        Assert.AreEqual(expected, Eval(expression, strings), 1e-9);
    }

    [TestMethod]
    public void Has_ReportsWhetherAnIdIsPublished()
    {
        Assert.AreEqual(1.0, Eval("has({a})", numbers: new() { ["a"] = 1 }));
        Assert.AreEqual(1.0, Eval("has({a})", new() { ["a"] = "x" }));
        Assert.AreEqual(0.0, Eval("has({a})"));
    }

    [TestMethod]
    public void Ternary_AndNot()
    {
        Assert.AreEqual(10.0, Eval("{x} > 1 ? 10 : 20", numbers: new() { ["x"] = 2 }));
        Assert.AreEqual(20.0, Eval("{x} > 1 ? 10 : 20", numbers: new() { ["x"] = 0 }));
        Assert.AreEqual(1.0, Eval("!has({x})"));
        Assert.AreEqual(5.0, Eval("1 ? 2 ? 5 : 6 : 7"));
    }

    [TestMethod]
    public void StringConcatenation_AndEscapes()
    {
        Assert.AreEqual(1.0, Eval("'a' + 'b' == 'ab'"));
        Assert.AreEqual(1.0, Eval(@"size('a\nb') == 3"));
        Assert.AreEqual(1.0, Eval(@"size('it\'s') == 4"));
    }

    [TestMethod]
    public void NonNumericTextResult_IsNaN() => Assert.IsTrue(double.IsNaN(Eval("'abc'")));

    [TestMethod]
    public void BadLiteralRegex_IsAParseError() =>
        Assert.IsFalse(Expression.TryParse("matches({a}, '(')", out _, out var error) || error is null);

    [TestMethod]
    public void BadComputedRegex_EvaluatesToFalse_WithoutThrowing() =>
        Assert.AreEqual(0.0, Eval("matches({a}, {p})", new() { ["a"] = "x", ["p"] = "(" }));

    [TestMethod]
    [DataRow("'abc")]
    [DataRow("has(1)")]
    [DataRow("matches({a})")]
    [DataRow("size()")]
    [DataRow("1 ? 2")]
    public void Malformed_IsReported(string text) =>
        Assert.IsFalse(Expression.TryParse(text, out _, out _));

    [TestMethod]
    public void NumericOnlyExpressions_StillEvaluateAsBefore() =>
        Assert.AreEqual(3.0, Expression.Parse("{a} + 1").Evaluate(new Dictionary<string, double> { ["a"] = 2 }));
}
