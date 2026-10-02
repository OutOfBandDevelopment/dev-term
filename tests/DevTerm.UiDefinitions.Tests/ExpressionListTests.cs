using DevTerm.Test.Utilities;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// <see cref="Expression"/>'s lists (<c>[1, 2]</c>, <c>x[i]</c>, <c>in</c>, <c>split</c>/<c>join</c>), bare and dotted
/// identifiers, and <c>true</c>/<c>false</c> — see docs/design/proposals/expression-picker-paths-and-cel.md.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ExpressionListTests
{
    private static readonly Dictionary<string, double> _none = [];

    private static double Number(string text, Dictionary<string, string>? strings = null, Dictionary<string, double>? numbers = null) =>
        Expression.Parse(text).Evaluate(numbers ?? _none, strings);

    private static string Text(string text, Dictionary<string, string>? strings = null, Dictionary<string, double>? numbers = null) =>
        Expression.Parse(text).EvaluateToText(numbers ?? _none, strings);

    [TestMethod]
    [DataRow("[10, 20, 30][1]", 20.0)]
    [DataRow("[10, 20, 30][0] + [10, 20, 30][2]", 40.0)]
    [DataRow("size([1, 2, 3])", 3.0)]
    [DataRow("size([])", 0.0)]
    [DataRow("2 in [1, 2, 3]", 1.0)]
    [DataRow("5 in [1, 2, 3]", 0.0)]
    [DataRow("'b' in ['a', 'b']", 1.0)]
    [DataRow("'ell' in 'hello'", 1.0)]
    [DataRow("[1, 2] == [1, 2]", 1.0)]
    [DataRow("[1, 2] != [1, 3]", 1.0)]
    [DataRow("size([1] + [2, 3])", 3.0)]
    [DataRow("contains(['a', 'b'], 'b')", 1.0)]
    [DataRow("[1, 2, 3][7]", 0.0)]
    [DataRow("[1, 2, 3][-1]", 0.0)]
    [DataRow("![]", 1.0)]
    public void Lists_IndexMembershipAndSize(string expression, double expected)
    {
        Assert.AreEqual(expected, Number(expression), 1e-9);
    }

    [TestMethod]
    public void SplitAndJoin_RoundTripPublishedText()
    {
        var strings = new Dictionary<string, string> { ["frame"] = "12.5,3.1,7" };

        Assert.AreEqual(3.0, Number("size(split({frame}, ','))", strings));
        Assert.AreEqual(3.1, Number("split({frame}, ',')[1]", strings), 1e-9);
        Assert.AreEqual("12.5;3.1;7", Text("join(split({frame}, ','), ';')", strings));
        Assert.AreEqual("7", Text("split(frame, ',')[2]", strings));
    }

    [TestMethod]
    public void Index_OnAString_ReadsACharacter()
    {
        Assert.AreEqual("e", Text("'hello'[1]"));
        Assert.AreEqual("0", Text("'hi'[9]"));
    }

    [TestMethod]
    public void ListResult_IsShownAsBracketedText_AndNotANumber()
    {
        Assert.AreEqual("[1,2,3]", Text("[1, 2, 3]"));
        Assert.IsTrue(double.IsNaN(Number("[1, 2] * 2")));
    }

    [TestMethod]
    public void BareIdentifiers_AreTheSameAsBracedIds()
    {
        var numbers = new Dictionary<string, double> { ["volts"] = 12, ["gps.sats"] = 7 };

        Assert.AreEqual(24.0, Number("volts * 2", numbers: numbers));
        Assert.AreEqual(7.0, Number("gps.sats", numbers: numbers));
        Assert.AreEqual(Number("{gps.sats} + {volts}", numbers: numbers), Number("gps.sats + volts", numbers: numbers));
        CollectionAssert.AreEquivalent(new[] { "volts", "gps.sats" }, Expression.Parse("volts + gps.sats").ReferencedIds.ToArray());
        Assert.AreEqual(1.0, Number("has(volts)", numbers: numbers));
        Assert.AreEqual(0.0, Number("has(missing)", numbers: numbers));
    }

    [TestMethod]
    public void TrueAndFalse_AreOneAndZero_NotReferences()
    {
        Assert.AreEqual(1.0, Number("true && !false"));
        Assert.AreEqual(0, Expression.Parse("true ? 1 : 2").ReferencedIds.Count);
    }

    [TestMethod]
    public void BareIdentifier_ReadsPublishedText()
    {
        var strings = new Dictionary<string, string> { ["model"] = "KA3005P" };

        Assert.AreEqual(1.0, Number("startsWith(model, 'KA') && model[0] == 'K'", strings));
    }

    [TestMethod]
    [DataRow("[1, 2")]
    [DataRow("[1, 2][")]
    [DataRow("[1 2]")]
    [DataRow("1 in")]
    public void MalformedLists_AreParseErrors(string text)
    {
        Assert.IsFalse(Expression.TryParse(text, out _, out var error));
        Assert.IsFalse(string.IsNullOrEmpty(error));
    }

    [TestMethod]
    public void UnknownFunction_IsStillAParseError_NotABareIdentifier()
    {
        Assert.IsFalse(Expression.TryParse("frobnicate(1)", out _, out var error));
        StringAssert.Contains(error, "frobnicate");
    }
}
