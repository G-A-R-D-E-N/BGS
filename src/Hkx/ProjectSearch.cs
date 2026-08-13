using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class ProjectSearch
{
    public const int DefaultResultLimit = 5000;

    public sealed record Hit(
        string Path,
        string File,
        string Kind,
        string ObjectId,
        string ClassName,
        string Field,
        string Value);

    public sealed record Problem(string Path, string File, string Error);

    public sealed class Result
    {
        public readonly List<Hit> Hits = new();
        public readonly List<Problem> Problems = new();
        public int FilesFound;
        public int FilesRead;
        public bool Truncated;

        public int FilesUnreadable => Problems.Count;

        public override string ToString()
        {
            string files = $"{FilesRead} of {FilesFound} behaviour files searched";
            string matches = $"{Hits.Count} match{(Hits.Count == 1 ? "" : "es")}";
            string unreadable = FilesUnreadable > 0 ? $", {FilesUnreadable} unreadable" : "";
            string truncated = Truncated ? $", stopped at {Hits.Count}" : "";
            return $"{files}: {matches}{unreadable}{truncated}";
        }
    }

    public static Result Run(
        ProjectChain chain,
        string query,
        int resultLimit = DefaultResultLimit,
        Action<string>? progress = null,
        Func<string, BehaviourGraphModel?>? modelReader = null)
    {
        var result = new Result();
        string needle = query.Trim();
        if (needle.Length == 0 || resultLimit <= 0) return result;

        List<string> files;
        try
        {
            files = ProjectCheck.BehaviourFiles(chain);
        }
        catch (Exception error)
        {
            result.Problems.Add(new Problem(chain.Root, Path.GetFileName(chain.Root),
                                            error.Message.Split('\n')[0]));
            return result;
        }

        result.FilesFound = files.Count;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int index = 0; index < files.Count && !result.Truncated; index++)
        {
            string path = files[index];
            string file = Path.GetFileName(path);
            progress?.Invoke($"{file}   ({index + 1} of {files.Count})");

            BehaviourGraphModel? model;
            try
            {
                model = modelReader == null ? Read(path) : modelReader(path);
                if (model == null)
                {
                    result.Problems.Add(new Problem(
                        path, file, "holds a class this build cannot describe"));
                    continue;
                }
            }
            catch (Exception error)
            {
                result.Problems.Add(new Problem(path, file, error.Message.Split('\n')[0]));
                continue;
            }

            result.FilesRead++;
            if (Matches(file, needle) || Matches(path, needle))
                Add(result, seen, resultLimit,
                    new Hit(path, file, "file", "", "", "path", path));

            foreach (var obj in model.Objects)
            {
                if (result.Truncated) break;

                if (Matches("#" + obj.Id, needle))
                    Add(result, seen, resultLimit,
                        new Hit(path, file, "object", obj.Id, obj.Class, "id", "#" + obj.Id));
                if (Matches(obj.Class, needle))
                    Add(result, seen, resultLimit,
                        new Hit(path, file, "class", obj.Id, obj.Class, "class", obj.Class));

                foreach (var (field, value) in obj.Scalars)
                {
                    if (!Matches(field, needle) && !Matches(value, needle)) continue;
                    Add(result, seen, resultLimit,
                        new Hit(path, file, Kind(field), obj.Id, obj.Class, field, Display(value)));
                    if (result.Truncated) break;
                }

                foreach (var (field, values) in obj.Lists)
                {
                    for (int valueIndex = 0; valueIndex < values.Count && !result.Truncated; valueIndex++)
                    {
                        string value = values[valueIndex];
                        if (!Matches(field, needle) && !Matches(value, needle)) continue;
                        Add(result, seen, resultLimit,
                            new Hit(path, file, Kind(field), obj.Id, obj.Class,
                                    $"{field}[{valueIndex}]", Display(value)));
                    }
                }

                foreach (var (field, values) in obj.Structs)
                {
                    foreach (var (member, value) in values)
                    {
                        string address = field + "." + member;
                        if (!Matches(address, needle) && !Matches(value, needle)) continue;
                        Add(result, seen, resultLimit,
                            new Hit(path, file, Kind(address), obj.Id, obj.Class,
                                    address, Display(value)));
                        if (result.Truncated) break;
                    }
                    if (result.Truncated) break;
                }

                foreach (var (field, elements) in obj.StructLists)
                {
                    for (int element = 0; element < elements.Count && !result.Truncated; element++)
                        foreach (var (member, value) in elements[element])
                        {
                            string address = $"{field}[{element}].{member}";
                            if (!Matches(address, needle) && !Matches(value, needle)) continue;
                            Add(result, seen, resultLimit,
                                new Hit(path, file, Kind(address), obj.Id, obj.Class,
                                        address, Display(value)));
                            if (result.Truncated) break;
                        }
                }
            }
        }

        return result;
    }

    private static BehaviourGraphModel? Read(string path)
    {
        string xml = HkxTextEdit.TextOf(path);
        return xml.Length == 0 ? null : BehaviourGraphModel.Parse(xml);
    }

    private static bool Matches(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string Kind(string field)
    {
        if (field.Contains("event", StringComparison.OrdinalIgnoreCase)) return "event";
        if (field.Contains("variable", StringComparison.OrdinalIgnoreCase)) return "variable";
        if (field.Contains("animation", StringComparison.OrdinalIgnoreCase)
            || field.Contains("behaviorFilename", StringComparison.OrdinalIgnoreCase)
            || field.Contains("rigName", StringComparison.OrdinalIgnoreCase)) return "asset";
        if (field.Equals("name", StringComparison.OrdinalIgnoreCase)
            || field.EndsWith(".name", StringComparison.OrdinalIgnoreCase)) return "name";
        return "field";
    }

    private static string Display(string value)
    {
        const int limit = 500;
        string oneLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= limit ? oneLine : oneLine[..limit] + "…";
    }

    private static void Add(Result result, HashSet<string> seen, int limit, Hit hit)
    {
        if (result.Hits.Count >= limit)
        {
            result.Truncated = true;
            return;
        }

        string key = string.Join("\n", hit.Path, hit.Kind, hit.ObjectId, hit.Field, hit.Value);
        if (!seen.Add(key)) return;
        result.Hits.Add(hit);
    }
}
