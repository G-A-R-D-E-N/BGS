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

    // Two shapes, one pass, because the order fields come back in is the order they are shown and
    // two separate matches would interleave wrongly. hkxpack writes an empty string as a
    // self closing tag, so animationBundleName and friends are only reachable through that branch.
    // Arrays are excluded for free: a numelements attribute sits between the name and the slash.
    // The \r? is not cosmetic. hkxpack writes the platform's line ending, so on Windows every line
    // ends \r\n, and .NET's multiline $ matches between the \r and the \n. Without it this matched
    // nothing on Windows: every object reported zero editable fields, and every edit that goes
    // through SetParam, which includes connecting and disconnecting nodes, failed with "no simple
    // parameter named x". Reading and drawing the graph were unaffected, so the tool looked fine.
    private static readonly Regex SimpleParam =
        new(@"^(?<indent>[ \t]*)<hkparam name=""(?<name>[^""]+)""(?:\s*/>|>(?<value>[^<\r\n]*)</hkparam>)[ \t]*\r?$",
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
        foreach (var entry in pathVar.Split(Path.PathSeparator))
        {
            // A Windows PATH entry may be quoted, and the quotes are part of the string as read.
            // Combining them into a path produces something that exists nowhere, so Java installed
            // and on PATH was still reported missing.
            string dir = entry.Trim().Trim('"');
            if (dir.Length == 0) continue;

            foreach (var exe in new[] { "java", "java.exe" })
            {
                // A malformed entry is one somebody typed, not a reason to stop looking at the rest.
                try
                {
                    string full = Path.Combine(dir, exe);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException)
                {
                }
            }
        }
        return null;
    }

    /// Why the picked file is not a usable Java, or null if it runs. A path that exists is not the
    /// same as a Java that starts, and accepting one on the strength of its name is how the tool ends
    /// up read only again on the next save with no explanation.
    public static string? WhyNotJava(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "No file was picked.";
        if (!File.Exists(path)) return $"{path} does not exist.";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var p = Process.Start(psi);
            if (p == null) return $"{Path.GetFileName(path)} would not start.";

            // java writes its version banner to stderr, not stdout.
            string banner = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(15000)) { try { p.Kill(true); } catch { } return $"{Path.GetFileName(path)} did not answer -version."; }
            if (p.ExitCode != 0) return $"{Path.GetFileName(path)} -version failed ({p.ExitCode}).";
            if (banner.IndexOf("version", StringComparison.OrdinalIgnoreCase) < 0)
                return $"{Path.GetFileName(path)} ran, but did not report a Java version. Pick java or java.exe from a JDK or JRE bin folder.";
            return null;
        }
        catch (Exception e)
        {
            return $"{Path.GetFileName(path)} could not be run: {e.Message.Split('\n')[0]}";
        }
    }

    /// The version banner of a Java known to work, for reporting back what was accepted.
    public static string JavaVersion(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            string banner = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            foreach (string line in banner.Split('\n'))
                if (line.Trim().Length > 0) return line.Trim();
        }
        catch
        {
        }
        return "";
    }

    /// An empty working directory, on a filesystem where something else may be holding a handle to
    /// the one being replaced. On Windows an antivirus scanner or the search indexer opening a file
    /// moments after it is written makes the delete fail, and it succeeds a fraction of a second
    /// later, so the only thing needed is to ask again.
    public static void ResetDirectory(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                Directory.CreateDirectory(path);
                return;
            }
            catch (Exception e) when (attempt < 4 && (e is IOException or UnauthorizedAccessException))
            {
                System.Threading.Thread.Sleep(150);
            }
        }
    }

    /// Why the file cannot be written, in words that say what to do about it, or null if it can.
    /// Checked before packing rather than after, so a refusal costs nothing.
    public static string? WhyNotWritable(string path)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                return $"{Path.GetFileName(path)} is marked read only. Clear it in the file's " +
                       "Properties, or run  attrib -r  on it, and save again.";

            using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return $"Windows will not let this program write {Path.GetFileName(path)}. It is either " +
                   "read only or owned by another account; check the file's Properties.";
        }
        catch (IOException)
        {
            return $"{Path.GetFileName(path)} is open in another program. Close Fallout 4, the mod " +
                   "manager, or whatever else is holding it, and save again.";
        }
    }

    // Set by the app to the directory the executable sits in. An exported build has no project
    // directory to search, and res:// cannot be globalized once it is inside the binary, so the
    // bundled jar is only findable relative to the executable.
    public static string AppDirectory = "";

    public static string? FindHkxPack(string configured, string projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        string[] relative =
        {
            Path.Combine("tools", "hkxpack-cli.jar"),
        };
        foreach (var root in new[] { AppDirectory, projectRoot })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var rel in relative)
            {
                string full = Path.GetFullPath(Path.Combine(root, rel));
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }

    /// Reads unpacked XML with one line ending everywhere. Every edit in here splices in text of its
    /// own, so a file that is half CRLF and half LF is what a mixed read produces, and the regexes
    /// that put it back together have to agree with what is already in the string.
    public static string ReadXml(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");

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
            string body = newValue.Length == 0
                ? $"<hkparam name=\"{paramName}\"/>"
                : $"<hkparam name=\"{paramName}\">{newValue}</hkparam>";
            return m.Groups["indent"].Value + body;
        });

        if (!replaced) throw new ArgumentException($"#{id} has no simple parameter named {paramName}");

        return xmlText.Substring(0, start) + updated + xmlText.Substring(start + length);
    }

    public static string ClassOf(string xmlText, string id)
    {
        foreach (Match m in ObjectHead.Matches(xmlText))
            if (m.Groups["id"].Value == id) return m.Groups["cls"].Value;
        return "";
    }

    public static List<string> IdsOfClass(string xmlText, string className)
    {
        var ids = new List<string>();
        foreach (Match m in ObjectHead.Matches(xmlText))
            if (m.Groups["cls"].Value == className) ids.Add(m.Groups["id"].Value);
        return ids;
    }

    public static string AddObject(string xmlText, string className, string signature,
                                   string innerXml, out string newId)
    {
        int highest = 0;
        foreach (Match m in ObjectHead.Matches(xmlText))
            if (int.TryParse(m.Groups["id"].Value, out int n) && n > highest) highest = n;
        newId = (highest + 1).ToString();

        int close = xmlText.LastIndexOf("</hksection>", StringComparison.Ordinal);
        if (close < 0) throw new InvalidOperationException("no </hksection> in this file");

        string block =
            $"        <hkobject class=\"{className}\" name=\"#{newId}\" signature=\"{signature}\">\n" +
            innerXml.TrimEnd('\n') + "\n" +
            "        </hkobject>\n";

        return xmlText.Substring(0, close) + block + xmlText.Substring(close);
    }

    // An array param is either <hkparam name="x" numelements="0"/> when empty, or
    // <hkparam name="x" numelements="N"> ... </hkparam>. Both shapes have to be handled.
    public static string ArrayAppend(string xmlText, string id, string paramName, string elementXml)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");
        string block = xmlText.Substring(start, length);

        var empty = new Regex($"<hkparam name=\"{Regex.Escape(paramName)}\" numelements=\"0\"\\s*/>");
        var mEmpty = empty.Match(block);
        if (mEmpty.Success)
        {
            string replacement =
                $"<hkparam name=\"{paramName}\" numelements=\"1\">\n{elementXml.TrimEnd('\n')}\n            </hkparam>";
            block = block.Remove(mEmpty.Index, mEmpty.Length).Insert(mEmpty.Index, replacement);
            return xmlText.Substring(0, start) + block + xmlText.Substring(start + length);
        }

        var open = new Regex($"<hkparam name=\"{Regex.Escape(paramName)}\" numelements=\"(?<n>\\d+)\">");
        var mOpen = open.Match(block);
        if (!mOpen.Success) throw new ArgumentException($"#{id} has no array parameter named {paramName}");

        int count = int.Parse(mOpen.Groups["n"].Value);
        int endTag = ArrayBodyEnd(block, mOpen.Index + mOpen.Length);
        if (endTag < 0) throw new InvalidOperationException($"#{id}.{paramName} is not closed");

        block = block.Insert(endTag, elementXml.TrimEnd('\n') + "\n            ");
        block = open.Replace(block, $"<hkparam name=\"{paramName}\" numelements=\"{count + 1}\">", 1);

        return xmlText.Substring(0, start) + block + xmlText.Substring(start + length);
    }

    public static string ArrayInsertAt(string xmlText, string id, string paramName, int index, string elementXml)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");
        string block = xmlText.Substring(start, length);

        var open = new Regex($"<hkparam name=\"{Regex.Escape(paramName)}\" numelements=\"(?<n>\\d+)\">");
        var mOpen = open.Match(block);
        if (!mOpen.Success)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return ArrayAppend(xmlText, id, paramName, elementXml);
        }

        int count = int.Parse(mOpen.Groups["n"].Value);
        if (index < 0 || index > count) throw new ArgumentOutOfRangeException(nameof(index));

        int bodyStart = mOpen.Index + mOpen.Length;
        int bodyEnd = ArrayBodyEnd(block, bodyStart);
        if (bodyEnd < 0) throw new InvalidOperationException($"#{id}.{paramName} is not closed");

        var elements = SplitElements(block.Substring(bodyStart, bodyEnd - bodyStart));
        if (elements.Count != count)
            throw new InvalidOperationException(
                $"#{id}.{paramName} says {count} elements but {elements.Count} were found; refusing to edit");

        elements.Insert(index, elementXml.TrimEnd('\n'));
        string newBody = "\n" + string.Join("\n", elements) + "\n            ";

        block = block.Remove(bodyStart, bodyEnd - bodyStart).Insert(bodyStart, newBody);
        block = open.Replace(block, $"<hkparam name=\"{paramName}\" numelements=\"{count + 1}\">", 1);

        return xmlText.Substring(0, start) + block + xmlText.Substring(start + length);
    }

    public static string ArrayRemoveAt(string xmlText, string id, string paramName, int index)
    {
        var (start, length) = ObjectBlock(xmlText, id);
        if (start < 0) throw new ArgumentException($"object #{id} not found");
        string block = xmlText.Substring(start, length);

        var open = new Regex($"<hkparam name=\"{Regex.Escape(paramName)}\" numelements=\"(?<n>\\d+)\">");
        var mOpen = open.Match(block);
        if (!mOpen.Success) throw new ArgumentException($"#{id} has no populated array named {paramName}");

        int count = int.Parse(mOpen.Groups["n"].Value);
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));

        int bodyStart = mOpen.Index + mOpen.Length;
        int bodyEnd = ArrayBodyEnd(block, bodyStart);
        if (bodyEnd < 0) throw new InvalidOperationException($"#{id}.{paramName} is not closed");
        string body = block.Substring(bodyStart, bodyEnd - bodyStart);

        var elements = SplitElements(body);
        if (elements.Count != count)
            throw new InvalidOperationException(
                $"#{id}.{paramName} says {count} elements but {elements.Count} were found; refusing to edit");

        elements.RemoveAt(index);
        string newBody = elements.Count == 0 ? "\n            " : "\n" + string.Join("\n", elements) + "\n            ";

        block = block.Remove(bodyStart, bodyEnd - bodyStart).Insert(bodyStart, newBody);
        block = open.Replace(block, $"<hkparam name=\"{paramName}\" numelements=\"{count - 1}\">", 1);

        return xmlText.Substring(0, start) + block + xmlText.Substring(start + length);
    }

    // Array elements contain their own <hkparam> children, so the first </hkparam> after the array's
    // opening tag belongs to an element, not to the array. Match by depth or edits land inside the
    // first element and hkxpack rejects the file.
    private static int ArrayBodyEnd(string block, int bodyStart)
    {
        var tag = new Regex(@"<hkparam\b[^>]*?(?<selfclose>/)?>|</hkparam>");
        int depth = 0;
        foreach (Match m in tag.Matches(block, bodyStart))
        {
            if (m.Value.StartsWith("</"))
            {
                if (depth == 0) return m.Index;
                depth--;
            }
            else if (!m.Groups["selfclose"].Success)
            {
                depth++;
            }
        }
        return -1;
    }

    private static List<string> SplitElements(string body)
    {
        var result = new List<string>();
        int depth = 0, from = -1;
        var tag = new Regex(@"<(/?)hkobject\b[^>]*>");
        foreach (Match m in tag.Matches(body))
        {
            bool closing = m.Groups[1].Value == "/";
            if (!closing)
            {
                if (depth == 0) from = m.Index;
                depth++;
            }
            else
            {
                depth--;
                if (depth == 0 && from >= 0)
                {
                    result.Add(body.Substring(from, m.Index + m.Length - from));
                    from = -1;
                }
            }
        }
        if (result.Count > 0) return result;

        foreach (Match m in Regex.Matches(body, @"<hkcstring>.*?</hkcstring>", RegexOptions.Singleline))
            result.Add(m.Value);
        if (result.Count > 0) return result;

        // A reference array is bare whitespace separated tokens, e.g. "#93 #97 #197", with no tags at
        // all. Without this an element count check sees zero and refuses the edit.
        foreach (Match m in Regex.Matches(body, @"(#\d+|null)"))
            result.Add("                " + m.Value);
        return result;
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
