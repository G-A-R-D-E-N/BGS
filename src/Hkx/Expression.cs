using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Hkx;



































public static class Expression
{
    public enum Verdict { Unknown, True, False }

    public sealed record NumericResult(double? Value, string Refusal)
    {
        public bool Possible => Value != null;
    }


    public sealed record Parsed(Node? Root, string? Problem, bool IsAssignment,
                                IReadOnlyList<string> Names)
    {
        public bool Ok => Root != null;
        public override string ToString() => Problem ?? Root?.ToString() ?? "";
    }

    public abstract record Node
    {
        public sealed record Number(double Value) : Node
        {
            public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        }

        public sealed record Name(string Text) : Node
        {
            public override string ToString() => Text;
        }

        public sealed record Not(Node Inner) : Node
        {
            public override string ToString() => "!" + Inner;
        }

        public sealed record Compare(string Operator, Node Left, Node Right) : Node
        {
            public override string ToString() => $"({Left} {Operator} {Right})";
        }

        public sealed record Both(bool Or, Node Left, Node Right) : Node
        {
            public override string ToString() => $"({Left} {(Or ? "||" : "&&")} {Right})";
        }

        public sealed record Arithmetic(string Operator, Node Left, Node Right) : Node
        {
            public override string ToString() => $"({Left} {Operator} {Right})";
        }

        public sealed record Sign(bool Negative, Node Inner) : Node
        {
            public override string ToString() => (Negative ? "-" : "+") + Inner;
        }

        public sealed record Function(string FunctionName, IReadOnlyList<Node> Arguments) : Node
        {
            public override string ToString() => $"{FunctionName}({string.Join(", ", Arguments)})";
        }



        public sealed record Assign(string Variable, Node Value) : Node
        {
            public override string ToString() => $"{Variable} = {Value}";
        }
    }

    public static Parsed Parse(string text)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return new Parsed(null, "the condition is empty", false, names);

        List<Token> tokens;
        try { tokens = Scan(text); }
        catch (FormatException e) { return new Parsed(null, e.Message, false, names); }

        int at = 0;
        Node node;
        try { node = Assign(tokens, ref at, names); }
        catch (FormatException e) { return new Parsed(null, e.Message, false, names); }

        if (at != tokens.Count)
            return new Parsed(null, $"there is text after the end of the condition, from '{tokens[at].Text}'",
                              false, names);

