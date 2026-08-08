using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Hkx;

// The little expression language a behaviour carries, and what it evaluates to.
//
// A transition can hold an `hkbExpressionCondition`, which is one line of text tested before the
// transition is allowed to fire. Until now nothing here read that text, so the stepper treated every
// conditional transition as able to fire and said so. That is the safe way round and it is also
// wrong often enough to matter: a door whose transition reads `bPartialCover==1` fires in the
// stepper whatever the variable holds.
//
// **The grammar is not guessed at, it is what the corpus contains.** `symrm conditions` reads every
// condition out of the 531 vanilla behaviours: 49 of them, 34 distinct, across 13 files. Between
// them they use exactly this much language:
//
//     a variable name, a number
//     == != < > >= <=          (<= appears in no vanilla condition; it is here because leaving one
//                               of a pair out is how a parser acquires a hole)
//     && ||                    always parenthesised in vanilla, not required here
//     !                        as in `!IsPlayer`
//     a bare variable          as in `!bBlockMoveStop`, true when the variable is not zero
//     ( )
//
// So this is a complete reading of the vanilla data rather than a subset of Havok's language. What
// Havok's own compiler accepts is wider, and anything wider than the above comes back Unknown rather
// than being approximated.
//
// **Unknown is the important part.** Three tri-state answers, and Unknown must always mean the
// transition can still fire. That way reading a condition can only ever remove a transition this can
// prove will not fire, and never adds one or hides one it did not understand. A build that stopped
// parsing correctly would go back to behaving exactly as the build before conditions existed.
//
// **One vanilla oddity, reported rather than resolved.** `iSyncIdleLocomotion=18` is written with a
// single `=`, which in this language is assignment and not a test. Havok's compiler would probably
// evaluate it to the assigned value and so to true, but "probably" is a guess about a runtime nobody
// here has, and this project does not guess about the runtime. It parses, it is classified as an
// assignment, and it evaluates to Unknown so the transition still fires.
public static class Expression
{
    public enum Verdict { Unknown, True, False }

    /// A parsed condition, or the reason it is not one.
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

        /// A single `=`, which is an assignment and not a test. Kept as its own shape so it is
        /// reported rather than quietly read as `==`.
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

    /// What a parsed condition comes to, given what the graph's variables hold.
    ///
    /// `value` answers with the variable's number, or null when the graph does not declare it. A
    /// name nothing declares is Unknown rather than zero: zero is a real value a variable can hold
    /// and would make `iIsInSneak == 0` come out true on a file that has no such variable at all.
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

                // Short circuits both ways round, so half an answer is still an answer where the
                // operator allows it: false && anything is false even when the other half names a
                // variable this file does not declare.
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

            // A bare variable, which is how `!IsPlayer` and `!bBlockMoveStop` are written. Anything
            // that is not zero is true, which is what a bool variable stored as a word means.
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

            // The two character operators first, or `!=` scans as `!` followed by `=` and the parser
            // sees a negation of an assignment.
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

        // A single `=` is an assignment. It is kept apart from `==` rather than folded into it,
        // because reading it as a test would be inventing a meaning for a line one vanilla file
        // carries and nothing here can check.
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

        // A sign in front of a number, which the expression modifier lines use freely and a condition
        // could. Folded into the number rather than becoming a node, since nothing here negates
        // anything else.
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
