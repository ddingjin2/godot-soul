using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// Unity's <c>PlayerPrefs</c>, backed by a Godot <c>ConfigFile</c> in the user data folder.
    /// Kept API-compatible so the seven ported files that read settings and save data did not have to
    /// change shape.
    /// </summary>
    public static class PlayerPrefs
    {
        private const string SavePath = "user://playerprefs.cfg";
        private const string Section = "prefs";

        private static ConfigFile _config;

        private static ConfigFile Config
        {
            get
            {
                if (_config == null)
                {
                    _config = new ConfigFile();
                    _config.Load(SavePath); // A missing file just leaves the config empty.
                }

                return _config;
            }
        }

        public static bool HasKey(string key) => Config.HasSectionKey(Section, key);

        public static int GetInt(string key, int defaultValue = 0) =>
            Config.GetValue(Section, key, defaultValue).AsInt32();

        public static void SetInt(string key, int value) => Config.SetValue(Section, key, value);

        public static float GetFloat(string key, float defaultValue = 0f) =>
            (float)Config.GetValue(Section, key, defaultValue).AsDouble();

        public static void SetFloat(string key, float value) => Config.SetValue(Section, key, value);

        public static string GetString(string key, string defaultValue = "") =>
            Config.GetValue(Section, key, defaultValue).AsString();

        public static void SetString(string key, string value) => Config.SetValue(Section, key, value);

        public static void DeleteKey(string key)
        {
            if (HasKey(key))
            {
                Config.EraseSectionKey(Section, key);
            }
        }

        public static void DeleteAll()
        {
            _config = new ConfigFile();
            Save();
        }

        public static void Save() => Config.Save(SavePath);
    }

    /// <summary>
    /// Unity's <c>JsonUtility</c>, over <c>System.Text.Json</c>. The design JSON files name plain
    /// public fields, so field serialisation is on and matching is case-insensitive.
    /// </summary>
    public static class JsonData
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            IncludeFields = true,

            // Case-SENSITIVE, like Unity's JsonUtility, and for a reason beyond fidelity: the ported
            // data classes keep the Unity field (chapterColor) and add a Godot-style property over it
            // (ChapterColor). Case-insensitive matching folds those two onto one JSON name, and
            // System.Text.Json rejects that outright while it is still building the type's contract -
            // before any modifier below could remove the property. The design JSON already spells every
            // key exactly as its field, because Unity matched case-sensitively too.
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { FieldsOnly } },
            Converters =
            {
                new Vector2Converter(),
                new Vector3Converter(),
                new ColorConverter(),
            },
        };

        /// <summary>
        /// Unity's <c>JsonUtility</c> serialised **fields only**, never properties, and the design JSON
        /// is written against that. Keeping to it here drops the Godot-style properties the ported data
        /// classes added over their fields, and every inherited <c>Godot.Resource</c> member with them,
        /// so a round trip writes back the same shape it read.
        /// </summary>
        private static void FieldsOnly(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
            {
                if (typeInfo.Properties[i].AttributeProvider is not FieldInfo)
                {
                    typeInfo.Properties.RemoveAt(i);
                }
            }
        }

        /// <summary>Returns null (or default) when the text is empty or malformed, like JsonUtility's lenient path.</summary>
        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, Options);
            }
            catch (JsonException e)
            {
                GD.PushWarning($"JsonData: could not parse {typeof(T).Name}: {e.Message}");
                return default;
            }
        }

        public static string ToJson(object value) => JsonSerializer.Serialize(value, Options);

        /// <summary>Overwrites the public fields of an existing object, like <c>JsonUtility.FromJsonOverwrite</c>.</summary>
        public static void FromJsonOverwrite<T>(string json, T target) where T : class
        {
            T parsed = FromJson<T>(json);
            if (parsed == null || target == null)
            {
                return;
            }

            foreach (System.Reflection.FieldInfo field in typeof(T).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                field.SetValue(target, field.GetValue(parsed));
            }
        }
    }

    /// <summary>
    /// Unity's <c>Resources.Load</c>. The Unity project addressed everything by a path relative to a
    /// <c>Resources</c> folder with no extension; the same call shape works here against
    /// <c>res://Resources/</c>.
    /// </summary>
    public static class Res
    {
        public const string Root = "res://Resources/";

        /// <summary>Reads a design/tuning JSON file - <c>Res.LoadJson&lt;T&gt;("Design/PlayerMovement")</c>.</summary>
        public static T LoadJson<T>(string path)
        {
            string text = LoadText(path, ".json");
            return text == null ? default : JsonData.FromJson<T>(text);
        }

        /// <summary>Raw text of a resource file. Returns null when it is missing, so callers can fall back to defaults.</summary>
        public static string LoadText(string path, string extension = "")
        {
            string full = Root + path + extension;
            if (!FileAccess.FileExists(full))
            {
                return null;
            }

            using FileAccess file = FileAccess.Open(full, FileAccess.ModeFlags.Read);
            return file?.GetAsText();
        }

        /// <summary>A texture, scene or other imported asset. Returns null rather than throwing when absent.</summary>
        public static T Load<T>(string path, string extension = "") where T : class
        {
            string full = Root + path + extension;
            return ResourceLoader.Exists(full) ? ResourceLoader.Load(full) as T : null;
        }

        /// <summary>Every file directly inside a resource folder, sorted by name - the frame lists depend on the order.</summary>
        public static string[] ListFiles(string folder, string extension = ".png")
        {
            using DirAccess dir = DirAccess.Open(Root + folder);
            if (dir == null)
            {
                return Array.Empty<string>();
            }

            var found = new System.Collections.Generic.List<string>();
            foreach (string name in dir.GetFiles())
            {
                // Godot appends .import to assets it has processed; the real file keeps its own name.
                string clean = name.EndsWith(".import") ? name[..^".import".Length] : name;
                if (clean.EndsWith(extension) && !found.Contains(clean))
                {
                    found.Add(clean);
                }
            }

            found.Sort(string.CompareOrdinal);
            return found.ToArray();
        }
    }

    /// <summary>
    /// Godot's vector and colour structs spell their members <c>X</c>/<c>Y</c> and <c>R</c>/<c>G</c>,
    /// while Unity's <c>JsonUtility</c> wrote them lowercase - and the design JSON is full of
    /// <c>{"x": 13, "y": -1}</c>. Case-insensitive matching would bridge that, but it cannot be turned
    /// on globally: it also folds each data class's Unity field onto the Godot-style property beside it
    /// and System.Text.Json rejects the pair outright. So the three struct shapes get explicit
    /// converters instead, and everything else stays case-sensitive, exactly as Unity was.
    ///
    /// A missing member reads as 0 rather than failing, which is what JsonUtility did with a partial
    /// object - a layout file that names only <c>x</c> still means "y is zero".
    /// </summary>
    internal sealed class Vector2Converter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions options)
        {
            var value = new Vector2();
            foreach ((string name, float number) in VectorJson.ReadMembers(ref reader))
            {
                switch (name)
                {
                    case "x": value.X = number; break;
                    case "y": value.Y = number; break;
                }
            }

            return value;
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteEndObject();
        }
    }

    /// <summary>Unity <c>Vector3</c>. The port keeps it only where the JSON still writes a z.</summary>
    internal sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions options)
        {
            var value = new Vector3();
            foreach ((string name, float number) in VectorJson.ReadMembers(ref reader))
            {
                switch (name)
                {
                    case "x": value.X = number; break;
                    case "y": value.Y = number; break;
                    case "z": value.Z = number; break;
                }
            }

            return value;
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteNumber("z", value.Z);
            writer.WriteEndObject();
        }
    }

    /// <summary>Unity <c>Color</c>: four 0..1 floats, and an absent alpha means opaque, not invisible.</summary>
    internal sealed class ColorConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions options)
        {
            var value = new Color(0f, 0f, 0f, 1f);
            foreach ((string name, float number) in VectorJson.ReadMembers(ref reader))
            {
                switch (name)
                {
                    case "r": value.R = number; break;
                    case "g": value.G = number; break;
                    case "b": value.B = number; break;
                    case "a": value.A = number; break;
                }
            }

            return value;
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("r", value.R);
            writer.WriteNumber("g", value.G);
            writer.WriteNumber("b", value.B);
            writer.WriteNumber("a", value.A);
            writer.WriteEndObject();
        }
    }

    /// <summary>Shared reader for the three struct converters above.</summary>
    internal static class VectorJson
    {
        public static System.Collections.Generic.List<(string Name, float Value)> ReadMembers(ref Utf8JsonReader reader)
        {
            var members = new System.Collections.Generic.List<(string, float)>(4);
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return members;
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string name = reader.GetString()?.ToLowerInvariant();
                if (!reader.Read())
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.Number)
                {
                    members.Add((name, reader.GetSingle()));
                }
                else
                {
                    reader.Skip();
                }
            }

            return members;
        }
    }
}
