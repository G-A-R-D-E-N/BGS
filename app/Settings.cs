using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BehaviourStudio.App;




public static class Settings
{
    public readonly record struct LayoutPoint(double X, double Y);

    internal static Func<string, string[]> ReadAllLinesForTest = File.ReadAllLines;

    private static readonly string DefaultPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BehaviourGraphStudio", "settings.cfg");

    private static readonly object WriteGate = new();
    private static readonly TimeSpan WriteLockTimeout = TimeSpan.FromSeconds(5);

    internal static string? SettingsPathForTest { get; set; }

    private static string Path => SettingsPathForTest ?? DefaultPath;

    public static string Get(string key)
    {
        if (!TryRead(out var all, out _)) return "";
        foreach (var line in all)
            if (line.Key == key) return line.Value;
        return "";
    }

    public static void Set(string key, string value)
    {
        if (!TrySet(key, value, out string failure)) throw new IOException(failure);
    }

    /// <summary>Attempts to persist one preference without allowing ordinary filesystem failures
    /// to interrupt the feature that merely wanted to remember it.</summary>
    public static bool TrySet(string key, string value, out string failure)
    {
        lock (WriteGate)
        {
            string dir;
            try
            {
                dir = System.IO.Path.GetDirectoryName(Path)!;
                Directory.CreateDirectory(dir);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                failure = e.Message.Split('\n')[0];
                return false;
            }

            using var mutex = new Mutex(false, WriteMutexName(Path));
            bool acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(WriteLockTimeout);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    failure = "another Behaviour Graph Studio process is updating preferences; try again";
                    return false;
                }

                if (!TryRead(out var all, out failure)) return false;

                all[key] = value;
                string temp = System.IO.Path.Combine(
                    dir,
                    System.IO.Path.GetFileName(Path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                WriteAll(temp, Path, all);
                failure = "";
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                failure = e.Message.Split('\n')[0];
                return false;
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
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

    private static string WriteMutexName(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) fullPath = fullPath.ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return "BehaviourGraphStudio.Settings." + hash;
    }

    /// <summary>Writes the settings to a temp file in the same directory and moves it over the
    /// final path, so a crash mid-write cannot leave a half-written settings file behind. Any
    /// failure to write or replace throws instead of passing silently.</summary>
    internal static void WriteAll(string tempPath, string finalPath, Dictionary<string, string> all)
    {
        try
        {
            using (var writer = new StreamWriter(tempPath, false))
            {
                foreach (var (k, v) in all) writer.WriteLine($"{k}={v}");
                writer.Flush();
            }
            File.Move(tempPath, finalPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool TryRead(out Dictionary<string, string> all, out string failure)
    {
        all = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (string line in ReadAllLinesForTest(Path))
            {
                int split = line.IndexOf('=');
                if (split > 0) all[line[..split]] = line[(split + 1)..];
            }
            failure = "";
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
        {
            failure = "";
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            failure = e.Message.Split('\n')[0];
            return false;
        }
    }
}
