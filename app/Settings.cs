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
    internal const int MaxRememberedGraphLayouts = 256;
    private const string GraphLayoutPrefix = "graph-layout.";

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
        return all.TryGetValue(key, out string? value) ? value : "";
    }

    public static void Set(string key, string value)
    {
        if (!TrySet(key, value, out string failure)) throw new IOException(failure);
    }

    public static bool TrySet(string key, string value, out string failure) =>
        TryMutate(all => all.Set(key, value), out failure);

    public static IReadOnlyDictionary<string, LayoutPoint> GetGraphLayout(string path)
    {
        var result = new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);
        string encoded = Get(GraphLayoutPrefix + GraphLayoutKey(path));
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
        string key = GraphLayoutPrefix + GraphLayoutKey(path);
        string value = string.Join('|', records);

        return TryMutate(all =>
        {
            all.Set(key, value, moveToEnd: true);

            var layouts = all.Keys
                .Where(k => k.StartsWith(GraphLayoutPrefix, StringComparison.Ordinal))
                .ToList();
            int remove = layouts.Count - MaxRememberedGraphLayouts;
            for (int i = 0; i < remove; i++) all.Remove(layouts[i]);
        }, out failure);
    }

    private static bool TryMutate(Action<StoredSettings> mutate, out string failure)
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
                try { acquired = mutex.WaitOne(WriteLockTimeout); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    failure = "another Behaviour Graph Studio process is updating preferences; try again";
                    return false;
                }

                if (!TryRead(out var all, out failure)) return false;
                mutate(all);
                string temp = System.IO.Path.Combine(
                    dir, System.IO.Path.GetFileName(Path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
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

    internal static void WriteAll(string tempPath, string finalPath, Dictionary<string, string> all) =>
        WriteAll(tempPath, finalPath, StoredSettings.From(all));

    private static void WriteAll(string tempPath, string finalPath, StoredSettings all)
    {
        try
        {
            using (var writer = new StreamWriter(tempPath, false))
            {
                foreach (var (key, value) in all.Ordered()) writer.WriteLine($"{key}={value}");
                writer.Flush();
            }
            File.Move(tempPath, finalPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }

    private static bool TryRead(out StoredSettings all, out string failure)
    {
        all = new StoredSettings();
        try
        {
            foreach (string line in ReadAllLinesForTest(Path))
            {
                int split = line.IndexOf('=');
                if (split > 0)
                    all.Set(line[..split], line[(split + 1)..], moveToEnd: true);
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

    private sealed class StoredSettings
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly List<string> _order = new();

        public IEnumerable<string> Keys => _order;

        public bool TryGetValue(string key, out string? value) =>
            _values.TryGetValue(key, out value);

        public void Set(string key, string value, bool moveToEnd = false)
        {
            bool existed = _values.ContainsKey(key);
            _values[key] = value;

            if (!existed)
            {
                _order.Add(key);
                return;
            }

            if (!moveToEnd) return;
            _order.Remove(key);
            _order.Add(key);
        }

        public bool Remove(string key)
        {
            if (!_values.Remove(key)) return false;
            _order.Remove(key);
            return true;
        }

        public IEnumerable<KeyValuePair<string, string>> Ordered()
        {
            foreach (string key in _order)
                if (_values.TryGetValue(key, out string? value))
                    yield return new KeyValuePair<string, string>(key, value);
        }

        public static StoredSettings From(Dictionary<string, string> values)
        {
            var stored = new StoredSettings();
            foreach (var (key, value) in values) stored.Set(key, value);
            return stored;
        }
    }
}