        return new Parsed(node, null, node is Node.Assign, names);
    }






    public static Verdict Evaluate(Parsed parsed, Func<string, double?> value)
    {
        if (!parsed.Ok || parsed.IsAssignment) return Verdict.Unknown;
        return Truth(parsed.Root!, value);
    }

    public static Verdict Evaluate(string text, Func<string, double?> value) =>
        Evaluate(Parse(text), value);

    public static NumericResult EvaluateNumber(Parsed parsed, Func<string, double?> value)
    {
        if (!parsed.Ok) return new NumericResult(null, parsed.Problem ?? "the expression did not parse");
        Node node = parsed.Root! is Node.Assign assignment ? assignment.Value : parsed.Root!;
        return Numeric(node, value);
    }

    private static Verdict Truth(Node node, Func<string, double?> value)
    {
        switch (node)
        {
            case Node.Both both:
            {
                var left = Truth(both.Left, value);
                var right = Truth(both.Right, value);




                if (both.Or)
                {
                    if (left == Verdict.True || right == Verdict.True) return Verdict.True;
                    if (left == Verdict.False && right == Verdict.False) return Verdict.False;
                    return Verdict.Unknown;
                }

                if (left == Verdict.False || right == Verdict.False) return Verdict.False;
                if (left == Verdict.True && right == Verdict.True) return Verdict.True;
                return Verdict.Unknown;
            }

            case Node.Not not:
                return Truth(not.Inner, value) switch
                {
                    Verdict.True => Verdict.False,
                    Verdict.False => Verdict.True,
                    _ => Verdict.Unknown,
                };

            case Node.Compare compare:
            {
                if (Numeric(compare.Left, value).Value is not double left) return Verdict.Unknown;
                if (Numeric(compare.Right, value).Value is not double right) return Verdict.Unknown;

                bool answer = compare.Operator switch
                {
                    "==" => left == right,
                    "!=" => left != right,
                    "<" => left < right,
                    ">" => left > right,
                    "<=" => left <= right,
                    ">=" => left >= right,
                    _ => throw new InvalidOperationException(
                        $"'{compare.Operator}' got past the parser without being an operator this evaluates"),
                };
                return answer ? Verdict.True : Verdict.False;
            }



            default:
                if (Numeric(node, value).Value is not double alone) return Verdict.Unknown;
                return alone != 0 ? Verdict.True : Verdict.False;
        }
    }

    private static NumericResult Numeric(Node node, Func<string, double?> value)
    {
        NumericResult Read(Node part) => Numeric(part, value);
        NumericResult Fail(string why) => new(null, why);
        NumericResult Number(double numberValue) => double.IsFinite(numberValue)
            ? new NumericResult(numberValue, "")
            : Fail("the expression produces a non-finite number");

        switch (node)
        {
            case Node.Number number:
                return Number(number.Value);
            case Node.Name name:
                return value(name.Text) is double resolved
                    ? Number(resolved)
                    : Fail($"'{name.Text}' is not a declared runtime variable");
            case Node.Sign sign:
            {
                var inner = Read(sign.Inner);
                return inner.Value is double signed ? Number(sign.Negative ? -signed : signed) : inner;
            }
            case Node.Arithmetic arithmetic:
            {
                var left = Read(arithmetic.Left);
                if (left.Value is not double a) return left;
                var right = Read(arithmetic.Right);
                if (right.Value is not double b) return right;
                if (arithmetic.Operator == "/" && b == 0) return Fail("division by zero");
                return arithmetic.Operator switch
                {
                    "+" => Number(a + b),
                    "-" => Number(a - b),
                    "*" => Number(a * b),
                    "/" => Number(a / b),
                    _ => Fail($"'{arithmetic.Operator}' is not an arithmetic operator this evaluates"),
                };
            }
            case Node.Function call when call.FunctionName == "clamp":
            {
                if (call.Arguments.Count != 3) return Fail("clamp needs exactly three arguments");
                var number = Read(call.Arguments[0]);
                if (number.Value is not double clamped) return number;
                var low = Read(call.Arguments[1]);
                if (low.Value is not double minimum) return low;
                var high = Read(call.Arguments[2]);
                if (high.Value is not double maximum) return high;
                return Number(Math.Clamp(clamped, minimum, maximum));
            }
            case Node.Function call when call.FunctionName == "cond":
            {
                if (call.Arguments.Count != 3) return Fail("cond needs exactly three arguments");
                var verdict = Truth(call.Arguments[0], value);
                if (verdict == Verdict.Unknown) return Fail("cond has a test the runtime cannot decide");
                return Read(call.Arguments[verdict == Verdict.True ? 1 : 2]);
            }
            case Node.Function call:
                return Fail($"'{call.FunctionName}' is not a function this runtime evaluates");
            case Node.Compare or Node.Both or Node.Not:
            {
                return Truth(node, value) switch
                {
                    Verdict.True => Number(1),
                    Verdict.False => Number(0),
                    _ => Fail("the expression has a test the runtime cannot decide"),
                };
            }
            case Node.Assign assignment:
                return Read(assignment.Value);
            default:
                return Fail("the expression has a form this runtime cannot evaluate");
        }
    }

    private readonly record struct Token(string Text, bool IsName, bool IsNumber);

    private static List<Token> Scan(string text)
    {
        var tokens = new List<Token>();

        for (int at = 0; at < text.Length; )
        {
            char c = text[at];
            if (char.IsWhiteSpace(c)) { at++; continue; }

            if (char.IsLetter(c) || c == '_')
            {
                int start = at;
                while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;
                tokens.Add(new Token(text[start..at], true, false));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && at + 1 < text.Length && char.IsDigit(text[at + 1])))
            {
                int start = at;
                while (at < text.Length && (char.IsDigit(text[at]) || text[at] == '.')) at++;
                tokens.Add(new Token(text[start..at], false, true));
                continue;
            }



            if (at + 1 < text.Length)
            {
                string pair = text.Substring(at, 2);
                if (pair is "==" or "!=" or "<=" or ">=" or "&&" or "||")
                {
                    tokens.Add(new Token(pair, false, false));
                    at += 2;
                    continue;
                }
            }

            if (c is '<' or '>' or '!' or '(' or ')' or '=' or '-' or '+' or '*' or '/' or ',')
            {
                tokens.Add(new Token(c.ToString(), false, false));
                at++;
                continue;
            }

            throw new FormatException($"'{c}' is not something this reads, at character {at + 1}");
        }

        return tokens;
    }

    private static Node Assign(List<Token> tokens, ref int at, List<string> names)
    {
        var left = Or(tokens, ref at, names);
        if (at >= tokens.Count || tokens[at].Text != "=") return left;
        at++;
        if (left is not Node.Name name)
            throw new FormatException("there is an assignment to something that is not a variable");
        return new Node.Assign(name.Text, Or(tokens, ref at, names));
    }

    private static Node Or(List<Token> tokens, ref int at, List<string> names)
    {
        var left = And(tokens, ref at, names);
        while (at < tokens.Count && tokens[at].Text == "||")
        {
            at++;
            left = new Node.Both(true, left, And(tokens, ref at, names));
        }
        return left;
    }

    private static Node And(List<Token> tokens, ref int at, List<string> names)
    {
        var left = Compare(tokens, ref at, names);
        while (at < tokens.Count && tokens[at].Text == "&&")
        {
            at++;
            left = new Node.Both(false, left, Compare(tokens, ref at, names));
        }
        return left;
    }

    private static readonly HashSet<string> Comparisons =
        new(StringComparer.Ordinal) { "==", "!=", "<", ">", "<=", ">=" };

    private static Node Compare(List<Token> tokens, ref int at, List<string> names)
    {
        var left = Sum(tokens, ref at, names);
        if (at >= tokens.Count) return left;

        if (!Comparisons.Contains(tokens[at].Text)) return left;

        string op = tokens[at].Text;
        at++;
        return new Node.Compare(op, left, Sum(tokens, ref at, names));
    }

    private static Node Sum(List<Token> tokens, ref int at, List<string> names)
    {
        var left = Product(tokens, ref at, names);
        while (at < tokens.Count && tokens[at].Text is "+" or "-")
        {
            string op = tokens[at++].Text;
            left = new Node.Arithmetic(op, left, Product(tokens, ref at, names));
        }
        return left;
    }

    private static Node Product(List<Token> tokens, ref int at, List<string> names)
    {
        var left = Unary(tokens, ref at, names);
        while (at < tokens.Count && tokens[at].Text is "*" or "/")
        {
            string op = tokens[at++].Text;
            left = new Node.Arithmetic(op, left, Unary(tokens, ref at, names));
        }
        return left;
    }

    private static Node Unary(List<Token> tokens, ref int at, List<string> names)
    {
        if (at >= tokens.Count) throw new FormatException("the condition stops before it says anything");

        if (tokens[at].Text == "!")
        {
            at++;
            return new Node.Not(Unary(tokens, ref at, names));
        }




        if (tokens[at].Text is "-" or "+")
        {
            bool negative = tokens[at].Text == "-";
            at++;
            var inner = Unary(tokens, ref at, names);
            return new Node.Sign(negative, inner);
        }

        return Primary(tokens, ref at, names);
    }

    private static Node Primary(List<Token> tokens, ref int at, List<string> names)
    {
        if (at >= tokens.Count) throw new FormatException("the condition stops before it says anything");

        var token = tokens[at];

        if (token.Text == "(")
        {
            at++;
            var inner = Or(tokens, ref at, names);
            if (at >= tokens.Count || tokens[at].Text != ")")
                throw new FormatException("a bracket is opened and never closed");
            at++;
            return inner;
        }

        if (token.IsNumber)
        {
            at++;
            return double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? new Node.Number(value)
                : throw new FormatException($"'{token.Text}' is not a number this reads");
        }

        if (token.IsName)
        {
            at++;
            if (at < tokens.Count && tokens[at].Text == "(")
            {
                at++;
                var arguments = new List<Node>();
                if (at < tokens.Count && tokens[at].Text != ")")
                {
                    while (true)
                    {
                        arguments.Add(Or(tokens, ref at, names));
                        if (at >= tokens.Count || tokens[at].Text != ",") break;
                        at++;
                    }
                }
                if (at >= tokens.Count || tokens[at].Text != ")")
                    throw new FormatException($"the arguments to '{token.Text}' are not closed");
                at++;
                return new Node.Function(token.Text, arguments);
            }
            if (!names.Contains(token.Text)) names.Add(token.Text);
            return new Node.Name(token.Text);
        }

        throw new FormatException($"'{token.Text}' is not something a condition can start with");
    }
}
