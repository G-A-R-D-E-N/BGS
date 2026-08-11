using System;
using System.Collections.Generic;
using System.IO;

namespace BehaviourStudio.App;




public static class Settings
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BehaviourGraphStudio", "settings.cfg");

    public static string Get(string key)
    {
        foreach (var line in Read())
            if (line.Key == key) return line.Value;
        return "";
    }

    public static void Set(string key, string value)
    {
        var all = Read();
        all[key] = value;

        string dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);
        WriteAll(System.IO.Path.Combine(dir, System.IO.Path.GetFileName(Path) + ".tmp"), Path, all);
    }

    /// <summary>Attempts to persist one preference without allowing ordinary filesystem failures
    /// to interrupt the feature that merely wanted to remember it.</summary>
    public static bool TrySet(string key, string value, out string failure)
    {
        try
        {
            Set(key, value);
            failure = "";
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            failure = e.Message.Split('\n')[0];
            return false;
        }
    }

    /// <summary>Writes the settings to a temp file in the same directory and moves it over the
    /// final path, so a crash mid-write cannot leave a half-written settings file behind. Any
    /// failure to write or replace throws instead of passing silently.</summary>
    internal static void WriteAll(string tempPath, string finalPath, Dictionary<string, string> all)
    {
        using (var writer = new StreamWriter(tempPath, false))
        {
            foreach (var (k, v) in all) writer.WriteLine($"{k}={v}");
            writer.Flush();
        }
        File.Move(tempPath, finalPath, overwrite: true);
    }

    private static Dictionary<string, string> Read()
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(Path)) return all;

        try
        {
            foreach (string line in File.ReadAllLines(Path))
            {
                int split = line.IndexOf('=');
                if (split > 0) all[line[..split]] = line[(split + 1)..];
            }
        }
        catch (IOException)
        {
        }
        return all;
    }
}
