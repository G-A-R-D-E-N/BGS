using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;






public static class ProjectCheck
{
    public sealed class FileResult
    {
        public string Path = "";
        public string Name = "";
        public string Error = "";
        public readonly List<GraphValidator.Finding> Findings = new();

        public int Errors => Findings.Count(f => f.Level == GraphValidator.Level.Error);
        public int Warnings => Findings.Count(f => f.Level == GraphValidator.Level.Warning);
    }

    public sealed class Result
    {
        public readonly List<FileResult> Files = new();
        public int Errors => Files.Sum(f => f.Errors);
        public int Warnings => Files.Sum(f => f.Warnings);
        public int Unreadable => Files.Count(f => f.Error.Length > 0);

        public override string ToString()
        {
            string read = $"{Files.Count - Unreadable} of {Files.Count} behaviour files read";
            if (Files.Count == 0) return "no behaviour files were found in this project";
            return $"{read}: {Errors} error{(Errors == 1 ? "" : "s")}, " +
                   $"{Warnings} warning{(Warnings == 1 ? "" : "s")}" +
                   (Unreadable > 0 ? $", {Unreadable} could not be unpacked" : "");
        }
    }




    public static List<string> BehaviourFiles(ProjectChain chain)
    {
        var found = new List<string>();
        if (chain.Root.Length == 0) return found;

        string folder = Path.Combine(chain.Root, "Behaviors");
        if (Directory.Exists(folder))
            found.AddRange(Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories)
                                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));

        foreach (var link in chain.Links.Where(l => l.Role == "behaviour" && l.Exists))
            if (!found.Contains(link.Resolved, StringComparer.OrdinalIgnoreCase))
                found.Insert(0, link.Resolved);

        return found;
    }

    public static Result Run(ProjectChain chain, string? java = null, string? jar = null,
                             Action<string>? progress = null)
    {
        var result = new Result();
        var files = BehaviourFiles(chain);

        for (int i = 0; i < files.Count; i++)
        {
            string path = files[i];
            var file = new FileResult { Path = path, Name = Path.GetFileName(path) };
            result.Files.Add(file);
            progress?.Invoke($"{file.Name}   ({i + 1} of {files.Count})");

            try
            {
                string xml = HkxTextEdit.TextOf(path, java, jar);
                if (xml.Length == 0)
                {
                    file.Error = "holds a class this build cannot describe, and there is no hkxpack " +
                                 "to fall back on";
                    continue;
                }

                file.Findings.AddRange(GraphValidator.Check(xml, chain));
            }
            catch (Exception ex)
            {
                file.Error = ex.Message.Split('\n')[0];
            }
        }

        return result;
    }
}
