using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenCommonwealth.Services.Hkx;

public static class ProjectReport
{
    public enum Format
    {
        Html,
        Json,
        Csv,
    }

    public static Format FormatForPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => Format.Html,
            ".json" => Format.Json,
            ".csv" => Format.Csv,
            _ => throw new ArgumentException(
                "The report name must end in .html, .json or .csv.", nameof(path)),
        };

    public static string Render(ProjectChain chain, ProjectCheck.Result result, Format format) =>
        format switch
        {
            Format.Html => Html(chain, result),
            Format.Json => Json(chain, result),
            Format.Csv => Csv(chain, result),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    public static void Write(string path, ProjectChain chain, ProjectCheck.Result result)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
                           ?? throw new ArgumentException("The report path has no parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        string content = Render(chain, result, FormatForPath(fullPath));
        string staged = Path.Combine(
            directory,
            "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".writing");

        try
        {
            using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                               81920, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(staged, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(staged)) File.Delete(staged);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Json(ProjectChain chain, ProjectCheck.Result result)
    {
        var report = new
        {
            project = chain.Root,
            summary = result.ToString(),
            totals = new
            {
                files = result.Files.Count,
                readable = result.Files.Count - result.Unreadable,
                unreadable = result.Unreadable,
                errors = result.Errors,
                warnings = result.Warnings,
            },
            files = result.Files.Select(file => new
            {
                path = file.Path,
                name = file.Name,
                unreadable = file.Error.Length > 0 ? file.Error : null,
                errors = file.Errors,
                warnings = file.Warnings,
                findings = file.Findings.Select(finding => new
                {
                    severity = finding.Level == GraphValidator.Level.Error ? "error" : "warning",
                    objectId = finding.ObjectId,
                    where = finding.Where,
                    message = finding.What,
                    blocksSave = finding.BlocksSave,
                }),
            }),
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static string Csv(ProjectChain chain, ProjectCheck.Result result)
    {
        var output = new StringBuilder();
        Row(output, "project", "file", "path", "status", "severity", "object_id", "where", "message", "blocks_save");

        foreach (var file in result.Files)
        {
            if (file.Error.Length > 0)
            {
                Row(output, chain.Root, file.Name, file.Path, "unreadable", "error", "", "", file.Error, "");
                continue;
            }

            if (file.Findings.Count == 0)
            {
                Row(output, chain.Root, file.Name, file.Path, "clean", "", "", "", "", "");
                continue;
            }

            foreach (var finding in file.Findings)
                Row(output,
                    chain.Root,
                    file.Name,
                    file.Path,
                    "finding",
                    finding.Level == GraphValidator.Level.Error ? "error" : "warning",
                    finding.ObjectId,
                    finding.Where,
                    finding.What,
                    finding.BlocksSave ? "true" : "false");
        }

        return output.ToString();
    }

    private static void Row(StringBuilder output, params string[] values) =>
        output.AppendLine(string.Join(',', values.Select(CsvCell)));

    private static string CsvCell(string value)
    {
        value = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        // Spreadsheets skip leading blanks before deciding a cell is a formula, so " =X" is
        // as dangerous as "=X". A leading tab is itself a trigger in some importers.
        int lead = 0;
        while (lead < value.Length && value[lead] is ' ' or '\t') lead++;
        bool formula = value.Length > 0
                       && (value[0] is '\t'
                           || (lead < value.Length && value[lead] is '=' or '+' or '-' or '@'));

        // Quote padded cells so the blanks the neutralisation depends on survive a round trip.
        bool padded = value.Length > 0
                      && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));

        string cell = formula ? "'" + value : value;
        return padded || cell.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
            ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : cell;
    }

    private static string Html(ProjectChain chain, ProjectCheck.Result result)
    {
        static string H(string value) => WebUtility.HtmlEncode(value);

        var body = new StringBuilder();
        foreach (var file in result.Files)
        {
            if (file.Error.Length > 0)
            {
                HtmlRow(body, file.Name, file.Path, "unreadable", "error", "", "", file.Error, blocks: false);
                continue;
            }

            if (file.Findings.Count == 0)
            {
                HtmlRow(body, file.Name, file.Path, "clean", "", "", "", "", blocks: false);
                continue;
            }

            foreach (var finding in file.Findings)
                HtmlRow(body,
                        file.Name,
                        file.Path,
                        "finding",
                        finding.Level == GraphValidator.Level.Error ? "error" : "warning",
                        finding.ObjectId,
                        finding.Where,
                        finding.What,
                        finding.BlocksSave);
        }

        return """
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Behaviour Graph Studio project report</title>
              <style>
                :root { color-scheme: light dark; font-family: system-ui, sans-serif; }
                body { max-width: 1400px; margin: 2rem auto; padding: 0 1rem; }
                h1 { margin-bottom: .25rem; }
                .summary { display: flex; flex-wrap: wrap; gap: .75rem; margin: 1.25rem 0; }
                .card { border: 1px solid #7777; border-radius: .5rem; padding: .75rem 1rem; min-width: 8rem; }
                .card strong { display: block; font-size: 1.5rem; }
                table { width: 100%; border-collapse: collapse; font-size: .9rem; }
                th, td { border-bottom: 1px solid #7775; padding: .5rem; text-align: left; vertical-align: top; }
                th { position: sticky; top: 0; background: Canvas; }
                .error { font-weight: 700; }
                code { white-space: pre-wrap; overflow-wrap: anywhere; }
              </style>
            </head>
            <body>
            """ +
            $"<h1>Behaviour Graph Studio project report</h1>\n" +
            $"<p><strong>Project:</strong> <code>{H(chain.Root)}</code></p>\n" +
            $"<p>{H(result.ToString())}</p>\n" +
            "<div class=\"summary\">" +
            Card("Files", result.Files.Count) +
            Card("Readable", result.Files.Count - result.Unreadable) +
            Card("Unreadable", result.Unreadable) +
            Card("Errors", result.Errors) +
            Card("Warnings", result.Warnings) +
            "</div>\n" +
            "<table><thead><tr><th>File</th><th>Status</th><th>Severity</th><th>Object</th>" +
            "<th>Where</th><th>Message</th><th>Blocks save</th></tr></thead><tbody>\n" +
            body +
            "</tbody></table>\n</body>\n</html>\n";
    }

    private static string Card(string label, int value) =>
        $"<div class=\"card\"><span>{WebUtility.HtmlEncode(label)}</span><strong>{value}</strong></div>";

    private static void HtmlRow(StringBuilder body, string file, string path, string status,
                                string severity, string objectId, string where, string message, bool blocks)
    {
        static string H(string value) => WebUtility.HtmlEncode(value);
        body.Append("<tr")
            .Append(severity == "error" ? " class=\"error\"" : "")
            .Append("><td><strong>").Append(H(file)).Append("</strong><br><code>")
            .Append(H(path)).Append("</code></td><td>").Append(H(status))
            .Append("</td><td>").Append(H(severity))
            .Append("</td><td>").Append(H(objectId))
            .Append("</td><td><code>").Append(H(where))
            .Append("</code></td><td>").Append(H(message))
            .Append("</td><td>").Append(blocks ? "yes" : "")
            .AppendLine("</td></tr>");
    }
}
