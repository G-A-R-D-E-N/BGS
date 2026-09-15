using System;
using System.IO;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void MissingGameDataFolderIsIgnored()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bgs-settings-{Guid.NewGuid():N}.cfg");
        string missing = Path.Combine(Path.GetTempPath(), $"bgs-data-missing-{Guid.NewGuid():N}");
        string? previous = Settings.SettingsPathForTest;
        try
        {
            Settings.SettingsPathForTest = path;
            File.WriteAllText(path, "gameDataFolder=" + missing + Environment.NewLine);

            Assert.Equal("", Settings.Get("gameDataFolder"));
        }
        finally
        {
            Settings.SettingsPathForTest = previous;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
