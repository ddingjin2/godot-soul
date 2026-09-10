#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Checks every design file parses, names fields the matching Data class actually has, and
    /// rewrites it pretty-printed so the folder stays diffable.
    /// </summary>
    /// <remarks>
    /// This is the port of Unity's <c>GameplayTuningJsonExporter</c>, and it runs the other way round.
    ///
    /// That tool seeded the JSON <i>from</i> the tuning ScriptableObjects, because in Unity the
    /// <c>.asset</c> files were where the numbers had originally been typed. The port has no
    /// ScriptableObjects - <c>Resources/Gameplay/*.asset</c> was not converted and the JSON is the only
    /// source of truth - so there is nothing left to seed from and the seeding direction is dead.
    ///
    /// What the seeder was really protecting is still worth having: that every design file on disk is
    /// readable by the class that loads it. So the same table, the same folder, the same
    /// missing-is-an-error reporting, pointed at validation instead. A field the Data class does not
    /// declare is the failure this catches - the loader would silently ignore it and the designer would
    /// wonder why their number does nothing.
    /// </remarks>
    public static class GameplayTuningJsonValidator
    {
        public const string SuccessMarker = "GameplayTuningJsonValidator: done.";

        private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

        private static readonly JsonDocumentOptions DocOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static void Run()
        {
            string[] fileNames = DesignDataFiles.AllFileNames();
            if (fileNames.Length == 0)
            {
                GD.PushError($"GameplayTuningJsonValidator: no JSON found in {DesignDataFiles.JsonFolder}.");
                return;
            }

            int ok = 0;
            int rewritten = 0;
            int problems = 0;
            var unmatched = new List<string>();

            foreach (string fileName in fileNames)
            {
                string resPath = DesignDataFiles.JsonFolder + "/" + fileName + ".json";
                string path = DesignDataFiles.Abs(resPath);
                string text = File.ReadAllText(path);

                JsonObject parsed;
                try
                {
                    parsed = JsonNode.Parse(text, null, DocOptions) as JsonObject;
                }
                catch (JsonException e)
                {
                    GD.PushError($"GameplayTuningJsonValidator: {fileName}.json does not parse: {e.Message}");
                    problems++;
                    continue;
                }

                if (parsed == null)
                {
                    GD.PushError($"GameplayTuningJsonValidator: {fileName}.json is valid JSON but not an object; every design file is a single object of fields.");
                    problems++;
                    continue;
                }

                string typeName = DesignDataFiles.TypeNameFor(fileName);
                Type type = DesignDataFiles.Resolve(typeName);

                if (type == null)
                {
                    // The Data class has not landed yet, or this file is not one of the tuning groups.
                    // Reported rather than passed over silently: an unrecognised file in the designer's
                    // folder is how a typo in a chapter name hides.
                    unmatched.Add(typeName == null ? fileName : $"{fileName} (no {typeName} yet)");
                }
                else
                {
                    problems += ReportUnknownFields(fileName, parsed, type);
                    ok++;
                }

                string formatted = parsed.ToJsonString(Pretty) + "\n";
                if (formatted != text)
                {
                    File.WriteAllText(path, formatted);
                    rewritten++;
                }
            }

            GD.Print($"GameplayTuningJsonValidator: checked {ok} files against their Data class, rewrote {rewritten} pretty-printed, {problems} problem(s).");
            if (unmatched.Count > 0)
            {
                GD.Print($"GameplayTuningJsonValidator: no Data class to check against for {string.Join(", ", unmatched)}.");
            }

            GD.Print(SuccessMarker);
        }

        /// <summary>
        /// A JSON key with no field behind it. The loader drops it without a word, so this is the only
        /// place a mistyped tuning name ever surfaces.
        /// </summary>
        private static int ReportUnknownFields(string fileName, JsonObject parsed, Type type)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                known.Add(field.Name);
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                known.Add(property.Name);
            }

            var problems = 0;
            foreach (KeyValuePair<string, JsonNode> pair in parsed)
            {
                if (!known.Contains(pair.Key))
                {
                    GD.PushWarning($"GameplayTuningJsonValidator: {fileName}.json has '{pair.Key}', which {type.Name} does not declare. It is being ignored at load.");
                    problems++;
                }
            }

            return problems;
        }
    }
}
#endif
