using DevTerm.Test.Utilities;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// <see cref="IndicatorState"/> — the runtime companion for an <see cref="IndicatorControl"/> whose
/// <see cref="IndicatorControl.Expression"/> is set. See docs/design/features/manifest-editor-expression-builder.md.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class IndicatorStateTests
{
    [TestMethod]
    public void For_AnIndicatorWithNoExpression_ReturnsNull() =>
        Assert.IsNull(LiveDisplayState.For(new IndicatorControl { Id = "i", Label = "I" }));

    [TestMethod]
    public void For_AnIndicatorWithABlankExpression_ReturnsNull() =>
        Assert.IsNull(LiveDisplayState.For(new IndicatorControl { Id = "i", Label = "I", Expression = "" }));

    [TestMethod]
    public void For_AnIndicatorWithAnExpression_ReturnsAnIndicatorState() =>
        Assert.IsInstanceOfType<IndicatorState>(LiveDisplayState.For(new IndicatorControl { Id = "i", Label = "I", Expression = "{raw_mv} / 1000" }));

    [TestMethod]
    public void ValueIds_IsTheExpressionsReferencedIds_NotTheControlsOwnId()
    {
        var state = new IndicatorState(new IndicatorControl { Id = "i", Label = "I", Expression = "{raw_mv} / 1000" });

        Assert.AreSequenceEqual(["raw_mv"], [.. state.ValueIds]);
    }

    [TestMethod]
    public void Text_IsNull_UntilAReferencedIdArrives()
    {
        var state = new IndicatorState(new IndicatorControl { Id = "i", Label = "I", Expression = "{raw_mv} / 1000" });

        Assert.IsNull(state.Text);
        Assert.IsFalse(state.Apply("other", "1"), "An id the expression doesn't reference changes nothing.");
        Assert.IsNull(state.Text);
    }

    [TestMethod]
    public void Text_IsTheExpressionEvaluated_AndFormatted_OnceItsIdArrives()
    {
        var state = new IndicatorState(new IndicatorControl { Id = "i", Label = "I", Expression = "{raw_mv} / 1000" });

        Assert.IsTrue(state.Apply("raw_mv", "5500"));
        Assert.AreEqual("5.5", state.Text);
    }

    [TestMethod]
    public void Constructor_NeverThrows_OnAnInvalidExpression_AndLeavesTextAlwaysNull()
    {
        var state = new IndicatorState(new IndicatorControl { Id = "i", Label = "I", Expression = "1 +" });

        Assert.IsEmpty(state.ValueIds);
        Assert.IsNull(state.Text);
        Assert.IsFalse(state.Apply("i", "5"), "With no referenced ids, nothing this state sees can match.");
    }

    [TestMethod]
    public void Constructor_NullControl_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new IndicatorState(null!));

    [TestMethod]
    public void StringExpression_MatchesAgainstThePublishedText()
    {
        var state = new IndicatorState(new IndicatorControl { Id = "i", Label = "I", Expression = "matches({model}, '^KA') ? 'Korad' : 'Other'" });

        Assert.IsNull(state.Text);
        Assert.IsTrue(state.Apply("model", "KA3005P"), "A model name that holds digits is still published text.");
        Assert.AreEqual("Korad", state.Text);
        Assert.IsTrue(state.Apply("model", "RIGOL"), "Text with no number still changes the display.");
        Assert.AreEqual("Other", state.Text);
        Assert.IsFalse(state.Apply("model", "RIGOL"), "The same text again changes nothing.");
    }

    [TestMethod]
    public void NumericArithmetic_StillReadsANumberOutOfUnitText()
    {
        var state = new IndicatorState(new IndicatorControl { Id = "i", Label = "I", Expression = "{v} * 2" });

        state.Apply("v", "12.5 V");

        Assert.AreEqual("25", state.Text);
    }
}
