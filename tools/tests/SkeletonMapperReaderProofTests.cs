using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public class SkeletonMapperReaderProofTests
{
    [Fact]
    public void VanillaSkeletonMapperIsAvailableThroughReusableCoreReader()
    {
        var objects = new PackfileObjects(PackfileImage.Read(FixturePath()));
        var mapper = Assert.Single(
            SkeletonMapperReader.Read(objects),
            candidate => candidate.SkeletonA.BoneNames.Count == 95 && candidate.SkeletonB.BoneNames.Count == 18);

        Assert.Equal(0, mapper.MappingType);
        Assert.Equal(95, mapper.SkeletonA.BoneNames.Count);
        Assert.Equal(18, mapper.SkeletonB.BoneNames.Count);
        Assert.Equal(18, mapper.SimpleMappings.Count);
        Assert.NotEqual(mapper.SkeletonAObjectId, mapper.SkeletonBObjectId);

        foreach (var row in mapper.SimpleMappings)
        {
            Assert.InRange(row.BoneA, 0, mapper.SkeletonA.BoneNames.Count - 1);
            Assert.InRange(row.BoneB, 0, mapper.SkeletonB.BoneNames.Count - 1);
        }
    }

    private static string FixturePath()
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "vanilla",
            "Meshes",
            "Actors",
            "Character",
            "CharacterAssets",
            "skeleton.hkx");
    }
}
