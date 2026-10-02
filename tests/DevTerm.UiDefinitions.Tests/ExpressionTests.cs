using DevTerm.Test.Utilities;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// <see cref="Expression"/>'s grammar, its never-throws <see cref="Expression.Evaluate"/>, and
/// <see cref="Expression.ReferencedIds"/> — see docs/design/features/manifest-editor-expression-builder.md.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ExpressionTests
{
    private static double Eval(string text, IReadOnlyDictionary<string, double>? values = null) =>
        Expression.Parse(text).Evaluate(values ?? new Dictionary<string, double>());

    [TestMethod]
    [DataRow("42", 42.0)]
    [DataRow("4.5", 4.5)]
    [DataRow(".5", 0.5)]
    [DataRow("1e3", 1000.0)]
    [DataRow("1.5E+2", 150.0)]
    [DataRow("1e-2", 0.01)]
    public void NumericLiterals_EvaluateToThemselves(string text, double expected) =>
        Assert.AreEqual(expected, Eval(text), 1e-9);

    [TestMethod]
    public void Variable_EvaluatesToItsPublishedValue() =>
        Assert.AreEqual(5.5, Eval("{raw_mv}", new Dictionary<string, double> { ["raw_mv"] = 5.5 }), 1e-9);

    [TestMethod]
    public void Variable_MissingFromValues_EvaluatesToZero() =>
        Assert.AreEqual(0, Eval("{missing}"));

    [TestMethod]
    [DataRow("1 + 2", 3.0)]
    [DataRow("5 - 2", 3.0)]
    [DataRow("3 * 4", 12.0)]
    [DataRow("10 / 4", 2.5)]
    [DataRow("2 + 3 * 4", 14.0, "Multiplication binds tighter than addition.")]
    [DataRow("(2 + 3) * 4", 20.0, "Parens override precedence.")]
    [DataRow("-5", -5.0)]
    [DataRow("-(2 + 3)", -5.0)]
    [DataRow("+5", 5.0)]
    [DataRow("- -5", 5.0, "Unary minus is right-associative with itself.")]
    public void ArithmeticAndParens_EvaluateInExpectedOrder(string text, double expected, string? because = null) =>
        Assert.AreEqual(expected, Eval(text), 1e-9, because);

    [TestMethod]
    public void Division_ByZero_PropagatesAsInfinityOrNaN_RatherThanThrowing()
    {
        Assert.AreEqual(double.PositiveInfinity, Eval("1 / 0"));
        Assert.IsTrue(double.IsNaN(Eval("0 / 0")));
    }

    [TestMethod]
    [DataRow("1 < 2", 1.0)]
    [DataRow("2 < 1", 0.0)]
    [DataRow("2 <= 2", 1.0)]
    [DataRow("3 <= 2", 0.0)]
    [DataRow("2 > 1", 1.0)]
    [DataRow("1 >= 2", 0.0)]
    [DataRow("2 == 2", 1.0)]
    [DataRow("2 != 2", 0.0)]
    public void Comparisons_YieldOneOrZero(string text, double expected) =>
        Assert.AreEqual(expected, Eval(text));

    [TestMethod]
    [DataRow("1 && 1", 1.0)]
    [DataRow("1 && 0", 0.0)]
    [DataRow("0 || 1", 1.0)]
    [DataRow("0 || 0", 0.0)]
    public void LogicalOperators_YieldOneOrZero(string text, double expected) =>
        Assert.AreEqual(expected, Eval(text));

    [TestMethod]
    public void LogicalAnd_ShortCircuits_WithoutEvaluatingTheRightSide() =>
        Assert.AreEqual(0, Eval("0 && (1 / 0)"), "If the right side were evaluated, it would be infinity, not 0.");

    [TestMethod]
    public void LogicalOr_ShortCircuits_WithoutEvaluatingTheRightSide() =>
        Assert.AreEqual(1, Eval("1 || (1 / 0)"), "If the right side were evaluated, it would be infinity, not 1.");

    [TestMethod]
    [DataRow("abs(-5)", 5.0)]
    [DataRow("abs(5)", 5.0)]
    [DataRow("round(1.4)", 1.0)]
    [DataRow("round(1.5)", 2.0, "Away-from-zero, not banker's rounding.")]
    [DataRow("round(-1.5)", -2.0)]
    [DataRow("round(1.2345, 2)", 1.23)]
    [DataRow("min(3, 1, 2)", 1.0)]
    [DataRow("max(3, 1, 2)", 3.0)]
    [DataRow("min(1, 2)", 1.0)]
    public void Functions_Evaluate(string text, double expected, string? because = null) =>
        Assert.AreEqual(expected, Eval(text), 1e-9, because);

    [TestMethod]
    public void Round_WithFractionDigitsAboveFifteen_IsClampedRatherThanThrowing() =>
        Assert.AreEqual(1.23456789012, Eval("round(1.234567890123456, 99)"), 1e-9);

    [TestMethod]
    [DataRow("if(1, 10, 20)", 10.0)]
    [DataRow("if(0, 10, 20)", 20.0)]
    [DataRow("if({x} > 5, 1, 0)", 0.0)]
    public void If_EvaluatesTheTrueOrFalseBranch(string text, double expected) =>
        Assert.AreEqual(expected, Eval(text));

    [TestMethod]
    public void NestedExpression_EvaluatesRealistically() =>
        Assert.AreEqual(89.6, Eval("round({temp_c} * 9 / 5 + 32, 1)", new Dictionary<string, double> { ["temp_c"] = 32 }), 1e-9);

    [TestMethod]
    [DataRow("", "the expression is empty.")]
    [DataRow("   ", "the expression is empty.")]
    [DataRow("1 +", "")]
    [DataRow("(1 + 2", "")]
    [DataRow("1 2", "")]
    [DataRow("{bad id}", "")]
    [DataRow("{unterminated", "")]
    [DataRow("unknownfunc(1)", "")]
    [DataRow("abs(1, 2)", "")]
    [DataRow("round()", "")]
    [DataRow("if(1, 2)", "")]
    [DataRow("1 $ 2", "")]
    public void Parse_RejectsMalformedText(string text, string expectedMessagePrefix)
    {
        var ex = Assert.ThrowsExactly<ExpressionParseException>(() => Expression.Parse(text));
        if (expectedMessagePrefix.Length > 0)
        {
            Assert.AreEqual(expectedMessagePrefix, ex.Message);
        }
    }

    [TestMethod]
    public void TryParse_OnGoodText_ReturnsTrueAndTheExpression()
    {
        Assert.IsTrue(Expression.TryParse("{x} + 1", out var expression, out var error));
        Assert.IsNotNull(expression);
        Assert.IsNull(error);
        Assert.AreEqual("{x} + 1", expression!.Text);
    }

    [TestMethod]
    public void TryParse_OnBadText_ReturnsFalseAndAMessage_RatherThanThrowing()
    {
        Assert.IsFalse(Expression.TryParse("1 +", out var expression, out var error));
        Assert.IsNull(expression);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void ReferencedIds_CollectsEveryDistinctVariable_InFirstSeenOrder_NoDuplicates()
    {
        var expression = Expression.Parse("{b} + {a} + {b} + round({c}, {a})");

        Assert.AreSequenceEqual(["b", "a", "c"], [.. expression.ReferencedIds]);
    }

    [TestMethod]
    public void ReferencedIds_IsEmpty_ForAnExpressionWithNoVariables() =>
        Assert.IsEmpty(Expression.Parse("1 + 2").ReferencedIds);

    [TestMethod]
    public void ReferencedIds_ReachesInsideNestedFunctionCallsAndParens() =>
        Assert.AreSequenceEqual(["x", "y", "z"], [.. Expression.Parse("if(({x} > 0), min({y}, 1), max({z}, 2))").ReferencedIds]);

    [TestMethod]
    public void Evaluate_NullValues_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => Expression.Parse("1").Evaluate(null!));

    [TestMethod]
    public void Parse_NullText_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => Expression.Parse(null!));
}
