using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ProjectRoundTripCheckTests
{
    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla", relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void ProjectCheckProjectsRoundTripLossesIntoProblemsWithoutCountingGraphErrors()
    {
        string root = Directory.CreateTempSubdirectory("bgs-project-roundtrip").FullName;
        try
        {
            string behaviours = Path.Combine(root, "Behaviors");
            Directory.CreateDirectory(behaviours);
            string target = Path.Combine(behaviours, "Unsafe.hkx");
            File.Copy(Fixture("Meshes/Actors/Character/CharacterAssets/Hair/Female/FemaleHair04.hkx"), target);

            var result = ProjectCheck.Run(new ProjectChain { Root = root });

            var file = Assert.Single(result.Files);
            Assert.True(file.RoundTrip.HasLosses);
            Assert.True(result.RoundTripLosses > 0);
            Assert.Equal(1, result.FilesWithRoundTripLosses);
            Assert.True(file.Errors > 0); // Problems UI paints the round-trip blocker as an error.
            Assert.Equal(0, file.GraphErrors);
            Assert.Equal(0, result.Errors);
            Assert.Contains(file.Findings, finding =>
                RoundTripFindingAdapter.IsRoundTrip(finding) &&
                finding.Level == GraphValidator.Level.Error &&
                finding.BlocksSave &&
                finding.What.Contains("class this build has no definition for", StringComparison.Ordinal));
            Assert.Contains("round-trip issue", result.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
