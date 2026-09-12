using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

[Collection("ScanCli")]
public sealed class HeadlessRoundTripScanTests
{
    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla", relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void ScanArchivesReportsSaveUnsafeFileSeparatelyFromCorruption()
    {
        string root = Directory.CreateTempSubdirectory("bgs-roundtrip-scan").FullName;
        try
        {
            string destination = Path.Combine(root, "Meshes", "Actors", "Character", "CharacterAssets", "Hair", "FemaleHair04.hkx");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Fixture("Meshes/Actors/Character/CharacterAssets/Hair/Female/FemaleHair04.hkx"), destination);

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            TextWriter oldOut = Console.Out;
            TextWriter oldError = Console.Error;
            int exit;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                exit = ModlistScan.Run(new[] { "--scan-archives", root, "--json" });
            }
            finally
            {
                Console.SetOut(oldOut);
                Console.SetError(oldError);
            }

            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(stdout.ToString());
            var json = document.RootElement;
            Assert.Equal(0, json.GetProperty("summary").GetProperty("corrupt").GetInt32());
            Assert.True(json.GetProperty("summary").GetProperty("roundTripLosses").GetInt32() > 0);

            var findings = json.GetProperty("findings").EnumerateArray().ToList();
            var roundTrip = Assert.Single(findings.Where(f => f.GetProperty("kind").GetString() == "roundtrip-loss"));
            Assert.Equal("loose", roundTrip.GetProperty("source").GetString());
            Assert.Contains("FemaleHair04.hkx", roundTrip.GetProperty("path").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(roundTrip.GetProperty("details").EnumerateArray(), detail =>
                detail.GetString()!.Contains("UnsupportedClass", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void ScanArchivesKeepsCleanFileAtZeroRoundTripLosses()
    {
        string root = Directory.CreateTempSubdirectory("bgs-roundtrip-clean").FullName;
        try
        {
            string destination = Path.Combine(root, "Meshes", "Actors", "Character", "Behaviors", "SingleAnimFurniture.hkx");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"), destination);

            var stdout = new StringWriter();
            TextWriter oldOut = Console.Out;
            TextWriter oldError = Console.Error;
            int exit;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(TextWriter.Null);
                exit = ModlistScan.Run(new[] { "--scan-archives", root, "--json" });
            }
            finally
            {
                Console.SetOut(oldOut);
                Console.SetError(oldError);
            }

            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.Equal(0, document.RootElement.GetProperty("summary").GetProperty("roundTripLosses").GetInt32());
            Assert.DoesNotContain(document.RootElement.GetProperty("findings").EnumerateArray(),
                finding => finding.GetProperty("kind").GetString() == "roundtrip-loss");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
