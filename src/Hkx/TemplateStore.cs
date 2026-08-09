using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

// Keeping a shape so it can be used again, which is what a template is here.
//
// A template is a subtree lifted out of a real behaviour and kept. It is not a shape described in
// some format of our own: nobody has designed one of those, and the machinery to copy a subtree
// correctly already exists in `NativePaste`. So a template is a copy that outlives the session that
// made it.
//
// What it keeps is the whole source file, not the subtree on its own. That looks wasteful and is not:
// a behaviour averages 20 KB, and `NativePaste.Into` wants a source image to read the subtree out of,
// so keeping the file means the paste that a template performs is the same paste that was already
// proved over the corpus rather than a second implementation of it. Writing a packfile containing
// only the subtree would be a new writer with its own faults.
//
// Two things stop a subtree leaving its file, and both are checked here rather than at the point of
// use:
//
//   * It can share an object with the rest of the file it came from. A paste into a different file
//     refuses that outright, because there is nothing there for the shared pointer to name. Counted
//     over the corpus with `symrm template`, this is the common case for the shape this feature most
//     wants: 3,624 of 5,320 state infos share something, against 23 of 3,740 clip generators. So the
//     refusal has to come when the template is made, not when it is used, or people would keep
//     templates that can never be applied anywhere.
//
//   * It can use an event or variable by name that the file it lands in does not declare. That is not
//     a reason to refuse the template, because it depends on where it is going rather than on the
//     template itself, and the same template is fine in one file and not in another. 2,251 of the
//     3,717 liftable clip subtrees use at least one symbol, so this is the ordinary case rather than
//     the exception, and it is reported ahead of time by `Missing` so it reads as "declare these two
//     first" instead of as a failure.
//
// Nothing vanilla is shipped with the tool. A template is made by the person using it, out of their
// own game files, and lives in their own application data. There are no Bethesda assets in this
// repository and templates must not become the way some arrive.
public static class TemplateStore
{
    /// One kept shape, and everything needed to say what it is without opening it.
    ///
    /// `Events` and `Variables` are the names the subtree uses, recorded when it was lifted, so a
    /// window can say what a template will need before anybody applies it.
    public sealed record Template(string Slug, string Name, string Note, string FromFile,
                                  int RootId, string RootClass, int Objects,
                                  IReadOnlyList<string> Events, IReadOnlyList<string> Variables)
    {
        public override string ToString() =>
            $"{Name} ({RootClass}, {Objects} object(s)" +
            (Events.Count + Variables.Count > 0 ? $", needs {Events.Count + Variables.Count} symbol(s)" : "") +
            ")";
    }

    /// What a template would need before it could be applied to a particular file.
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

    /// Where templates are kept. Settable so a check can use a directory of its own rather than the
    /// one the person running it has been filling up.
    public static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BehaviourGraphStudio", "templates");

    /// Every template on disk, by name. A folder that is not there yet is an empty list rather than
    /// an error, because that is what it means before anybody has made one.
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

    /// Lifts a subtree out of a file and keeps it.
    ///
    /// Refuses a subtree that shares an object with the rest of its file, because such a subtree can
    /// never be pasted into a different file and a template that can only be used where it came from
    /// is not a template. The message names the shared objects, since they are the thing to go and
    /// look at: usually the root is a state whose generator some other state also uses.
    public static Template Lift(string sourcePath, int rootId, string name, string note = "",
                                HavokClassTypes? types = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("A template needs a name.");

        var image = PackfileImage.Read(sourcePath);
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
        string rootClass = objects.Instances[rootId - NativeGraphModel.FirstId].ClassName;

        string slug = Slug(name);
        Directory.CreateDirectory(Folder);

        // The source file is copied rather than referenced. A template that pointed at a path would
        // die the moment that file moved, and would silently change meaning if it were edited.
        File.Copy(sourcePath, SourceOf(slug), overwrite: true);

        var template = new Template(slug, name.Trim(), note.Trim(), Path.GetFileName(sourcePath),
                                    rootId, rootClass, tree.Ids.Count, tree.Events, tree.Variables);
        WriteDescription(template);
        return template;
    }

    /// What the target file would still need before this template could go into it.
    ///
    /// Answered without changing anything, so a window can show it beside the template list rather
    /// than only after somebody has tried and been refused.
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

