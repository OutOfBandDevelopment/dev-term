using System.Globalization;
using System.Text.RegularExpressions;

namespace DevTerm.UiDefinitions;

/// <summary>
/// Thrown by <see cref="Expression.Parse"/> when an expression's text doesn't parse — never thrown
/// by <see cref="Expression.Evaluate"/> itself, which always returns a number (see that method's
/// own remarks for why evaluation never throws).
/// </summary>
public sealed class ExpressionParseException : Exception
{
    public ExpressionParseException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A small expression language for mapping published decoder values to a derived number — a bar
/// graph/strip chart channel or an indicator's displayed text can show <c>({raw_mv} / 1000)</c>
/// instead of the raw value, and a button's parameter can send <c>round({temp_c} * 9 / 5 + 32)</c>
/// instead of a bare sibling-field lookup. See docs/design/proposals/manifest-editor-expression-builder.md.
/// </summary>
/// <remarks>
/// <para>
/// Grammar: numeric literals; <c>{id}</c> variable references (the same <c>{Name}</c> syntax
/// <see cref="DevTerm.DeviceManifests"/>'s command templates already use); <c>+ - * /</c> and unary
/// <c>-</c>; parentheses; comparisons (<c>&lt; &lt;= &gt; &gt;= == !=</c>, each yielding 1 or 0);
/// logical <c>&amp;&amp;</c>/<c>||</c> (short-circuiting, each yielding 1 or 0); the functions
/// <c>round(x)</c>/<c>round(x, n)</c>, <c>min(a, b, ...)</c>, <c>max(a, b, ...)</c>, <c>abs(x)</c>;
/// and the ternary-like <c>if(cond, a, b)</c>.
/// </para>
/// <para>
/// <see cref="Evaluate"/> never throws: a variable id missing from the values dictionary evaluates
/// to 0 (matching <see cref="VectorState"/>'s own convention for an unset coordinate), and a
/// division by zero propagates as <see cref="double.NaN"/>/<see cref="double.PositiveInfinity"/> via
/// ordinary IEEE double semantics rather than an exception — only <see cref="Parse"/> can fail, on a
/// genuine syntax error.
/// </para>
/// </remarks>
public sealed partial class Expression
{
    private readonly Node _root;

    private Expression(string text, Node root, IReadOnlyList<string> referencedIds)
    {
        Text = text;
        _root = root;
        ReferencedIds = referencedIds;
    }

    /// <summary>The original expression text this was parsed from.</summary>
    public string Text { get; }

    /// <summary>
    /// Every distinct <c>{id}</c> this expression references, in first-seen order — what a
    /// channel/indicator/parameter backed by this expression needs from the published-values batch
    /// before it can evaluate.
    /// </summary>
    public IReadOnlyList<string> ReferencedIds { get; }

    /// <summary>Parses <paramref name="text"/>, throwing <see cref="ExpressionParseException"/> on a syntax error.</summary>
    public static Expression Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Trim().Length == 0)
        {
            throw new ExpressionParseException("the expression is empty.");
        }

