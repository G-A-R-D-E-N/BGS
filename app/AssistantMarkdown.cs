using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace BehaviourStudio.App;

public enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    Bullet,
    Numbered,
    Code,
    Rule,
    Quote,
    Table,
}

public sealed record MarkdownSpan(string Text, bool Bold, bool Italic, bool Code, bool Strike = false);

public sealed record MarkdownTableRow(IReadOnlyList<IReadOnlyList<MarkdownSpan>> Cells);

public sealed record MarkdownBlock(
    MarkdownBlockKind Kind, IReadOnlyList<MarkdownSpan> Spans, int Level = 0, string Marker = "")
{
    public IReadOnlyList<MarkdownTableRow> Rows { get; init; } = Array.Empty<MarkdownTableRow>();
}

public static class AssistantMarkdown
{
    private const int MaximumIndent = 3;

    private static readonly FontFamily Mono =
        new("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace");

    public static IReadOnlyList<MarkdownBlock> Parse(string text)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrEmpty(text)) return blocks;

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var quote = new List<string>();
        var table = new List<IReadOnlyList<string>>();
        var code = new List<string>();
        string language = "";
        bool fenced = false;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, Inline(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        void FlushQuote()
        {
            if (quote.Count == 0) return;
            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Quote, Inline(string.Join(" ", quote))));
            quote.Clear();
        }

        void FlushTable()
        {
            if (table.Count == 0) return;
            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Table, Array.Empty<MarkdownSpan>())
            {
                Rows = table
                    .Select(row => new MarkdownTableRow(row.Select(Inline).ToArray()))
                    .ToArray(),
            });
            table.Clear();
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushQuote();
            FlushTable();
        }

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd();
            string trimmed = line.TrimStart();

            if (fenced)
            {
                if (IsFence(trimmed))
                {
                    fenced = false;
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code,
                        new[] { new MarkdownSpan(string.Join("\n", code), false, false, true) }, 0, language));
                    code.Clear();
                    language = "";
                }
                else
                {
                    code.Add(line);
                }
                continue;
            }

            if (IsFence(trimmed))
            {
                FlushAll();
                fenced = true;
                language = trimmed.Trim('`', '~').Trim();
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushAll();
                continue;
            }

            if (trimmed.StartsWith("|", StringComparison.Ordinal) && index + 1 < lines.Length &&
                IsTableSeparator(lines[index + 1]))
            {
                FlushParagraph();
                FlushQuote();
                table.Add(SplitCells(trimmed));
                index++;
                while (index + 1 < lines.Length && lines[index + 1].TrimStart().StartsWith("|", StringComparison.Ordinal))
                {
                    index++;
                    table.Add(SplitCells(lines[index].TrimStart()));
                }
                FlushTable();
                continue;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushTable();
                quote.Add(trimmed[1..].TrimStart());
                continue;
            }

            if (IsRule(trimmed))
            {
                FlushAll();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, Array.Empty<MarkdownSpan>()));
                continue;
            }

            int level = HeadingLevel(trimmed);
            if (level > 0)
            {
                FlushAll();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading,
                    Inline(trimmed[(level + 1)..].Trim()), level));
                continue;
            }

            if (TryBullet(trimmed, out string bullet))
            {
                FlushParagraph();
                FlushQuote();
                FlushTable();
                (string marker, string body) = TaskMarker(bullet);
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Bullet, Inline(body),
                    IndentLevel(line), marker));
                continue;
            }

            if (TryNumbered(trimmed, out string number, out string item))
            {
                FlushParagraph();
                FlushQuote();
                FlushTable();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Numbered, Inline(item),
                    IndentLevel(line), number));
                continue;
            }

            FlushQuote();
            FlushTable();
            paragraph.Add(trimmed);
        }

        if (fenced)
        {
            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code,
                new[] { new MarkdownSpan(string.Join("\n", code), false, false, true) }, 0, language));
        }
        FlushAll();
        return blocks;
    }

    public static IReadOnlyList<MarkdownSpan> Inline(string text)
    {
        var spans = new List<MarkdownSpan>();
        if (string.IsNullOrEmpty(text)) return spans;
        var buffer = new StringBuilder();
        int index = 0;

        void Flush()
        {
            if (buffer.Length == 0) return;
            spans.Add(new MarkdownSpan(buffer.ToString(), false, false, false));
            buffer.Clear();
        }

        while (index < text.Length)
        {
            char current = text[index];
            if (current == '`' && text.IndexOf('`', index + 1) is int tick && tick > index)
            {
                Flush();
                spans.Add(new MarkdownSpan(text[(index + 1)..tick], false, false, true));
                index = tick + 1;
                continue;
            }
            if (current == '~' && index + 1 < text.Length && text[index + 1] == '~' &&
                text.IndexOf("~~", index + 2, StringComparison.Ordinal) is int strike && strike > index)
            {
                Flush();
                spans.Add(new MarkdownSpan(text[(index + 2)..strike], false, false, false, true));
                index = strike + 2;
                continue;
            }
            if (current == '*' && index + 1 < text.Length && text[index + 1] == '*' &&
                text.IndexOf("**", index + 2, StringComparison.Ordinal) is int bold && bold > index)
            {
                Flush();
                spans.Add(new MarkdownSpan(text[(index + 2)..bold], true, false, false));
                index = bold + 2;
                continue;
            }
            if (current == '*' && text.IndexOf('*', index + 1) is int star && star > index + 1)
            {
                Flush();
                spans.Add(new MarkdownSpan(text[(index + 1)..star], false, true, false));
                index = star + 1;
                continue;
            }
            if (current == '_' && IsEmphasisUnderscore(text, index))
            {
                int close = text.IndexOf('_', index + 1);
                Flush();
                spans.Add(new MarkdownSpan(text[(index + 1)..close], false, true, false));
                index = close + 1;
                continue;
            }
            if (current == '[' && text.IndexOf(']', index + 1) is int label && label > index &&
                label + 1 < text.Length && text[label + 1] == '(' &&
                text.IndexOf(')', label + 2) is int target && target > label)
            {
                Flush();
                spans.Add(new MarkdownSpan(text[(index + 1)..label], false, false, false));
                index = target + 1;
                continue;
            }
            buffer.Append(current);
            index++;
        }

        Flush();
        return spans;
    }

    public static Control Render(string text)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (MarkdownBlock block in Parse(text)) panel.Children.Add(RenderBlock(block));
        return panel;
    }

    private static Control RenderBlock(MarkdownBlock block) => block.Kind switch
    {
        MarkdownBlockKind.Code => new Border
        {
            Background = Ux.RailBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Ux.Radius),
            Padding = new Thickness(8),
            Child = new TextBlock
            {
                Text = block.Spans.Count > 0 ? block.Spans[0].Text : "",
                FontFamily = Mono,
                FontSize = Ux.FontSmall,
                Foreground = Ux.CodeBrush,
                TextWrapping = TextWrapping.NoWrap,
            },
        },
        MarkdownBlockKind.Rule => new Border
        {
            Height = 1,
            Background = Ux.BorderBrush,
            Margin = new Thickness(0, 4, 0, 4),
        },
        MarkdownBlockKind.Quote => new Border
        {
            BorderBrush = Ux.AccentBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Child = Paragraph(block.Spans, Ux.MutedBrush, Ux.FontBody, FontWeight.Normal),
        },
        MarkdownBlockKind.Table => Table(block),
        MarkdownBlockKind.Heading => Paragraph(block.Spans, Ux.TitleBrush, HeadingSize(block.Level),
            FontWeight.SemiBold),
        MarkdownBlockKind.Bullet or MarkdownBlockKind.Numbered => Indented(block),
        _ => Paragraph(block.Spans, Ux.MetaBrush, Ux.FontBody, FontWeight.Normal),
    };

    private static Control Indented(MarkdownBlock block)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("20,*"),
            Margin = new Thickness(block.Level * 14, 0, 0, 0),
        };
        var marker = new TextBlock
        {
            Text = block.Marker,
            Foreground = Ux.MutedBrush,
            FontSize = Ux.FontBody,
        };
        var body = Paragraph(block.Spans, Ux.MetaBrush, Ux.FontBody, FontWeight.Normal);
        Grid.SetColumn(marker, 0);
        Grid.SetColumn(body, 1);
        grid.Children.Add(marker);
        grid.Children.Add(body);
        return grid;
    }

    private static Control Table(MarkdownBlock block)
    {
        int columns = block.Rows.Count == 0 ? 0 : block.Rows.Max(row => row.Cells.Count);
        if (columns == 0) return new TextBlock { Text = "" };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns))),
        };
        for (int row = 0; row < block.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            IReadOnlyList<IReadOnlyList<MarkdownSpan>> cells = block.Rows[row].Cells;
            bool header = row == 0;
            for (int column = 0; column < columns; column++)
            {
                Control text = column < cells.Count
                    ? Paragraph(cells[column], header ? Ux.TitleBrush : Ux.MetaBrush, Ux.FontSmall,
                        header ? FontWeight.SemiBold : FontWeight.Normal)
                    : new TextBlock();
                var host = new Border
                {
                    Padding = new Thickness(6, 3, 6, 3),
                    BorderBrush = Ux.BorderBrush,
                    BorderThickness = new Thickness(0, 0, 0, header ? 1 : 0),
                    Child = text,
                };
                Grid.SetRow(host, row);
                Grid.SetColumn(host, column);
                grid.Children.Add(host);
            }
        }
        return grid;
    }

    private static Control Paragraph(
        IReadOnlyList<MarkdownSpan> spans, IBrush brush, double size, FontWeight weight)
    {
        var text = new TextBlock
        {
            Foreground = brush,
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
        };
        foreach (MarkdownSpan span in spans)
        {
            var run = new Run(span.Text);
            if (span.Bold) run.FontWeight = FontWeight.SemiBold;
            if (span.Italic) run.FontStyle = FontStyle.Italic;
            if (span.Strike) run.TextDecorations = TextDecorations.Strikethrough;
            if (span.Code)
            {
                run.FontFamily = Mono;
                run.Foreground = Ux.CodeBrush;
            }
            text.Inlines!.Add(run);
        }
        return text;
    }

    internal static IReadOnlyList<string> SplitCells(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith("|", StringComparison.Ordinal)) trimmed = trimmed[1..];
        if (trimmed.EndsWith("|", StringComparison.Ordinal)) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    internal static bool IsTableSeparator(string line)
    {
        string trimmed = line.Trim().Trim('|').Trim();
        if (trimmed.Length == 0) return false;
        foreach (string cell in trimmed.Split('|'))
        {
            string value = cell.Trim();
            if (value.Length == 0) return false;
            string dashes = value.Trim(':');
            if (dashes.Length == 0) return false;
            foreach (char character in dashes)
                if (character != '-') return false;
        }
        return true;
    }

    private static (string Marker, string Body) TaskMarker(string body)
    {
        if (body.StartsWith("[ ] ", StringComparison.Ordinal)) return ("\u2610", body[4..]);
        if (body.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase)) return ("\u2611", body[4..]);
        return ("\u2022", body);
    }

    private static int IndentLevel(string line)
    {
        int spaces = 0;
        foreach (char character in line)
        {
            if (character == ' ') spaces++;
            else if (character == '\t') spaces += 2;
            else break;
        }
        return Math.Min(MaximumIndent, spaces / 2);
    }

    private static bool IsEmphasisUnderscore(string text, int index)
    {
        if (index > 0 && char.IsLetterOrDigit(text[index - 1])) return false;
        if (index + 1 >= text.Length || char.IsWhiteSpace(text[index + 1])) return false;
        int close = text.IndexOf('_', index + 1);
        if (close <= index + 1) return false;
        return close == text.Length - 1 || !char.IsLetterOrDigit(text[close + 1]);
    }

    private static double HeadingSize(int level) => level switch
    {
        1 => 17,
        2 => 15,
        3 => 14,
        _ => 13,
    };

    private static bool IsFence(string line) =>
        line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal);

    private static bool IsRule(string line)
    {
        if (line.Length < 3) return false;
        char marker = line[0];
        if (marker != '-' && marker != '*' && marker != '_') return false;
        foreach (char character in line)
            if (character != marker && character != ' ') return false;
        return true;
    }

    private static int HeadingLevel(string line)
    {
        int level = 0;
        while (level < line.Length && level < 6 && line[level] == '#') level++;
        return level > 0 && level < line.Length && line[level] == ' ' ? level : 0;
    }

    private static bool TryBullet(string line, out string body)
    {
        body = "";
        if (line.Length < 2) return false;
        if ((line[0] == '-' || line[0] == '*' || line[0] == '+') && line[1] == ' ')
        {
            body = line[2..].Trim();
            return true;
        }
        return false;
    }

    private static bool TryNumbered(string line, out string marker, out string body)
    {
        marker = "";
        body = "";
        int digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
        if (digits == 0 || digits + 1 >= line.Length) return false;
        if (line[digits] != '.' || line[digits + 1] != ' ') return false;
        marker = line[..(digits + 1)];
        body = line[(digits + 2)..].Trim();
        return true;
    }
}
