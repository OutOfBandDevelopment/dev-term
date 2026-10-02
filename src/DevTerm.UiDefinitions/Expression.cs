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
    private static readonly TimeSpan _regexTimeout = TimeSpan.FromMilliseconds(100);

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
        return _root.Eval(new Context(values, null)).AsNumber();
    }

    /// <summary>
    /// Evaluates against numeric <paramref name="values"/> plus text values (a decoder's string captures): a <c>{id}</c> present
    /// in <paramref name="text"/> reads as that string, any other as its number (0 when absent). Never throws.
    /// A text result is returned as its number when it reads as one, else <see cref="double.NaN"/>.
    /// </summary>
    public double Evaluate(IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, string>? text)
    {
        ArgumentNullException.ThrowIfNull(values);
        return _root.Eval(new Context(values, text)).AsNumber();
    }

    private readonly record struct Context(IReadOnlyDictionary<string, double> Numbers, IReadOnlyDictionary<string, string>? Text);

    /// <summary>A value: a number, or a string when <see cref="Text"/> is set. Booleans are the numbers 1 and 0.</summary>
    private readonly record struct Value(double Number, string? Text)
    {
        public static Value Of(double number) => new(number, null);

        public static Value Of(bool flag) => new(flag ? 1 : 0, null);

        public static Value Of(string text) => new(0, text);

        public bool IsText => Text is not null;

        public bool IsTrue => IsText ? Text!.Length > 0 : Number != 0;

        public double AsNumber() =>
            !IsText ? Number : double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;

        public string AsText() => IsText ? Text! : Number.ToString("G15", CultureInfo.InvariantCulture);
    }

    private abstract class Node
    {
        public abstract Value Eval(Context context);
    }

    private sealed class NumberNode(double value) : Node
    {
        public override Value Eval(Context context) => Value.Of(value);
    }

    private sealed class StringNode(string value) : Node
    {
        public string Literal => value;

        public override Value Eval(Context context) => Value.Of(value);
    }

    private sealed class VariableNode(string id) : Node
    {
        public bool IsPresent(Context context) => context.Text?.ContainsKey(id) == true || context.Numbers.ContainsKey(id);

        public override Value Eval(Context context) =>
            context.Text is not null && context.Text.TryGetValue(id, out var text) ? Value.Of(text)
            : context.Numbers.TryGetValue(id, out var number) ? Value.Of(number)
            : Value.Of(0);
    }

    private sealed class UnaryMinusNode(Node operand) : Node
    {
        public override Value Eval(Context context) => Value.Of(-operand.Eval(context).AsNumber());
    }

    private sealed class NotNode(Node operand) : Node
    {
        public override Value Eval(Context context) => Value.Of(!operand.Eval(context).IsTrue);
    }

    private sealed class BinaryNode(string op, Node left, Node right) : Node
    {
        public override Value Eval(Context context)
        {
            var l = left.Eval(context);

            // && / || short-circuit: the right side is only evaluated when it can affect the result.
            if (op == "&&")
            {
                return Value.Of(l.IsTrue && right.Eval(context).IsTrue);
            }

            if (op == "||")
            {
                return Value.Of(l.IsTrue || right.Eval(context).IsTrue);
            }

            var r = right.Eval(context);
            if (l.IsText || r.IsText)
            {
                var ls = l.AsText();
                var rs = r.AsText();
                return op switch
                {
                    "+" => Value.Of(ls + rs),
                    "==" => Value.Of(string.Equals(ls, rs, StringComparison.Ordinal)),
                    "!=" => Value.Of(!string.Equals(ls, rs, StringComparison.Ordinal)),
                    "<" => Value.Of(string.CompareOrdinal(ls, rs) < 0),
                    "<=" => Value.Of(string.CompareOrdinal(ls, rs) <= 0),
                    ">" => Value.Of(string.CompareOrdinal(ls, rs) > 0),
                    ">=" => Value.Of(string.CompareOrdinal(ls, rs) >= 0),
                    _ => Value.Of(double.NaN),
                };
            }

            var ln = l.Number;
            var rn = r.Number;
            return op switch
            {
                "+" => Value.Of(ln + rn),
                "-" => Value.Of(ln - rn),
                "*" => Value.Of(ln * rn),
                "/" => Value.Of(ln / rn),
                "<" => Value.Of(ln < rn),
                "<=" => Value.Of(ln <= rn),
                ">" => Value.Of(ln > rn),
                ">=" => Value.Of(ln >= rn),
                "==" => Value.Of(ln == rn),
                "!=" => Value.Of(ln != rn),
                _ => throw new InvalidOperationException($"unreachable operator '{op}'"),
            };
        }
    }

    private sealed class IfNode(Node condition, Node whenTrue, Node whenFalse) : Node
    {
        public override Value Eval(Context context) =>
            condition.Eval(context).IsTrue ? whenTrue.Eval(context) : whenFalse.Eval(context);
    }

    private sealed class FunctionNode(string name, IReadOnlyList<Node> args) : Node
    {
        public override Value Eval(Context context)
        {
            var a = args.Select(n => n.Eval(context).AsNumber()).ToArray();
            return Value.Of(name switch
            {
                "abs" => Math.Abs(a[0]),
                "round" when a.Length == 1 => Math.Round(a[0], MidpointRounding.AwayFromZero),
                "round" => Math.Round(a[0], Math.Clamp((int)a[1], 0, 15), MidpointRounding.AwayFromZero),
                "min" => a.Min(),
                "max" => a.Max(),
                _ => throw new InvalidOperationException($"unreachable function '{name}'"),
            });
        }
    }

    private sealed class TextFunctionNode(string name, IReadOnlyList<Node> args, Regex? pattern) : Node
    {
        public override Value Eval(Context context)
        {
            if (name == "has")
            {
                return Value.Of(args[0] is VariableNode variable && variable.IsPresent(context));
            }

            var first = args[0].Eval(context);
            var text = first.AsText();
            switch (name)
            {
                case "size":
                    return Value.Of(text.Length);
                case "number":
                    return Value.Of(first.AsNumber());
                case "string":
                    return Value.Of(text);
            }

            var second = args[1].Eval(context).AsText();
            switch (name)
            {
                case "contains":
                    return Value.Of(text.Contains(second, StringComparison.Ordinal));
                case "startsWith":
                    return Value.Of(text.StartsWith(second, StringComparison.Ordinal));
                case "endsWith":
                    return Value.Of(text.EndsWith(second, StringComparison.Ordinal));
                case "matches":
                    try
                    {
                        var regex = pattern ?? new Regex(second, RegexOptions.None, _regexTimeout);
                        return Value.Of(regex.IsMatch(text));
                    }
                    catch (ArgumentException)
                    {
                        return Value.Of(false);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        return Value.Of(false);
                    }

                default:
                    throw new InvalidOperationException($"unreachable function '{name}'");
            }
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
        String,
        Not,
        Question,
        Colon,
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

                if (c is '\'' or '"')
                {
                    i = ReadString(text, i, tokens);
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
                    case '!':
                        tokens.Add(new Token(TokenKind.Not, "!"));
                        i++;
                        break;
                    case '?':
                        tokens.Add(new Token(TokenKind.Question, "?"));
                        i++;
                        break;
                    case ':':
                        tokens.Add(new Token(TokenKind.Colon, ":"));
                        i++;
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

        /// <summary>Reads a quoted literal starting at <paramref name="start"/> (either quote; backslash escapes n, r, t, the quote and backslash); returns the index after it.</summary>
        private static int ReadString(string text, int start, List<Token> tokens)
        {
            var quote = text[start];
            var builder = new System.Text.StringBuilder();
            var i = start + 1;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == quote)
                {
                    tokens.Add(new Token(TokenKind.String, builder.ToString()));
                    return i + 1;
                }

                if (c == '\\' && i + 1 < text.Length)
                {
                    var next = text[i + 1];

                    // Any other escape (\d, \., ...) keeps its backslash, so a regex literal reads naturally.
                    builder.Append(next switch
                    {
                        'n' => "\n",
                        'r' => "\r",
                        't' => "\t",
                        '\\' => "\\",
                        '\'' => "'",
                        '"' => "\"",
                        _ => "\\" + next,
                    });
                    i += 2;
                    continue;
                }

                builder.Append(c);
                i++;
            }

            throw new ExpressionParseException("a string literal has no closing quote.");
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

        public Node ParseExpression() => ParseTernary();

        private Node ParseTernary()
        {
            var condition = ParseOr();
            if (Current.Kind != TokenKind.Question)
            {
                return condition;
            }

            _pos++;
            var whenTrue = ParseTernary();
            Expect(TokenKind.Colon, ":");
            return new IfNode(condition, whenTrue, ParseTernary());
        }

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

            if (Current.Kind == TokenKind.Not)
            {
                _pos++;
                return new NotNode(ParseUnary());
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

                case TokenKind.String:
                    {
                        var literal = Current.Text;
                        _pos++;
                        return new StringNode(literal);
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
                        var inner = ParseTernary();
                        Expect(TokenKind.RParen, ")");
                        return inner;
                    }

                case TokenKind.Identifier:
                    return ParseCall();

                default:
                    throw new ExpressionParseException($"expected a number, string, '{{variable}}', '(' or a function, found '{Current.Text}'.");
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
                args.Add(ParseTernary());
                while (Current.Kind == TokenKind.Comma)
                {
                    _pos++;
                    args.Add(ParseTernary());
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
                case "size" or "number" or "string" when args.Count == 1:
                    return new TextFunctionNode(name, args, null);
                case "has" when args.Count == 1 && args[0] is VariableNode:
                    return new TextFunctionNode(name, args, null);
                case "has":
                    throw new ExpressionParseException("'has' takes one {id}, e.g. has({volts}).");
                case "contains" or "startsWith" or "endsWith" when args.Count == 2:
                    return new TextFunctionNode(name, args, null);
                case "matches" when args.Count == 2:
                    return new TextFunctionNode(name, args, CompileLiteralPattern(args[1]));
                case "size" or "number" or "string" or "contains" or "startsWith" or "endsWith" or "matches":
                    throw new ExpressionParseException($"'{name}' was given {args.Count} argument(s), which isn't valid for it.");
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

        /// <summary>A literal regex is compiled once, so a bad pattern is a parse error rather than a silent false.</summary>
        private static Regex? CompileLiteralPattern(Node pattern)
        {
            if (pattern is not StringNode literal)
            {
                return null;
            }

            try
            {
                return new Regex(literal.Literal, RegexOptions.None, _regexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ExpressionParseException($"'{literal.Literal}' isn't a valid regular expression ({ex.Message}).");
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
