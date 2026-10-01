using System.Linq;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantMarkdownTests
{
    [Fact]
    public void BoldTextIsOneBoldSpanAndNotLiteralAsterisks()
    {
        MarkdownBlock block = Assert.Single(AssistantMarkdown.Parse("**bgs_check_project** — Check a project."));

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal("bgs_check_project", block.Spans[0].Text);
        Assert.True(block.Spans[0].Bold);
        Assert.DoesNotContain("**", string.Concat(block.Spans.Select(span => span.Text)));
    }

    [Fact]
    public void ABoldToolListStaysOneParagraphWithEveryToolBold()
    {
        const string reply =
            "Available BGS tools: - **bgs_check_project** — Check a behavior project. " +
            "- **bgs_search_project** — Search project data. " +
            "- **bgs_inspect_behavior** — Inspect a behavior HKX.";

        MarkdownBlock block = Assert.Single(AssistantMarkdown.Parse(reply));

        Assert.Equal(new[] { "bgs_check_project", "bgs_search_project", "bgs_inspect_behavior" },
            block.Spans.Where(span => span.Bold).Select(span => span.Text));
    }

    [Fact]
    public void InlineCodeItalicAndPlainTextAreSeparated()
    {
        var spans = AssistantMarkdown.Inline("use `bgs.set_clip_animation` for *previews* now");

        Assert.Equal("use ", spans[0].Text);
        Assert.True(spans[1].Code);
        Assert.Equal("bgs.set_clip_animation", spans[1].Text);
        Assert.True(spans[3].Italic);
        Assert.Equal("previews", spans[3].Text);
        Assert.Equal(" now", spans[4].Text);
    }

    [Fact]
    public void FencedCodeKeepsItsLinesAndLanguage()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse(
            "before\n\n```json\n{\n  \"a\": 1\n}\n```\n\nafter");

        MarkdownBlock code = blocks.Single(block => block.Kind == MarkdownBlockKind.Code);
        Assert.Equal("json", code.Marker);
        Assert.Equal("{\n  \"a\": 1\n}", Assert.Single(code.Spans).Text);
        Assert.Equal(2, blocks.Count(block => block.Kind == MarkdownBlockKind.Paragraph));
    }

    [Fact]
    public void AnUnclosedFenceStillRendersAsCode()
    {
        MarkdownBlock code = Assert.Single(AssistantMarkdown.Parse("```\nstill code"));

        Assert.Equal(MarkdownBlockKind.Code, code.Kind);
        Assert.Equal("still code", Assert.Single(code.Spans).Text);
    }

    [Fact]
    public void HeadingsCarryTheirLevel()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse("# One\n\n### Three\n\n####### NotAHeading");

        Assert.Equal(1, blocks[0].Level);
        Assert.Equal(MarkdownBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(3, blocks[1].Level);
        Assert.Equal(MarkdownBlockKind.Paragraph, blocks[2].Kind);
        Assert.Equal("####### NotAHeading", blocks[2].Spans[0].Text);
    }

    [Fact]
    public void BulletsAndNumberedItemsKeepTheirMarkers()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse("- first\n- second\n1. third\n2. fourth");

        Assert.Equal(MarkdownBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal("\u2022", blocks[0].Marker);
        Assert.Equal("second", blocks[1].Spans[0].Text);
        Assert.Equal(MarkdownBlockKind.Numbered, blocks[2].Kind);
        Assert.Equal("1.", blocks[2].Marker);
        Assert.Equal("fourth", blocks[3].Spans[0].Text);
    }

    [Fact]
    public void AThematicBreakBecomesARule()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse("above\n\n---\n\nbelow");

        Assert.Equal(MarkdownBlockKind.Rule, blocks[1].Kind);
    }

    [Fact]
    public void LinksShowTheirLabelRatherThanTheRawSyntax()
    {
        var spans = AssistantMarkdown.Inline("see [the guide](https://example.test/guide) for more");

        Assert.Contains(spans, span => span.Text == "the guide");
        Assert.DoesNotContain(spans, span => span.Text.Contains("http", System.StringComparison.Ordinal));
        Assert.DoesNotContain(spans, span => span.Text.Contains('[', System.StringComparison.Ordinal));
    }

    [Fact]
    public void UnmatchedMarkersAreShownVerbatimInsteadOfSwallowingText()
    {
        var spans = AssistantMarkdown.Inline("2 * 3 = 6 and a lone backtick ` here");

        Assert.Equal("2 * 3 = 6 and a lone backtick ` here", string.Concat(spans.Select(span => span.Text)));
    }

    [Fact]
    public void BlankLinesSplitParagraphsButWrappedProseJoins()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse("one\ntwo\n\nthree");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("one two", blocks[0].Spans[0].Text);
        Assert.Equal("three", blocks[1].Spans[0].Text);
    }

    [Fact]
    public void EmptyInputProducesNoBlocks()
    {
        Assert.Empty(AssistantMarkdown.Parse(""));
        Assert.Empty(AssistantMarkdown.Inline(""));
    }

    [Fact]
    public void APipeTableBecomesRowsOfCells()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse(
            "| Tool | What it does |\n| --- | ---: |\n| `bgs_check_project` | Checks |\n| `bgs_inspect_object` | Inspects |");

        MarkdownBlock table = Assert.Single(blocks);
        Assert.Equal(MarkdownBlockKind.Table, table.Kind);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Tool", table.Rows[0].Cells[0][0].Text);
        Assert.Equal("What it does", table.Rows[0].Cells[1][0].Text);
        Assert.True(table.Rows[1].Cells[0][0].Code);
        Assert.Equal("bgs_inspect_object", table.Rows[2].Cells[0][0].Text);
    }

    [Fact]
    public void PipesWithoutASeparatorRowStayOrdinaryText()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse("a | b\nnot a table");

        Assert.Equal(MarkdownBlockKind.Paragraph, Assert.Single(blocks).Kind);
    }

    [Fact]
    public void BlockquoteLinesJoinIntoOneQuote()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse("> first line\n> second line\n\nafter");

        Assert.Equal(MarkdownBlockKind.Quote, blocks[0].Kind);
        Assert.Equal("first line second line", blocks[0].Spans[0].Text);
        Assert.Equal("after", blocks[1].Spans[0].Text);
    }

    [Fact]
    public void StrikethroughIsMarkedOnItsSpan()
    {
        var spans = AssistantMarkdown.Inline("~~old~~ new");

        Assert.True(spans[0].Strike);
        Assert.Equal("old", spans[0].Text);
        Assert.Equal(" new", spans[1].Text);
    }

    [Fact]
    public void TaskListItemsGetCheckboxMarkers()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse("- [x] done\n- [ ] todo");

        Assert.Equal("\u2611", blocks[0].Marker);
        Assert.Equal("done", blocks[0].Spans[0].Text);
        Assert.Equal("\u2610", blocks[1].Marker);
        Assert.Equal("todo", blocks[1].Spans[0].Text);
    }

    [Fact]
    public void IndentedListItemsRecordTheirDepth()
    {
        IReadOnlyList<MarkdownBlock> blocks = AssistantMarkdown.Parse("- top\n  - child\n    - grandchild");

        Assert.Equal(0, blocks[0].Level);
        Assert.Equal(1, blocks[1].Level);
        Assert.Equal(2, blocks[2].Level);
    }

    [Fact]
    public void UnderscoresInsideIdentifiersAreKeptIntact()
    {
        var spans = AssistantMarkdown.Inline("call bgs_set_clip_animation on the file");

        Assert.Equal("call bgs_set_clip_animation on the file",
            string.Concat(spans.Select(span => span.Text)));
        Assert.DoesNotContain(spans, span => span.Italic);
    }

    [Fact]
    public void UnderscoreEmphasisStillWorksAtWordBoundaries()
    {
        var spans = AssistantMarkdown.Inline("this is _emphasised_ text");

        Assert.Contains(spans, span => span.Italic && span.Text == "emphasised");
    }
}
