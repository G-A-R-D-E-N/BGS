using System;
using System.Collections.Generic;
using System.IO;

namespace BehaviourStudio.App;

// Where the last opened path and any hand-set java or hkxpack location live. Deliberately a plain
// key=value file in the user's config directory: it is meant to be readable and correctable by
// hand when a path is wrong, which is the only reason anyone opens it.
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
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            using var writer = new StreamWriter(Path, false);
            foreach (var (k, v) in all) writer.WriteLine($"{k}={v}");
        }
        catch (IOException)
        {
            // A read-only or missing config directory must not stop the tool opening a file.
        }
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
