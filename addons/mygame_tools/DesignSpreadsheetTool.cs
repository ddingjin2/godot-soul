#if TOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Round-trips the designer tuning JSON through a single spreadsheet, so tuning can be authored in
    /// Excel instead of a text editor. Export writes every value into one CSV; import reads that CSV
    /// back into the JSON files. CSV is used deliberately: Excel opens and saves it natively, so this
    /// needs no third-party library and nothing to install.
    /// </summary>
    /// <remarks>
    /// Unity reflected over the <c>ScriptableObject</c> the JSON deserialises into. This walks the JSON
    /// itself, because the plugin must build while the Data classes are still being ported and because
    /// the JSON is the source of truth anyway. The round trip is the same either way: only scalars that
    /// are already in the file get a row, and import starts from the file on disk, so a value with no
    /// row keeps whatever it had.
    /// </remarks>
    public static class DesignSpreadsheetTool
    {
        public const string SpreadsheetPath = DesignDataFiles.JsonFolder + "/Tuning.csv";

        private const string Header = "file,key,value";

        public static void ExportToSpreadsheet()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);

            int values = 0;
            foreach (DesignDataFiles.Entry entry in DesignDataFiles.All)
            {
                JsonObject data = LoadJson(entry.JsonPath);
                if (data == null)
                {
                    GD.PushWarning($"DesignSpreadsheetTool: {entry.JsonPath} is missing; it will not appear in the spreadsheet.");
                    continue;
                }

                foreach (KeyValuePair<string, string> pair in Flatten(data))
                {
                    sb.Append(Escape(entry.FileName)).Append(',')
                      .Append(Escape(pair.Key)).Append(',')
                      .Append(Escape(pair.Value)).Append('\n');
                    values++;
                }
            }

            // UTF-8 with BOM: without it Excel misreads the file on non-English Windows.
            File.WriteAllText(DesignDataFiles.Abs(SpreadsheetPath), sb.ToString(), new UTF8Encoding(true));
            GD.Print($"DesignSpreadsheetTool: exported {values} values to {SpreadsheetPath}. Open it in Excel, edit the value column, then run the import.");
        }

        public static void ImportFromSpreadsheet()
        {
            string csv = DesignDataFiles.Abs(SpreadsheetPath);
            if (!File.Exists(csv))
            {
                GD.PushError($"DesignSpreadsheetTool: {SpreadsheetPath} not found. Run the export first.");
                return;
            }

            Dictionary<string, Dictionary<string, string>> byFile = ReadSpreadsheet(csv);
            int written = 0;

            foreach (DesignDataFiles.Entry entry in DesignDataFiles.All)
            {
                if (!byFile.TryGetValue(entry.FileName, out Dictionary<string, string> values))
                {
                    continue;
                }

                // Start from the current JSON so a column the designer deleted keeps its old value
                // rather than silently snapping back to the C# default.
                JsonObject data = LoadJson(entry.JsonPath);
                if (data == null)
                {
                    GD.PushWarning($"DesignSpreadsheetTool: {entry.JsonPath} is missing; its spreadsheet rows were ignored. Only a programmer can create a design file.");
                    continue;
                }

                int applied = Apply(data, values, entry.FileName);
                File.WriteAllText(DesignDataFiles.Abs(entry.JsonPath), data.ToJsonString(Pretty));
                written++;
                GD.Print($"DesignSpreadsheetTool: {entry.FileName}.json updated from {applied} spreadsheet values.");
            }

            GD.Print($"DesignSpreadsheetTool: imported {written} design files from {SpreadsheetPath}. Reopen the running game to pick the numbers up - the catalog reads the JSON at load.");
        }

        private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

        private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

        private static readonly JsonDocumentOptions DocOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static JsonObject LoadJson(string resPath)
        {
            string path = DesignDataFiles.Abs(resPath);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(File.ReadAllText(path), NodeOptions, DocOptions) as JsonObject;
            }
            catch (JsonException e)
            {
                GD.PushError($"DesignSpreadsheetTool: {resPath} is not valid JSON and was skipped: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// One row per scalar. A nested object (a colour, a vector) contributes one row per member,
        /// keyed <c>field.member</c> - the shape Unity's Color/Vector2/Vector3 cases produced.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> Flatten(JsonObject data)
        {
            foreach (KeyValuePair<string, JsonNode> field in data)
            {
                // A table (the sin modifier rows) has no honest one-cell-per-row shape, and the round
                // trip already preserves it: the import starts from the current JSON and only writes
                // the scalars it finds columns for. Skipped silently so it is not reported as a fault.
                if (field.Value is JsonArray)
                {
                    continue;
                }

                if (field.Value is JsonObject nested)
                {
                    foreach (KeyValuePair<string, JsonNode> member in nested)
                    {
                        if (member.Value is JsonValue)
                        {
                            yield return new KeyValuePair<string, string>(field.Key + "." + member.Key, Raw(member.Value));
                        }
                    }

                    continue;
                }

                if (field.Value is JsonValue)
                {
                    yield return new KeyValuePair<string, string>(field.Key, Raw(field.Value));
                }
            }
        }

        /// <summary>The value as the file spells it - no reformatting, so an untouched round trip is a no-op.</summary>
        private static string Raw(JsonNode value)
        {
            return value.GetValue<JsonElement>().ValueKind == JsonValueKind.String
                ? value.GetValue<string>()
                : value.ToJsonString();
        }

        private static int Apply(JsonObject data, Dictionary<string, string> values, string fileName)
        {
            int applied = 0;

            foreach (KeyValuePair<string, string> row in values)
            {
                if (!TryFind(data, row.Key, out JsonObject owner, out string member))
                {
                    GD.PushWarning($"DesignSpreadsheetTool: {fileName} row '{row.Key}' matches no field and was ignored. Adding a new field is a programmer change.");
                    continue;
                }

                JsonNode replacement = Convert(owner[member], row.Value, row.Key);
                if (replacement == null)
                {
                    continue;
                }

                owner[member] = replacement;
                applied++;
            }

            return applied;
        }

        /// <summary>Resolves <c>field</c> or <c>field.member</c> against the file, one level deep like the export.</summary>
        private static bool TryFind(JsonObject data, string key, out JsonObject owner, out string member)
        {
            owner = null;
            member = null;

            int dot = key.IndexOf('.');
            if (dot < 0)
            {
                if (!data.ContainsKey(key) || data[key] is JsonArray or JsonObject)
                {
                    return false;
                }

                owner = data;
                member = key;
                return true;
            }

            if (data[key[..dot]] is not JsonObject nested)
            {
                return false;
            }

            string leaf = key[(dot + 1)..];
            if (!nested.ContainsKey(leaf))
            {
                return false;
            }

            owner = nested;
            member = leaf;
            return true;
        }

        /// <summary>
        /// Keeps the value's existing JSON type. A number stays a number even when Excel hands back
        /// <c>"1,5"</c>, and a string stays a string even when it happens to look numeric.
        /// </summary>
        private static JsonNode Convert(JsonNode current, string raw, string key)
        {
            JsonValueKind kind = current.GetValue<JsonElement>().ValueKind;

            switch (kind)
            {
                case JsonValueKind.String:
                    return JsonValue.Create(raw);

                case JsonValueKind.True:
                case JsonValueKind.False:
                    string flag = raw.Trim();
                    return JsonValue.Create(flag.Equals("true", StringComparison.OrdinalIgnoreCase) || flag == "1");

                default:
                    if (!TryFloat(raw, key, out double number))
                    {
                        return null;
                    }

                    // An int field stays an int: JSON has one number type but the Data classes do not,
                    // and "3.0" into an int field is a deserialisation error, not a rounding.
                    return current.ToJsonString().IndexOfAny(new[] { '.', 'e', 'E' }) < 0
                        ? JsonValue.Create((long)Math.Round(number))
                        : JsonValue.Create(number);
            }
        }

        private static bool TryFloat(string raw, string key, out double result)
        {
            result = 0d;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;

            // Invariant first, then the machine's locale, so a sheet saved by Excel under a locale
            // that writes "1,5" still imports instead of silently zeroing the value.
            if (double.TryParse(raw, styles, CultureInfo.InvariantCulture, out result))
            {
                return true;
            }

            if (double.TryParse(raw, styles, CultureInfo.CurrentCulture, out result))
            {
                return true;
            }

            GD.PushWarning($"DesignSpreadsheetTool: could not read '{raw}' for '{key}' as a number; the previous value was kept.");
            return false;
        }

        private static Dictionary<string, Dictionary<string, string>> ReadSpreadsheet(string csvPath)
        {
            var byFile = new Dictionary<string, Dictionary<string, string>>();
            string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);

            for (var i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (i == 0 && line.TrimStart('﻿').StartsWith("file,", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                List<string> cells = SplitCsvLine(line.TrimStart('﻿'));
                if (cells.Count < 3)
                {
                    GD.PushWarning($"DesignSpreadsheetTool: line {i + 1} has fewer than 3 columns and was skipped.");
                    continue;
                }

                string file = cells[0].Trim();
                string key = cells[1].Trim();
                if (file.Length == 0 || key.Length == 0)
                {
                    continue;
                }

                if (!byFile.TryGetValue(file, out Dictionary<string, string> values))
                {
                    values = new Dictionary<string, string>();
                    byFile[file] = values;
                }

                values[key] = cells[2];
            }

            return byFile;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(ch);
            }

            cells.Add(cell.ToString());
            return cells;
        }

        private static string Escape(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
#endif
