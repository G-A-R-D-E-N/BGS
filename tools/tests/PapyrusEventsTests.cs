using System;
using System.IO;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public class PapyrusEventsTests
{
    [Fact]
    public void Scan_ReadsAccessibleScripts()
    {
        string root = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "Example.psc"),
                "Event OnInit()\n    SendAnimationEvent(Self, \"JumpStart\")\nEndEvent\n");

            var index = PapyrusEvents.Scan(root);

            Assert.Equal(1, index.ScriptsRead);
            Assert.Contains("Example.psc", index.Senders("JumpStart"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Scan_DoesNotTraverseReparsePointTrees()
    {
        string root = TempDirectory();
        string outside = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "Visible.psc"),
                "SendAnimationEvent(Self, \"VisibleEvent\")");
            File.WriteAllText(Path.Combine(outside, "Hidden.psc"),
                "SendAnimationEvent(Self, \"HiddenEvent\")");

            string link = Path.Combine(root, "linked");
            try { Directory.CreateSymbolicLink(link, outside); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            var index = PapyrusEvents.Scan(root);

            Assert.Contains("Visible.psc", index.Senders("VisibleEvent"));
            Assert.Empty(index.Senders("HiddenEvent"));
        }
        finally
        {
            TryDelete(root);
            TryDelete(outside);
        }
    }

    private static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "bgs-papyrus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
