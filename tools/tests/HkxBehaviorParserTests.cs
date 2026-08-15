using System;
using System.IO;
using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public class HkxBehaviorParserTests
{
    [Fact]
    public void ParseBehavior_RejectsMalformedSectionWithoutThrowing()
    {
        string path = TempPath();
        try
        {
            byte[] bytes = MinimalBehaviorPackfile();
            BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 0x40 + 0x14);
            File.WriteAllBytes(path, bytes);

            Assert.Null(HkxBehaviorParser.ParseBehavior(path));
            Assert.Empty(HkxBehaviorParser.LastObjects);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ParseBehavior_ClearsObjectsAfterFailedParse()
    {
        string good = TempPath();
        string bad = TempPath();
        try
        {
            File.WriteAllBytes(good, MinimalBehaviorPackfile());
            File.WriteAllBytes(bad, new byte[64]);

            var root = HkxBehaviorParser.ParseBehavior(good);
            Assert.NotNull(root);
            Assert.Equal("hkbBehaviorGraph", root!.ClassName);
            Assert.Single(HkxBehaviorParser.LastObjects);

            Assert.Null(HkxBehaviorParser.ParseBehavior(bad));
            Assert.Empty(HkxBehaviorParser.LastObjects);
        }
        finally
        {
            TryDelete(good);
            TryDelete(bad);
        }
    }

    [Fact]
    public void ParseBehavior_RejectsDuplicateLocalFixupSourcesWithoutThrowing()
    {
        AssertRejected(data => data.SetLocals(new[] { (0, 8), (0, 8) }));
    }

    [Fact]
    public void ParseBehavior_RejectsDuplicateGlobalFixupSourcesWithoutThrowing()
    {
        AssertRejected(data => data.SetGlobals(new[] { (0, 1, 8), (0, 1, 8) }));
    }

    [Fact]
    public void ParseBehavior_RejectsDuplicateVirtualFixupSourcesWithoutThrowing()
    {
        AssertRejected(data => data.AddVirtual(0, 0, 0));
    }

    [Fact]
    public void ParseBehavior_IgnoresPointerFixupThatDoesNotFitItsField()
    {
        string path = TempPath();
        try
        {
            File.WriteAllBytes(path, MinimalBehaviorPackfile(
                data => data.SetLocals(new[] { (12, 8) })));

            var root = HkxBehaviorParser.ParseBehavior(path);

            Assert.NotNull(root);
            Assert.Single(HkxBehaviorParser.LastObjects);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void AssertRejected(Action<PackfileSection> mutate)
    {
        string path = TempPath();
        try
        {
            File.WriteAllBytes(path, MinimalBehaviorPackfile(mutate));

            Assert.Null(HkxBehaviorParser.ParseBehavior(path));
            Assert.Empty(HkxBehaviorParser.LastObjects);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static byte[] MinimalBehaviorPackfile(Action<PackfileSection>? mutate = null)
    {
        var image = new PackfileImage();

        var classNames = new PackfileSection();
        Encoding.ASCII.GetBytes("__classnames__\0").CopyTo(classNames.TagBytes, 0);
        classNames.Data = Encoding.ASCII.GetBytes("hkbBehaviorGraph\0");

        var data = new PackfileSection();
        Encoding.ASCII.GetBytes("__data__\0").CopyTo(data.TagBytes, 0);
        data.Data = new byte[16];

        image.Sections.Add(classNames);
        image.Sections.Add(data);
        data.AddVirtual(0, 0, 0);
        mutate?.Invoke(data);

        return image.Rebuild();
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "bgs-parser-" + Guid.NewGuid().ToString("N") + ".hkx");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }
}
