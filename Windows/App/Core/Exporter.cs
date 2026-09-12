using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeartRateMonitor.Core;

/// <summary>
/// Data export: export SQLite records as JSON, YAML, or CSV, or copy the .db directly.
/// Writes to the exports/ directory and returns an absolute path.
/// </summary>
public static class Exporter
{
    /// <summary>Supported format identifiers.</summary>
    public static readonly string[] Formats = { "json", "yaml", "csv", "sqlite" };

    /// <summary>Formats supported for log export; sqlite is excluded because logs are not stored in the database.</summary>
    public static readonly string[] LogFormats = { "txt", "json", "yaml", "csv" };

    /// <summary>Export the most recent limit rows from a table: hr_records, osc_records, health_records, or variables.</summary>
    public static (bool ok, string file, string error) Export(string table, string format, int limit)
    {
        format = (format ?? "json").ToLowerInvariant();
        if (!Formats.Contains(format)) return (false, "", $"不支持的格式: {format}");
        try
        {
            var dir = Path.Combine(App.BaseDir, "exports");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (format == "sqlite")
            {
                var dst = Path.Combine(dir, $"hrm_{stamp}.db");
                File.Copy(HrmDb.PathOf(), dst, overwrite: true);
                App.Log.Info(LogText.L("log.export.db_done", dst));
                return (true, dst, "");
            }

            var rows = HrmDb.Recent(table, limit);
            var file = Path.Combine(dir, $"{table}_{stamp}.{format}");
            var text = format switch
            {
                "json" => ToJson(rows),
                "yaml" => ToYaml(rows),
                _ => ToCsv(rows),
            };
            File.WriteAllText(file, text, new UTF8Encoding(false));
            App.Log.Info(LogText.L("log.export.done", table, rows.Count, format, file));
            return (true, file, "");
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.export.fail", table, format, e.Message));
            return (false, "", e.Message);
        }
    }

    /// <summary>Export caller-filtered log lines. txt writes lines unchanged; other formats use structured time, level, and message columns.</summary>
    public static (bool ok, string file, string error) ExportLogs(List<string> lines, string format)
    {
        format = (format ?? "txt").ToLowerInvariant();
        if (!LogFormats.Contains(format)) return (false, "", $"不支持的格式: {format}");
        try
        {
            var dir = Path.Combine(App.BaseDir, "exports");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"hrm_log_{DateTime.Now:yyyyMMdd_HHmmss}.{format}");
            var text = format switch
            {
                "txt" => string.Join(Environment.NewLine, lines),
                "json" => ToJson(SplitLogs(lines)),
                "yaml" => ToYaml(SplitLogs(lines)),
                _ => ToCsv(SplitLogs(lines)),
            };
            File.WriteAllText(file, text, new UTF8Encoding(false));
            App.Log.Info(LogText.L("log.export.logs_done", lines.Count, format, file));
            return (true, file, "");
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.export.logs_fail", format, e.Message));
            return (false, "", e.Message);
        }
    }

    /// <summary>Split "[HH:mm:ss][LEVEL] msg" into three columns; unmatched lines go entirely into message.</summary>
    static List<Dictionary<string, object?>> SplitLogs(List<string> lines)
    {
        var rows = new List<Dictionary<string, object?>>(lines.Count);
        foreach (var line in lines)
        {
            string time = "", level = "", msg = line;
            if (line.StartsWith('[') && line.IndexOf(']') is var t && t > 0)
            {
                time = line[1..t];
                var rest = line[(t + 1)..];
                if (rest.StartsWith('[') && rest.IndexOf(']') is var l && l > 0)
                {
                    level = rest[1..l];
                    msg = rest[(l + 1)..].TrimStart();
                }
                else msg = rest.TrimStart();
            }
            rows.Add(new Dictionary<string, object?> { ["time"] = time, ["level"] = level, ["message"] = msg });
        }
        return rows;
    }

    /// <summary>Export a Monitor report: JSON/YAML use a structured object; CSV/TXT flatten it into readable text.</summary>
    public static (bool ok, string file, string error) ExportReport(object report, string format)
    {
        format = (format ?? "json").ToLowerInvariant();
        if (!LogFormats.Contains(format)) return (false, "", $"不支持的格式: {format}");
        try
        {
            var dir = Path.Combine(App.BaseDir, "exports");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"hrm_report_{DateTime.Now:yyyyMMdd_HHmmss}.{format}");
            // Reports are nested objects that CSV/TXT cannot express directly, so flatten them into key=value rows first.
            var text = format switch
            {
                "json" => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                "yaml" => ToYaml(Flatten(report)),
                "csv" => ToCsv(Flatten(report)),
                _ => string.Join(Environment.NewLine,
                        Flatten(report).Select(r => $"{r["key"]} = {r["value"]}")),
            };
            File.WriteAllText(file, text, new UTF8Encoding(false));
            App.Log.Info(LogText.L("log.export.report_done", format, file));
            return (true, file, "");
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.export.report_fail", format, e.Message));
            return (false, "", e.Message);
        }
    }

    /// <summary>Flatten a nested object into key/value columns, using dots and indices in keys to represent hierarchy.</summary>
    static List<Dictionary<string, object?>> Flatten(object report)
    {
        var rows = new List<Dictionary<string, object?>>();
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(report));
        Walk("", doc.RootElement);
        return rows;

        void Walk(string prefix, JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                        Walk(prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}", p.Value);
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in el.EnumerateArray()) Walk($"{prefix}[{i++}]", item);
                    break;
                default:
                    rows.Add(new Dictionary<string, object?> { ["key"] = prefix, ["value"] = el.ToString() });
                    break;
            }
        }
    }

    static string ToJson(List<Dictionary<string, object?>> rows) =>
        JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Minimal YAML serialization; row records contain only scalar fields, so a full YAML implementation is unnecessary.</summary>
    static string ToYaml(List<Dictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            var first = true;
            foreach (var kv in row)
            {
                sb.Append(first ? "- " : "  ").Append(kv.Key).Append(": ").AppendLine(YamlScalar(kv.Value));
                first = false;
            }
            if (first) sb.AppendLine("- {}");
        }
        return sb.ToString();
    }

    static string YamlScalar(object? v)
    {
        if (v == null) return "null";
        if (v is bool b) return b ? "true" : "false";
        if (v is long or int or double or float or decimal)
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "0";
        var s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        // Quote and escape values containing special characters.
        if (s.Length == 0 || s.IndexOfAny(new[] { ':', '#', '\n', '"', '\'', '{', '}', '[', ']', ',' }) >= 0)
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        return s;
    }

    static string ToCsv(List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return "";
        var cols = rows[0].Keys.ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", cols.Select(CsvCell)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", cols.Select(c => CsvCell(Convert.ToString(row.GetValueOrDefault(c), CultureInfo.InvariantCulture) ?? ""))));
        return sb.ToString();
    }

    static string CsvCell(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
