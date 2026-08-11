using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BehaviourStudio.App;




public static class Settings
{
    public readonly record struct LayoutPoint(double X, double Y);

    internal static Func<string, string[]> ReadAllLinesForTest = File.ReadAllLines;

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

    public static IReadOnlyDictionary<string, LayoutPoint> GetGraphLayout(string path)
    {
        var result = new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);
        string encoded = Get("graph-layout." + GraphLayoutKey(path));
        if (encoded.Length == 0) return result;

        foreach (string record in encoded.Split('|'))
        {
            string[] fields = record.Split(',');
            if (fields.Length != 3) continue;
            string id;
            try { id = Uri.UnescapeDataString(fields[0]); }
            catch (UriFormatException) { continue; }
            if (id.Length == 0 || result.ContainsKey(id)) continue;
            if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.IsFinite(x) || !double.IsFinite(y)) continue;
            result[id] = new LayoutPoint(x, y);
        }
        return result;
    }

    public static bool TrySetGraphLayout(string path, IReadOnlyDictionary<string, LayoutPoint> positions,
                                         out string failure)
    {
        var records = positions
            .Where(pair => pair.Key.Length > 0 && double.IsFinite(pair.Value.X) && double.IsFinite(pair.Value.Y))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => string.Join(',', Uri.EscapeDataString(pair.Key),
                pair.Value.X.ToString("R", CultureInfo.InvariantCulture),
                pair.Value.Y.ToString("R", CultureInfo.InvariantCulture)));
        return TrySet("graph-layout." + GraphLayoutKey(path), string.Join('|', records), out failure);
    }

    private static string GraphLayoutKey(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) fullPath = fullPath.ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        string hex = Convert.ToHexString(hash);
        return OperatingSystem.IsWindows() ? hex : hex.ToLowerInvariant();
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
            foreach (string line in ReadAllLinesForTest(Path))
            {
                int split = line.IndexOf('=');
                if (split > 0) all[line[..split]] = line[(split + 1)..];
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
        }
        return all;
    }
}
