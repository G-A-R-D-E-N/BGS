using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

/// <summary>
/// Save-fidelity regression over a corpus of committed Fallout 4 vanilla behaviour and
/// character files (see fixtures/vanilla/README.md for provenance and selection).
///
/// Every fixture is opened and re-saved with an empty edit plan — the exact serialization
/// the app writes for an unchanged document (<see cref="NativeSave.Apply"/>) — and the
/// result is checked against the manifest's per-file fidelity contract:
///
///   byte       — the no-op save must be byte-identical to the source. Default for canonical
///                FO4 packfiles; a regression here means the save path re-lays something it
///                should leave alone.
///   structural — a legal rebuild re-lays layout (e.g. FixupOrder strips 0xFF filler from
///                fixup tables), so bytes differ but the object graph must be preserved:
///                same class names, same instances, clean signatures, identical XML.
///   refused    — the save path refuses the file cleanly (e.g. classes this build does not
///                define). The test asserts the refusal is an explicit exception and that no
///                bytes come out of the save.
/// </summary>
public sealed class SaveFidelityCorpusTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla");

    private static string ManifestRoot => Path.Combine(AppContext.BaseDirectory, "app.Config");

    private sealed class Manifest
    {
        public List<ManifestFile> Files { get; set; } = new();
    }

    private sealed class ManifestFile
    {
        public string Path { get; set; } = "";
        public string Source { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Bytes { get; set; }
        public string Fidelity { get; set; } = "";
        public string Note { get; set; } = "";
    }

    private static Manifest ReadManifest()
    {
        // The per-file contracts live at tools/tests/app.Config/manifest.json (cached like
        // other config files); fixtures/vanilla/manifest.json is the checked-in copy. Keep
        // them in lockstep so the metadata a reader sees is the one the test enforces. It is
        // also how the corpus can be validated before the fixture binaries are copied in.
        string cached = File.ReadAllText(Path.Combine(ManifestRoot, "manifest.json"));
        Assert.Equal(
            File.ReadAllText(Path.Combine(FixturesRoot, "manifest.json")),
            cached);
        string json = cached;
        return JsonSerializer.Deserialize<Manifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("the fidelity manifest did not parse");
    }

    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        foreach (var entry in ReadManifest().Files) data.Add(entry.Path);
        return data;
    }

    [Fact]
    public void EveryFixtureIsListedInTheManifest()
    {
        var manifest = ReadManifest().Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var onDisk = Directory.GetFiles(FixturesRoot, "*.hkx", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(FixturesRoot, p).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Subset(onDisk, manifest);
        Assert.Subset(manifest, onDisk);
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void NoOpSaveIsFidelityFaithful(string relative)
    {
        string fixture = Path.Combine(FixturesRoot, relative);
        var entry = ReadManifest().Files.Single(f => f.Path == relative);

        byte[] source = File.ReadAllBytes(fixture);
        Assert.Equal(entry.Bytes, source.LongLength);
        Assert.Equal(entry.Sha256, Convert.ToHexString(SHA256.HashData(source)));

        byte[]? saved = null;
        Exception? refusal = null;
        try
        {
            saved = NativeSave.Apply(source, new NativeSave.Plan(new List<NativeSave.Change>(), null));
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
        {
            refusal = e;
        }

        switch (entry.Fidelity)
        {
            case "refused":
                Assert.True(refusal != null,
                    $"{relative} is marked refused but the no-op save produced {saved?.Length} bytes; " +
                    "either the refusal was fixed (downgrade the manifest entry) or the save path " +
                    "started accepting unsupported classes (regression).");
                return;

            case "byte":
                Assert.Null(refusal);
                Assert.True(saved!.SequenceEqual(source),
                    $"{relative} did not round-trip byte-identically (first difference at " +
                    $"0x{FirstDifference(source, saved):x}); the manifest says 'byte', so the save " +
                    "path re-laid something it should leave alone.");
                return;

            case "structural":
                Assert.Null(refusal);
                AssertStructural(source, saved!);
                return;

            default:
                Assert.Fail($"{relative} has unknown fidelity '{entry.Fidelity}' in the manifest");
                return;
        }
    }

    private static void AssertStructural(byte[] source, byte[] saved)
    {
        var sourceObjects = new PackfileObjects(PackfileImage.Read(source));
        var savedObjects = new PackfileObjects(PackfileImage.Read(saved));

        Assert.Equal(sourceObjects.ClassNames(), savedObjects.ClassNames());
        Assert.Equal(sourceObjects.Instances.Count, savedObjects.Instances.Count);
        Assert.Empty(HavokClassTypes.Shipped.SignatureProblems(savedObjects.ClassNames()));
        Assert.Equal(NativeXml.From(source), NativeXml.From(saved));
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        int limit = Math.Min(a.Length, b.Length);
        for (int i = 0; i < limit; i++)
            if (a[i] != b[i]) return i;
        return limit;
    }
}
