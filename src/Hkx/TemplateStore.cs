using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OpenCommonwealth.Services.Hkx;

public static class TemplateStore
{
    public sealed record Template(string Slug, string Name, string Note, string FromFile,
                                  int RootId, string RootClass, int Objects,
                                  IReadOnlyList<string> Events, IReadOnlyList<string> Variables)
    {
        public override string ToString() =>
            $"{Name} ({RootClass}, {Objects} object(s)" +
            (Events.Count + Variables.Count > 0 ? $", needs {Events.Count + Variables.Count} symbol(s)" : "") +
            ")";
    }

    public sealed record Fit(IReadOnlyList<string> Events, IReadOnlyList<string> Variables)
    {
        public bool Fits => Events.Count == 0 && Variables.Count == 0;

        public override string ToString()
        {
            if (Fits) return "everything this needs is already declared";

            var parts = new List<string>();
            if (Events.Count > 0) parts.Add($"{Events.Count} event(s): {string.Join(", ", Events)}");
            if (Variables.Count > 0) parts.Add($"{Variables.Count} variable(s): {string.Join(", ", Variables)}");
            return "declare " + string.Join("; and ", parts);
        }
    }

    public static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BehaviourGraphStudio", "templates");

    private static readonly object WriteGate = new();
    private static readonly TimeSpan WriteLockTimeout = TimeSpan.FromSeconds(5);

    internal static Action? BeforeDescriptionPublishForTest { get; set; }
    internal static Action? BeforeSourceStageForTest { get; set; }

    public static List<Template> All()
    {
        var found = new List<Template>();
        if (!Directory.Exists(Folder)) return found;

        foreach (string path in Directory.GetFiles(Folder, "*.template").OrderBy(f => f, StringComparer.Ordinal))
        {
            var one = ReadDescription(path);
            if (one != null && File.Exists(SourceOf(one.Slug))) found.Add(one);
        }

        return found.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Template? Get(string slug) => All().FirstOrDefault(t => t.Slug == slug);

    public static Template Lift(string sourcePath, int rootId, string name, string note = "",
                                HavokClassTypes? types = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("A template needs a name.");

        byte[] sourceBytes = InputFilePolicy.ReadHkx(sourcePath);
        var image = PackfileImage.Read(sourceBytes);
        var tree = NativePaste.Of(image, rootId, types);

        if (tree.Shared.Count > 0)
            throw new InvalidOperationException(
                $"#{rootId} shares {tree.Shared.Count} object(s) with the rest of the file it is in, " +
                "so it cannot be lifted out of it. Something outside this shape points at those " +
                "objects, most often a generator another state also uses, and a template has no way " +
                "to carry them. Pick a shape that owns everything below it: " +
                string.Join(", ", tree.Shared.Take(8).Select(id => "#" + id)) +
                (tree.Shared.Count > 8 ? ", and more" : "") + ".");

        var objects = new PackfileObjects(image, HavokClasses.Shipped);
        int rootIndex = rootId - NativeGraphModel.FirstId;
        if (rootIndex < 0 || rootIndex >= objects.Instances.Count)
            throw new InvalidOperationException($"#{rootId} is not an object in this file.");

        string rootClass = objects.Instances[rootIndex].ClassName;
        string slug = Slug(name);
        var template = new Template(slug, name.Trim(), note.Trim(), Path.GetFileName(sourcePath),
                                    rootId, rootClass, tree.Ids.Count, tree.Events, tree.Variables);

        string folder = Path.GetFullPath(Folder);
        Directory.CreateDirectory(folder);
        BeforeSourceStageForTest?.Invoke();
        Publish(sourceBytes, folder, template);
        return template;
    }

    private static void Publish(byte[] sourceBytes, string folder, Template template)
    {
        string description = Path.Combine(folder, template.Slug + ".template");
        string source = Path.Combine(folder, template.Slug + ".hkx");

        lock (WriteGate)
        {
            using var mutex = new Mutex(false, WriteMutexName(description));
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(WriteLockTimeout); }
                catch (AbandonedMutexException) { acquired = true; }

                if (!acquired)
                    throw new IOException(
                        $"Another Behaviour Graph Studio process is saving template '{template.Name}'. Try again.");

                if (File.Exists(description))
                {
                    var existing = ReadDescription(description);
                    throw new InvalidOperationException(
                        existing == null
                            ? $"A damaged template description already exists for slug '{template.Slug}'. " +
                              $"Move or remove {Path.GetFileName(description)} before saving this template again."
                            : $"A template already exists under the name '{existing.Name}' " +
                              $"(slug '{template.Slug}'), so this one was not saved. " +
                              "Give it a name that normalizes differently.");
                }

                PublishUnderLock(sourceBytes, source, description, template);
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }
    }

    private static void PublishUnderLock(byte[] sourceBytes, string source, string description,
                                         Template template)
    {
        string token = Guid.NewGuid().ToString("N");
        string stagedSource = source + "." + token + ".tmp";
        string stagedDescription = description + "." + token + ".tmp";
        bool sourcePublished = false;

        try
        {
            StageBytes(sourceBytes, stagedSource);
            WriteDescription(template, stagedDescription);

            File.Move(stagedSource, source, overwrite: true);
            sourcePublished = true;

            BeforeDescriptionPublishForTest?.Invoke();
            File.Move(stagedDescription, description, overwrite: false);
        }
        catch
        {
            if (sourcePublished && !File.Exists(description)) TryDelete(source);
            throw;
        }
        finally
        {
            TryDelete(stagedSource);
            TryDelete(stagedDescription);
        }
    }

