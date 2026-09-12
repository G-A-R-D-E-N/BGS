using System;
using System.Text.Json;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ProjectRoundTripReportTests
{
    [Fact]
    public void EveryProjectReportFormatCarriesRoundTripLosses()
    {
        var chain = new ProjectChain { Root = "/mods/Test" };
        var result = new ProjectCheck.Result();
        var file = new ProjectCheck.FileResult
        {
            Name = "unsafe.hkx",
            Path = "/mods/Test/Behaviors/unsafe.hkx",
        };
        file.RoundTrip.Add(new RoundTripLoss(
            RoundTripLossKind.UnsupportedClass,
            42,
            "mode",
            "unsupported <class> & value"));
        result.Files.Add(file);

        string json = ProjectReport.Render(chain, result, ProjectReport.Format.Json);
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("totals").GetProperty("roundTripLosses").GetInt32());
            Assert.Equal(1, root.GetProperty("totals").GetProperty("filesWithRoundTripLosses").GetInt32());
            var loss = root.GetProperty("files")[0].GetProperty("roundTripLosses")[0];
            Assert.Equal("UnsupportedClass", loss.GetProperty("kind").GetString());
            Assert.Equal(42, loss.GetProperty("objectId").GetInt64());
            Assert.Equal("mode", loss.GetProperty("member").GetString());
            Assert.True(loss.GetProperty("blocksSave").GetBoolean());
        }

        string csv = ProjectReport.Render(chain, result, ProjectReport.Format.Csv);
        Assert.Contains(",round-trip-loss,error,42,mode,", csv, StringComparison.Ordinal);
        Assert.Contains("unsupported <class> & value,true", csv, StringComparison.Ordinal);

        string html = ProjectReport.Render(chain, result, ProjectReport.Format.Html);
        Assert.Contains("Round-trip issues", html, StringComparison.Ordinal);
        Assert.Contains("round-trip-loss", html, StringComparison.Ordinal);
        Assert.Contains("unsupported &lt;class&gt; &amp; value", html, StringComparison.Ordinal);
    }
}