        var tokens = Lexer.Tokenize(text);
        var parser = new Parser(tokens);
        var root = parser.ParseExpression();
        parser.ExpectEnd();
        return new Expression(text, root, [.. parser.ReferencedIds]);
    }

    /// <summary>Like <see cref="Parse"/>, but reports a syntax error instead of throwing.</summary>
    public static bool TryParse(string text, out Expression? expression, out string? error)
    {
        try
        {
            expression = Parse(text);
            error = null;
            return true;
        }
        catch (ExpressionParseException ex)
        {
            expression = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Evaluates against <paramref name="values"/> — never throws; see the class remarks.</summary>
    public double Evaluate(IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return _root.Eval(values);
    }

    private abstract class Node
    {
        public abstract double Eval(IReadOnlyDictionary<string, double> values);
    }

    private sealed class NumberNode(double value) : Node
    {
        public override double Eval(IReadOnlyDictionary<string, double> values) => value;
    }

    private sealed class VariableNode(string id) : Node
    {
        public override double Eval(IReadOnlyDictionary<string, double> values) =>
            values.TryGetValue(id, out var value) ? value : 0;
    }

    private sealed class UnaryMinusNode(Node operand) : Node
    {
        public override double Eval(IReadOnlyDictionary<string, double> values) => -operand.Eval(values);
    }

    private sealed class BinaryNode(string op, Node left, Node right) : Node
    {
        public override double Eval(IReadOnlyDictionary<string, double> values)
        {
            var l = left.Eval(values);

            // && / || short-circuit: the right side is only evaluated when it can affect the result.
            if (op == "&&")
            {
                return l != 0 && right.Eval(values) != 0 ? 1 : 0;
            }

            if (op == "||")
            {
                return l != 0 || right.Eval(values) != 0 ? 1 : 0;
            }

            var r = right.Eval(values);
            return op switch
            {
                "+" => l + r,
                "-" => l - r,
                "*" => l * r,
                "/" => l / r,
                "<" => l < r ? 1 : 0,
                "<=" => l <= r ? 1 : 0,
                ">" => l > r ? 1 : 0,
                ">=" => l >= r ? 1 : 0,
                "==" => l == r ? 1 : 0,
                "!=" => l != r ? 1 : 0,
                _ => throw new InvalidOperationException($"unreachable operator '{op}'"),
            };
        }
    }

    private sealed class IfNode(Node condition, Node whenTrue, Node whenFalse) : Node
    {
        public override double Eval(IReadOnlyDictionary<string, double> values) =>
            condition.Eval(values) != 0 ? whenTrue.Eval(values) : whenFalse.Eval(values);
    }

    private sealed class FunctionNode(string name, IReadOnlyList<Node> args) : Node
    {
        public override double Eval(IReadOnlyDictionary<string, double> values)
        {
            var a = args.Select(n => n.Eval(values)).ToArray();
            return name switch
            {
                "abs" => Math.Abs(a[0]),
                "round" when a.Length == 1 => Math.Round(a[0], MidpointRounding.AwayFromZero),
                "round" => Math.Round(a[0], Math.Clamp((int)a[1], 0, 15), MidpointRounding.AwayFromZero),
                "min" => a.Min(),
                "max" => a.Max(),
                _ => throw new InvalidOperationException($"unreachable function '{name}'"),
            };
        }
    }

    private enum TokenKind
    {
        Number,
        Variable,
        Identifier,
        Plus,
        Minus,
        Star,
        Slash,
        LParen,
        RParen,
        Comma,
        Lt,
        Le,
        Gt,
        Ge,
        EqEq,
        NotEq,
        AndAnd,
        OrOr,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Number = 0);

    private static partial class Lexer
    {
        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                {
                    var start = i;
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                    {
                        i++;
                    }

                    if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
                    {
                        var j = i + 1;
                        if (j < text.Length && (text[j] == '+' || text[j] == '-'))
                        {
                            j++;
                        }

                        if (j < text.Length && char.IsDigit(text[j]))
                        {
                            i = j;
                            while (i < text.Length && char.IsDigit(text[i]))
                            {
                                i++;
                            }
                        }
                    }

                    var numberText = text[start..i];
                    if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        throw new ExpressionParseException($"'{numberText}' isn't a valid number.");
                    }

                    tokens.Add(new Token(TokenKind.Number, numberText, number));
                    continue;
                }

                if (c == '{')
                {
                    var start = i + 1;
                    var end = text.IndexOf('}', start);
                    if (end < 0)
                    {
                        throw new ExpressionParseException("'{' has no matching '}'.");
                    }

                    var id = text[start..end];
                    if (!VariableId().IsMatch(id))
                    {
                        throw new ExpressionParseException($"'{{{id}}}' isn't a valid variable reference.");
                    }

                    tokens.Add(new Token(TokenKind.Variable, id));
                    i = end + 1;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Identifier, text[start..i]));
                    continue;
                }

                switch (c)
                {
                    case '+':
                        tokens.Add(new Token(TokenKind.Plus, "+"));
                        i++;
                        break;
                    case '-':
                        tokens.Add(new Token(TokenKind.Minus, "-"));
                        i++;
                        break;
                    case '*':
                        tokens.Add(new Token(TokenKind.Star, "*"));
                        i++;
                        break;
                    case '/':
                        tokens.Add(new Token(TokenKind.Slash, "/"));
                        i++;
                        break;
                    case '(':
                        tokens.Add(new Token(TokenKind.LParen, "("));
                        i++;
                        break;
                    case ')':
                        tokens.Add(new Token(TokenKind.RParen, ")"));
                        i++;
                        break;
                    case ',':
                        tokens.Add(new Token(TokenKind.Comma, ","));
                        i++;
                        break;
                    case '<':
                        if (i + 1 < text.Length && text[i + 1] == '=')
                        {
                            tokens.Add(new Token(TokenKind.Le, "<="));
                            i += 2;
                        }
                        else
                        {
                            tokens.Add(new Token(TokenKind.Lt, "<"));
                            i++;
                        }

                        break;
                    case '>':
                        if (i + 1 < text.Length && text[i + 1] == '=')
                        {
                            tokens.Add(new Token(TokenKind.Ge, ">="));
                            i += 2;
                        }
                        else
                        {
                            tokens.Add(new Token(TokenKind.Gt, ">"));
                            i++;
                        }

                        break;
                    case '=' when i + 1 < text.Length && text[i + 1] == '=':
                        tokens.Add(new Token(TokenKind.EqEq, "=="));
                        i += 2;
                        break;
                    case '!' when i + 1 < text.Length && text[i + 1] == '=':
                        tokens.Add(new Token(TokenKind.NotEq, "!="));
                        i += 2;
                        break;
                    case '&' when i + 1 < text.Length && text[i + 1] == '&':
                        tokens.Add(new Token(TokenKind.AndAnd, "&&"));
                        i += 2;
                        break;
                    case '|' when i + 1 < text.Length && text[i + 1] == '|':
                        tokens.Add(new Token(TokenKind.OrOr, "||"));
                        i += 2;
                        break;
                    default:
                        throw new ExpressionParseException($"unexpected character '{c}'.");
                }
            }

            tokens.Add(new Token(TokenKind.End, string.Empty));
            return tokens;
        }

        [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
        public static partial Regex VariableId();
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;

        public HashSet<string> ReferencedIds { get; } = new(StringComparer.Ordinal);

        private Token Current => tokens[_pos];

        public void ExpectEnd()
        {
            if (Current.Kind != TokenKind.End)
            {
                throw new ExpressionParseException($"unexpected '{Current.Text}'.");
            }
        }

        public Node ParseExpression() => ParseOr();

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (Current.Kind == TokenKind.OrOr)
            {
                _pos++;
                left = new BinaryNode("||", left, ParseAnd());
            }

            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseComparison();
            while (Current.Kind == TokenKind.AndAnd)
            {
                _pos++;
                left = new BinaryNode("&&", left, ParseComparison());
            }

            return left;
        }

        private Node ParseComparison()
        {
            var left = ParseAdditive();
            while (Current.Kind is TokenKind.Lt or TokenKind.Le or TokenKind.Gt or TokenKind.Ge or TokenKind.EqEq or TokenKind.NotEq)
            {
                var op = Current.Text;
                _pos++;
                left = new BinaryNode(op, left, ParseAdditive());
            }

            return left;
        }

        private Node ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = Current.Text;
                _pos++;
                left = new BinaryNode(op, left, ParseMultiplicative());
            }

            return left;
        }

        private Node ParseMultiplicative()
        {
            var left = ParseUnary();
            while (Current.Kind is TokenKind.Star or TokenKind.Slash)
            {
                var op = Current.Text;
                _pos++;
                left = new BinaryNode(op, left, ParseUnary());
            }

            return left;
        }

        private Node ParseUnary()
        {
            if (Current.Kind == TokenKind.Minus)
            {
                _pos++;
                return new UnaryMinusNode(ParseUnary());
            }

            if (Current.Kind == TokenKind.Plus)
            {
                _pos++;
                return ParseUnary();
            }

            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            switch (Current.Kind)
            {
                case TokenKind.Number:
                    {
                        var value = Current.Number;
                        _pos++;
                        return new NumberNode(value);
                    }

                case TokenKind.Variable:
                    {
                        var id = Current.Text;
                        ReferencedIds.Add(id);
                        _pos++;
                        return new VariableNode(id);
                    }

                case TokenKind.LParen:
                    {
                        _pos++;
                        var inner = ParseOr();
                        Expect(TokenKind.RParen, ")");
                        return inner;
                    }

                case TokenKind.Identifier:
                    return ParseCall();

                default:
                    throw new ExpressionParseException($"expected a number, '{{variable}}', '(' or a function, found '{Current.Text}'.");
            }
        }

        private Node ParseCall()
        {
            var name = Current.Text;
            _pos++;
            Expect(TokenKind.LParen, "(");

            var args = new List<Node>();
            if (Current.Kind != TokenKind.RParen)
            {
                args.Add(ParseOr());
                while (Current.Kind == TokenKind.Comma)
                {
                    _pos++;
                    args.Add(ParseOr());
                }
            }

            Expect(TokenKind.RParen, ")");

            if (name == "if")
            {
                if (args.Count != 3)
                {
                    throw new ExpressionParseException($"'if' takes 3 arguments (condition, then, else), got {args.Count}.");
                }

                return new IfNode(args[0], args[1], args[2]);
            }

            switch (name)
            {
                case "abs" when args.Count == 1:
                case "round" when args.Count is 1 or 2:
                    return new FunctionNode(name, args);
                case "min" or "max" when args.Count >= 2:
                    return new FunctionNode(name, args);
                case "abs" or "round" or "min" or "max":
                    throw new ExpressionParseException($"'{name}' was given {args.Count} argument(s), which isn't valid for it.");
                default:
                    throw new ExpressionParseException($"unknown function '{name}'.");
            }
        }

        private void Expect(TokenKind kind, string what)
        {
            if (Current.Kind != kind)
            {
                throw new ExpressionParseException($"expected '{what}', found '{Current.Text}'.");
            }

            _pos++;
        }
    }
}