    private static void StageBytes(byte[] source, string staged)
    {
        using var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                          81920, FileOptions.WriteThrough);
        output.Write(source);
        output.Flush(flushToDisk: true);
    }

    public static Fit Against(Template template, string targetPath)
    {
        var target = new PackfileObjects(PackfileImage.Read(targetPath), HavokClasses.Shipped);
        return Against(template, target);
    }

    public static Fit Against(Template template, PackfileObjects target)
    {
        var events = Declared(target, "eventNames");
        var variables = Declared(target, "variableNames");

        return new Fit(
            template.Events.Where(n => !events.Contains(n)).ToList(),
            template.Variables.Where(n => !variables.Contains(n)).ToList());
    }

    public static NativePaste.Result Apply(Template template, string targetPath, int attachToId = -1,
                                           string attachField = "", HavokClassTypes? types = null) =>
        Apply(template, InputFilePolicy.ReadHkx(targetPath), targetPath,
              attachToId, attachField, types);

    public static NativePaste.Result Apply(Template template, byte[] targetBytes, string targetPath,
                                           int attachToId = -1, string attachField = "",
                                           HavokClassTypes? types = null)
    {
        string source = SourceOf(template.Slug);
        if (!File.Exists(source))
            throw new InvalidOperationException(
                $"The file '{template.Name}' was lifted out of is missing from the template folder, " +
                "so there is nothing to copy. Make the template again.");

        var targetObjects = new PackfileObjects(PackfileImage.Read(targetBytes), HavokClasses.Shipped);
        var fit = Against(template, targetObjects);
        if (!fit.Fits)
            throw new InvalidOperationException(
                $"'{template.Name}' uses {fit.Events.Count + fit.Variables.Count} symbol(s) this file " +
                $"does not declare, so nothing was added. On the symbols tab, {fit}, then apply it again.");

        byte[] templateBytes = InputFilePolicy.ReadHkx(source);
        var tree = NativePaste.Of(PackfileImage.Read(templateBytes), template.RootId, types);
        return NativePaste.Paste(
            targetBytes, targetPath, templateBytes, new NativePaste.Clip(source, tree),
            attachToId, attachField, types);
    }

    public static bool Remove(string slug)
    {
        string description = Path.Combine(Folder, slug + ".template");
        if (!File.Exists(description)) return false;

        File.Delete(description);
        if (File.Exists(SourceOf(slug))) File.Delete(SourceOf(slug));
        return true;
    }

    private static NativePaste.Subtree TreeOf(Template template, string source, HavokClassTypes? types) =>
        NativePaste.Of(PackfileImage.Read(source), template.RootId, types);

    private static string SourceOf(string slug) => Path.Combine(Folder, slug + ".hkx");

    private static HashSet<string> Declared(PackfileObjects objects, string field)
    {
        var strings = objects.OfClass("hkbBehaviorGraphStringData").FirstOrDefault();
        var names = strings == null ? null : objects.ReadStringArray(strings, field);

        return names == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : names.Where(n => n != null).Select(n => n!).ToHashSet(StringComparer.Ordinal);
    }

    public static string Slug(string name)
    {
        var text = new StringBuilder();
        foreach (char c in name.Trim())
            text.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');

        string slug = text.ToString();
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');

        return slug.Length == 0 ? "template" : slug;
    }

    private static void WriteDescription(Template t, string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                          4096, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                                            4096, leaveOpen: true);
        writer.WriteLine("name=" + Encode(t.Name));
        writer.WriteLine("note=" + Encode(t.Note));
        writer.WriteLine("from=" + Encode(t.FromFile));
        writer.WriteLine("root=" + t.RootId);
        writer.WriteLine("class=" + t.RootClass);
        writer.WriteLine("objects=" + t.Objects);
        writer.WriteLine("events=" + string.Join("\x1f", t.Events.Select(Encode)));
        writer.WriteLine("variables=" + string.Join("\x1f", t.Variables.Select(Encode)));
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    public static string Encode(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\x1f", "\\u", StringComparison.Ordinal);

    public static string Decode(string text)
    {
        var built = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length) { built.Append(text[i]); continue; }

            char next = text[++i];
            built.Append(next switch
            {
                'r' => '\r',
                'n' => '\n',
                'u' => '\x1f',
                _ => next,
            });
        }
        return built.ToString();
    }

    private static Template? ReadDescription(string path)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path))
            {
                int at = line.IndexOf('=');
                if (at > 0) values[line[..at]] = line[(at + 1)..];
            }

            string Raw(string key) => values.TryGetValue(key, out string? v) ? v : "";
            string Value(string key) => Decode(Raw(key));
            List<string> List(string key) =>
                Raw(key).Split('\x1f', StringSplitOptions.RemoveEmptyEntries).Select(Decode).ToList();

            string name = Value("name");
            string rootClass = Value("class");
            bool validRoot = int.TryParse(Raw("root"), out int root) &&
                             root >= NativeGraphModel.FirstId;
            bool validObjects = int.TryParse(Raw("objects"), out int objects) && objects > 0;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rootClass) ||
                !validRoot || !validObjects)
                return null;

            return new Template(Path.GetFileNameWithoutExtension(path), name, Value("note"),
                                Value("from"), root, rootClass, objects,
                                List("events"), List("variables"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private static string WriteMutexName(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) fullPath = fullPath.ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return "BehaviourGraphStudio.Template." + hash;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