    /// Puts a template into a file and returns the file's new bytes.
    ///
    /// The paste itself is `NativePaste`, unchanged: a template is a copy that was kept, so applying
    /// one has to be the same operation as pasting one and not a parallel implementation of it.
    /// `attachToId` and `attachField` say where the new root hangs, and leaving them out puts it in
    /// unattached, which the checker already reports and a person can wire up on the canvas.
    public static NativePaste.Result Apply(Template template, string targetPath, int attachToId = -1,
                                           string attachField = "", HavokClassTypes? types = null)
    {
        string source = SourceOf(template.Slug);
        if (!File.Exists(source))
            throw new InvalidOperationException(
                $"The file '{template.Name}' was lifted out of is missing from the template folder, " +
                "so there is nothing to copy. Make the template again.");

        // Checked here as well as offered by `Against`, because `Apply` can be reached without
        // anybody having looked, and the message that names what to declare is worth more than the
        // one that comes back from the paste.
        var fit = Against(template, targetPath);
        if (!fit.Fits)
            throw new InvalidOperationException(
                $"'{template.Name}' uses {fit.Events.Count + fit.Variables.Count} symbol(s) this file " +
                $"does not declare, so nothing was added. On the symbols tab, {fit}, then apply it again.");

        return NativePaste.Paste(targetPath, new NativePaste.Clip(source, TreeOf(template, source, types)),
                                 attachToId, attachField, types);
    }

    /// Forgets a template. Its copy of the source file goes with it.
    public static bool Remove(string slug)
    {
        string description = Path.Combine(Folder, slug + ".template");
        if (!File.Exists(description)) return false;

        File.Delete(description);
        if (File.Exists(SourceOf(slug))) File.Delete(SourceOf(slug));
        return true;
    }

    /// The subtree as it stands in the kept copy.
    ///
    /// Worked out again rather than trusting the numbers recorded when the template was made. Those
    /// were true of the file it was lifted from, and the kept copy is that same file, so they should
    /// agree; asking again is what makes that a fact rather than an assumption.
    private static NativePaste.Subtree TreeOf(Template template, string source, HavokClassTypes? types) =>
        NativePaste.Of(PackfileImage.Read(source), template.RootId, types);

    private static string SourceOf(string slug) => Path.Combine(Folder, slug + ".hkx");

    /// The names a file declares, which is what decides whether a template fits it.
    private static HashSet<string> Declared(PackfileObjects objects, string field)
    {
        var strings = objects.OfClass("hkbBehaviorGraphStringData").FirstOrDefault();
        var names = strings == null ? null : objects.ReadStringArray(strings, field);

        return names == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : names.Where(n => n != null).Select(n => n!).ToHashSet(StringComparer.Ordinal);
    }

    /// A file name that is safe everywhere and still recognisable, so the folder can be read by a
    /// person and corrected by hand the same way the settings file can.
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

    // A plain key=value file, for the same reason the settings file is one: it is meant to be
    // readable and correctable by hand, and a template folder somebody cannot understand is a
    // template folder they will not trust.
    //
    // Names are escaped before they go in. Two vanilla event names carry a literal carriage return,
    // and a name holding one would end its own line and take the rest of the description with it, so
    // the file would parse into something that looked fine and was wrong. The list separator is a
    // unit separator rather than a comma because a name may contain a comma and may not contain that.
    private static void WriteDescription(Template t)
    {
        using var writer = new StreamWriter(Path.Combine(Folder, t.Slug + ".template"), false);
        writer.WriteLine("name=" + Encode(t.Name));
        writer.WriteLine("note=" + Encode(t.Note));
        writer.WriteLine("from=" + Encode(t.FromFile));
        writer.WriteLine("root=" + t.RootId);
        writer.WriteLine("class=" + t.RootClass);
        writer.WriteLine("objects=" + t.Objects);
        writer.WriteLine("events=" + string.Join("", t.Events.Select(Encode)));
        writer.WriteLine("variables=" + string.Join("", t.Variables.Select(Encode)));
    }

    /// Makes a name safe to keep on one line of a key=value file, and puts it back exactly.
    ///
    /// Only the four characters that would break the file are touched, so an ordinary name is stored
    /// as itself and the folder stays readable. The backslash goes first on the way out and last on
    /// the way back, or a name containing one would be un-escaped twice.
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
                Raw(key).Split('', StringSplitOptions.RemoveEmptyEntries).Select(Decode).ToList();

            return new Template(Path.GetFileNameWithoutExtension(path), Value("name"), Value("note"),
                                Value("from"), int.TryParse(Raw("root"), out int r) ? r : -1,
                                Value("class"), int.TryParse(Raw("objects"), out int o) ? o : 0,
                                List("events"), List("variables"));
        }
        catch (Exception)
        {
            // A description that cannot be read is left out of the list rather than throwing, so one
            // corrupt file does not take the whole folder with it.
            return null;
        }
    }
}
