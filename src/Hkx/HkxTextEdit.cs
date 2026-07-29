using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Hkx;

public static class HkxTextEdit
{
    public sealed class Param
    {
        public string Name = "";
        public string Value = "";
    }

    private static readonly Regex ObjectHead =
        new(@"<hkobject class=""(?<cls>[A-Za-z0-9_]+)"" name=""#(?<id>\d+)""", RegexOptions.Compiled);

    private static readonly Regex SimpleParam =
        new(@"^(?<indent>[ \t]*)<hkparam name=""(?<name>[^""]+)"">(?<value>[^<\r\n]*)</hkparam>[ \t]*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

    public static string? FindJava(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var candidates = new List<string>();
        string? home = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(home))
        {
            candidates.Add(Path.Combine(home, "bin", "java.exe"));
            candidates.Add(Path.Combine(home, "bin", "java"));
        }
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(user, ".local", "jdk", "bin", "java"));

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var exe in new[] { "java", "java.exe" })
            {
                string full = Path.Combine(dir, exe);
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }

    public static string? FindHkxPack(string configured, string projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        string[] relative =
        {
            Path.Combine("..", "..", "Tools", "FO4AnimForge", "tools", "extern", "hkxpack", "hkxpack-cli.jar"),
            Path.Combine("tools", "hkxpack-cli.jar"),
        };
        foreach (var rel in relative)
        {
            string full = Path.GetFullPath(Path.Combine(projectRoot, rel));
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public static string Unpack(string java, string jar, string hkxPath, string workDir)
    {
        Directory.CreateDirectory(workDir);
        string localHkx = Path.Combine(workDir, Path.GetFileName(hkxPath));
        File.Copy(hkxPath, localHkx, true);

        Run(java, $"-jar \"{jar}\" unpack \"{Path.GetFileName(hkxPath)}\"", workDir);

        string xml = Path.ChangeExtension(localHkx, ".xml");
        if (!File.Exists(xml))
            throw new IOException($"hkxpack produced no XML for {Path.GetFileName(hkxPath)}");
        return xml;
    }

    public static string Repack(string java, string jar, string xmlPath)
    {
        string dir = Path.GetDirectoryName(xmlPath)!;
        Run(java, $"-jar \"{jar}\" pack .", dir);

        string outHkx = Path.Combine(dir, "out", Path.GetFileNameWithoutExtension(xmlPath) + ".hkx");
        if (!File.Exists(outHkx))
            throw new IOException("hkxpack produced no .hkx; the XML was probably rejected");
        return outHkx;
    }

    public static List<string> ObjectIds(string xmlText)
    {
        var ids = new List<string>();
        foreach (Match m in ObjectHead.Matches(xmlText))
            ids.Add(m.Groups["id"].Value);
        return ids;
    }

    public static (int start, int length) ObjectBlock(string xmlText, string id)
    {
        var matches = ObjectHead.Matches(xmlText);
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i].Groups["id"].Value != id) continue;
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : xmlText.Length;
            return (start, end - start);
        }
        return (-1, 0);
    }

    public static List<Param> ReadParams(string xmlText, string id)
    {
        var result = new List<Param>();
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) return result;

        string block = xmlText.Substring(start, length);
        foreach (Match m in SimpleParam.Matches(block))
            result.Add(new Param { Name = m.Groups["name"].Value, Value = m.Groups["value"].Value });
        return result;
    }

    public static string SetParam(string xmlText, string id, string paramName, string newValue)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");

        string block = xmlText.Substring(start, length);
        bool replaced = false;

        string updated = SimpleParam.Replace(block, m =>
        {
            if (replaced || m.Groups["name"].Value != paramName) return m.Value;
            replaced = true;
            return $"{m.Groups["indent"].Value}<hkparam name=\"{paramName}\">{newValue}</hkparam>";
        });

        if (!replaced) throw new ArgumentException($"#{id} has no simple parameter named {paramName}");

        return xmlText.Substring(0, start) + updated + xmlText.Substring(start + length);
    }

    private static void Run(string exe, string args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var p = Process.Start(psi) ?? throw new IOException($"could not start {exe}");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(120000);

        if (p.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(exe)} {args} failed ({p.ExitCode})\n{stdout}\n{stderr}");
    }
}
