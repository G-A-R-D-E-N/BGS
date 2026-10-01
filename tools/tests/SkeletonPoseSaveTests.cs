using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SkeletonPoseSaveTests
{
    [Fact]
    public void ReferencePoseEditCanUseTheVerifiedNativeSavePipeline()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "vanilla", "Meshes",
            "Actors", "Character", "CharacterAssets", "skeleton.hkx");
        byte[] source = File.ReadAllBytes(path);
        string before = NativeXml.From(source);
        var document = XDocument.Parse(before);
        var skeleton = document.Descendants("hkobject").First(e => (string?)e.Attribute("class") == "hkaSkeleton");
        var pose = skeleton.Elements("hkparam").Single(e => (string?)e.Attribute("name") == "referencePose");
        pose.Value = new Regex(@"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?")
            .Replace(pose.Value, "12.5", 1);

        NativeSave.Plan plan = NativeSave.Compare(before, document.ToString());

        Assert.True(plan.Possible, plan.Refusal);
        byte[] rebuilt = NativeSave.Apply(source, plan);
        SaveVerifier.Verify(source, rebuilt, plan);
        var reopened = XDocument.Parse(NativeXml.From(rebuilt));
        var savedPose = reopened.Descendants("hkobject")
            .First(e => (string?)e.Attribute("class") == "hkaSkeleton")
            .Elements("hkparam").Single(e => (string?)e.Attribute("name") == "referencePose");
        Assert.Equal("12.5", Regex.Match(savedPose.Value, @"[-+]?[\d.]+(?:[eE][-+]?\d+)?").Value);
    }
}
