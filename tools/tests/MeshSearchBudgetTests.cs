using System;
using System.IO;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class MeshSearchBudgetTests
{
    [Fact]
    public void NonMeshEntriesCountTowardTheSearchLimit()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgs-mesh-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 4097; i++)
                File.WriteAllText(Path.Combine(root, i + ".txt"), "");

            var result = MeshLookup.Find(Path.Combine(root, "animation.hkx"), null, null);

            Assert.False(result.Found);
            Assert.Contains("search limit", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
