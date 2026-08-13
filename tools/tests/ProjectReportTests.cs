using System;
using System.IO;
using System.Text.Json;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ProjectReportTests
{
    [Fact]
    public void JsonContainsSummaryFilesAndFindings()
    {
        var (chain, result) = Sample();

        string json = ProjectReport.Render(chain, result, ProjectReport.Format.Json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(chain.Root, root.GetProperty("project").GetString());
        Assert.Equal(3, root.GetProperty("totals").GetProperty("files").GetInt32());
        Assert.Equal(1, root.GetProperty("totals").GetProperty("unreadable").GetInt32());
        Assert.Equal(1, root.GetProperty("totals").GetProperty("errors").GetInt32());
        Assert.Equal(1, root.GetProperty("totals").GetProperty("warnings").GetInt32());

        var files = root.GetProperty("files");
        Assert.Equal(3, files.GetArrayLength());
        Assert.Equal("broken & bad.hkx", files[1].GetProperty("name").GetString());
        Assert.Equal("could not <parse>", files[1].GetProperty("unreadable").GetString());
        Assert.True(files[2].GetProperty("findings")[0].GetProperty("blocksSave").GetBoolean());
    }

    [Fact]
    public void CsvQuotesFieldsAndNeutralizesSpreadsheetFormulas()
    {
        var (chain, result) = Sample();
        result.Files[2].Findings[0].What = "=HYPERLINK(\"bad\"), still bad";

        string csv = ProjectReport.Render(chain, result, ProjectReport.Format.Csv);

        Assert.StartsWith("project,file,path,status,severity,object_id,where,message,blocks_save", csv);
        Assert.Contains("\"' =never\"", CsvForFormula(" =never"), StringComparison.Ordinal);
        Assert.Contains("\"'=HYPERLINK(\"\"bad\"\"), still bad\"", csv, StringComparison.Ordinal);
        Assert.Contains("broken & bad.hkx", csv, StringComparison.Ordinal);
        Assert.Contains(",unreadable,error,,,could not <parse>,", csv, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@cmd")]
    [InlineData("\tformula")]
    public void CsvNeutralizesDangerousLeadingCharacters(string value)
    {
        var chain = new ProjectChain { Root = "project" };
        var result = new ProjectCheck.Result();
        var file = new ProjectCheck.FileResult { Name = "one.hkx", Path = "one.hkx" };
        file.Findings.Add(new GraphValidator.Finding
        {
            Level = GraphValidator.Level.Warning,
            What = value,
        });
        result.Files.Add(file);

        string csv = ProjectReport.Render(chain, result, ProjectReport.Format.Csv);

        Assert.Contains("'" + value, csv, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" =SUM(1,2)")]
    [InlineData("  +1")]
    [InlineData(" -1")]
    [InlineData("\t@cmd")]
    public void CsvNeutralizesFormulasHiddenBehindLeadingBlanks(string value)
    {
        string csv = CsvForFormula(value);

        // The quotes matter: without them the blank the formula hides behind is lost on re-import.
        Assert.Contains("\"'" + value + "\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlEncodesUntrustedFileAndFindingText()
    {
        var (chain, result) = Sample();

        string html = ProjectReport.Render(chain, result, ProjectReport.Format.Html);

        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("broken &amp; bad.hkx", html, StringComparison.Ordinal);
        Assert.Contains("could not &lt;parse&gt;", html, StringComparison.Ordinal);
        Assert.Contains("bad &lt;value&gt; &amp; &quot;quoted&quot;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("could not <parse>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSelectsTheFormatFromTheExtensionAndReplacesTheDestination()
    {
        var (chain, result) = Sample();
        string folder = Directory.CreateTempSubdirectory("bgs-project-report").FullName;
        try
        {
            string path = Path.Combine(folder, "report.json");
            File.WriteAllText(path, "old");

            ProjectReport.Write(path, chain, result);

            string written = File.ReadAllText(path);
            Assert.StartsWith("{", written.TrimStart());
            Assert.DoesNotContain("old", written, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(folder, "*.writing", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [Theory]
    [InlineData("report.html", ProjectReport.Format.Html)]
    [InlineData("report.htm", ProjectReport.Format.Html)]
    [InlineData("report.JSON", ProjectReport.Format.Json)]
    [InlineData("report.csv", ProjectReport.Format.Csv)]
    public void FormatFollowsTheFileExtension(string path, ProjectReport.Format expected) =>
        Assert.Equal(expected, ProjectReport.FormatForPath(path));

    [Fact]
    public void UnknownReportExtensionsAreRefused() =>
        Assert.Throws<ArgumentException>(() => ProjectReport.FormatForPath("report.txt"));

    private static string CsvForFormula(string value)
    {
        var chain = new ProjectChain { Root = "project" };
        var result = new ProjectCheck.Result();
        // Deliberately untrimmed: the leading blank is the thing under test.
        var file = new ProjectCheck.FileResult { Name = value, Path = value };
        result.Files.Add(file);
        return ProjectReport.Render(chain, result, ProjectReport.Format.Csv);
    }

    private static (ProjectChain Chain, ProjectCheck.Result Result) Sample()
    {
        var chain = new ProjectChain { Root = "/mods/Test & Project" };
        var result = new ProjectCheck.Result();

        result.Files.Add(new ProjectCheck.FileResult
        {
            Name = "clean.hkx",
            Path = "/mods/Test & Project/Behaviors/clean.hkx",
        });

        result.Files.Add(new ProjectCheck.FileResult
        {
            Name = "broken & bad.hkx",
            Path = "/mods/Test & Project/Behaviors/broken & bad.hkx",
            Error = "could not <parse>",
        });

        var findings = new ProjectCheck.FileResult
        {
            Name = "findings.hkx",
            Path = "/mods/Test & Project/Behaviors/findings.hkx",
        };
        findings.Findings.Add(new GraphValidator.Finding
        {
            Level = GraphValidator.Level.Error,
            ObjectId = "100",
            Where = "#100.animationName",
            What = "bad <value> & \"quoted\"",
            BlocksSave = true,
        });
        findings.Findings.Add(new GraphValidator.Finding
        {
            Level = GraphValidator.Level.Warning,
            ObjectId = "101",
            Where = "#101.name",
            What = "warning",
            BlocksSave = false,
        });
        result.Files.Add(findings);

        return (chain, result);
    }
}
