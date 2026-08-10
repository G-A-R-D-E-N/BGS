using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Hkx;



































public static class Expression
{
    public enum Verdict { Unknown, True, False }


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
        try { node = Or(tokens, ref at, names); }
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
                if (Number(compare.Left, value) is not double left) return Verdict.Unknown;
                if (Number(compare.Right, value) is not double right) return Verdict.Unknown;

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
                if (Number(node, value) is not double alone) return Verdict.Unknown;
                return alone != 0 ? Verdict.True : Verdict.False;
        }
    }

    private static double? Number(Node node, Func<string, double?> value) => node switch
    {
        Node.Number number => number.Value,
        Node.Name name => value(name.Text),
        _ => null,
    };

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

            if (c is '<' or '>' or '!' or '(' or ')' or '=' or '-' or '+')
            {
                tokens.Add(new Token(c.ToString(), false, false));
                at++;
                continue;
            }

            throw new FormatException($"'{c}' is not something this reads, at character {at + 1}");
        }

        return tokens;
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
        var left = Unary(tokens, ref at, names);
        if (at >= tokens.Count) return left;




        if (tokens[at].Text == "=")
        {
            at++;
            var assigned = Unary(tokens, ref at, names);
            return left is Node.Name name
                ? new Node.Assign(name.Text, assigned)
                : throw new FormatException("there is an assignment to something that is not a variable");
        }

        if (!Comparisons.Contains(tokens[at].Text)) return left;

        string op = tokens[at].Text;
        at++;
        return new Node.Compare(op, left, Unary(tokens, ref at, names));
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
            return inner is Node.Number number
                ? new Node.Number(negative ? -number.Value : number.Value)
                : throw new FormatException("there is a sign in front of something that is not a number");
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
            if (!names.Contains(token.Text)) names.Add(token.Text);
            return new Node.Name(token.Text);
        }

        throw new FormatException($"'{token.Text}' is not something a condition can start with");
    }
}
