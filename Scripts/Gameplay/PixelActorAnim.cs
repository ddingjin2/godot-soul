using System.Collections.Generic;
using System.Globalization;
using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Where the pixel frames live, and how <c>anim.json</c> is read. One load per actor key, cached
    /// for the session.
    /// </summary>
    /// <remarks>
    /// The convention is <c>Resources/PixelActors/&lt;ActorKey&gt;/&lt;state&gt;/&lt;nn&gt;.png</c> with
    /// a sibling <c>anim.json</c> of the shape
    /// <c>{"ppu": 34, "states": {"idle": {"fps": 6, "loop": true}}}</c>.
    /// <para>
    /// Parsed by hand rather than with <c>JsonData</c>: the states object is keyed by state name and a
    /// field-mapping deserialiser has no dictionary. The shipped manifests also start with a UTF-8 BOM,
    /// which a strict parser rejects outright and a scanner does not notice.
    /// </para>
    /// <para>
    /// Unity loaded <c>Sprite</c>s through <c>Resources.LoadAll</c>; Godot imports each PNG as its own
    /// <see cref="Texture2D"/>, so a clip is a texture list and <c>Res.ListFiles</c> supplies the frame
    /// order. That order is the whole contract - the names are zero-padded and <c>ListFiles</c> sorts
    /// ordinal, which is why nothing here re-sorts them.
    /// </para>
    /// </remarks>
    public static class PixelActorAnim
    {
        public const string ResourceFolder = "PixelActors/";
        public const string IdleState = "idle";
        public const float DefaultPixelsPerUnit = 32f;
        public const float DefaultFps = 8f;

        /// <summary>State names probed when an actor ships frames but no <c>anim.json</c>.</summary>
        private static readonly string[] FallbackStates = { "idle", "run", "attack", "jump", "hurt", "death" };

        public sealed class Clip
        {
            public Texture2D[] Frames;
            public float Fps;
            public bool Loop;
        }

        public sealed class ClipSet
        {
            /// <summary>
            /// The frame pixels that make one Unity metre. Godot imports a PNG at 1:1 pixels, so this is
            /// also what <see cref="SpriteFrameAnimator"/> turns into the sprite's scale:
            /// <c>World.Ppu / PixelsPerUnit</c>.
            /// </summary>
            public float PixelsPerUnit = DefaultPixelsPerUnit;

            public Dictionary<string, Clip> Clips = new(System.StringComparer.OrdinalIgnoreCase);

            public bool HasFrames => Clips.Count > 0;
        }

        public struct StateEntry
        {
            public string Name;
            public float Fps;
            public bool Loop;
        }

        private static readonly Dictionary<string, ClipSet> Cache = new();
        private static readonly ClipSet Empty = new();

        /// <summary>
        /// Every frame this actor key has, or an empty set. An empty set is the normal answer while the
        /// folder is unauthored, and the caller is expected to keep the vector body in that case.
        /// </summary>
        public static ClipSet Load(string actorKey)
        {
            if (string.IsNullOrEmpty(actorKey))
                return Empty;

            if (Cache.TryGetValue(actorKey, out ClipSet cached))
                return cached;

            var set = new ClipSet();
            string folder = ResourceFolder + actorKey + "/";

            string text = Res.LoadText(folder + "anim", ".json");
            List<StateEntry> states;
            if (text != null)
            {
                set.PixelsPerUnit = ParsePixelsPerUnit(text);
                states = ParseStates(text);
            }
            else
            {
                // Frames can land before the manifest does. Probe the usual names at a safe default
                // rather than calling the actor unauthored.
                states = new List<StateEntry>();
                foreach (string name in FallbackStates)
                    states.Add(new StateEntry { Name = name, Fps = DefaultFps, Loop = IsLoopingByDefault(name) });
            }

            foreach (StateEntry entry in states)
            {
                Texture2D[] frames = LoadFrames(folder + entry.Name);
                if (frames.Length == 0)
                    continue;

                set.Clips[entry.Name] = new Clip
                {
                    Frames = frames,
                    Fps = entry.Fps > 0f ? entry.Fps : DefaultFps,
                    Loop = entry.Loop,
                };
            }

            Cache[actorKey] = set;
            return set;
        }

        /// <summary>
        /// Every PNG directly inside <paramref name="stateFolder"/>, in filename order. The convention is
        /// "frames are in filename order" and <c>Res.ListFiles</c> sorts ordinal, which the zero-padded
        /// names make sufficient - so there is deliberately no second sort here.
        /// </summary>
        private static Texture2D[] LoadFrames(string stateFolder)
        {
            string[] names = Res.ListFiles(stateFolder, ".png");
            var frames = new List<Texture2D>(names.Length);

            foreach (string name in names)
            {
                var texture = Res.Load<Texture2D>(stateFolder + "/" + name);
                if (texture != null)
                    frames.Add(texture);
            }

            return frames.ToArray();
        }

        private static bool IsLoopingByDefault(string state)
        {
            return state != "attack" && state != "hurt" && state != "death" && state != "jump";
        }

        /// <summary>The manifest's <c>ppu</c>, or <paramref name="fallback"/> when it does not name one.</summary>
        public static float ParsePixelsPerUnit(string json, float fallback = DefaultPixelsPerUnit)
        {
            if (string.IsNullOrEmpty(json))
                return fallback;

            return ReadNumber(json, IndexOfValue(json, "ppu", 0), fallback);
        }

        /// <summary>Every state the manifest names, in the order it names them.</summary>
        public static List<StateEntry> ParseStates(string json)
        {
            var list = new List<StateEntry>();
            if (string.IsNullOrEmpty(json))
                return list;

            int at = IndexOfValue(json, "states", 0);
            if (at < 0)
                return list;

            int open = json.IndexOf('{', at);
            if (open < 0)
                return list;

            int close = MatchingBrace(json, open);
            if (close < 0)
                return list;

            int i = open + 1;
            while (i < close)
            {
                int nameOpen = json.IndexOf('"', i);
                if (nameOpen < 0 || nameOpen > close)
                    break;

                int nameClose = json.IndexOf('"', nameOpen + 1);
                if (nameClose < 0 || nameClose > close)
                    break;

                int bodyOpen = json.IndexOf('{', nameClose);
                if (bodyOpen < 0 || bodyOpen > close)
                    break;

                int bodyClose = MatchingBrace(json, bodyOpen);
                if (bodyClose < 0)
                    break;

                string body = json.Substring(bodyOpen, bodyClose - bodyOpen + 1);
                list.Add(new StateEntry
                {
                    Name = json.Substring(nameOpen + 1, nameClose - nameOpen - 1),
                    Fps = ReadNumber(body, IndexOfValue(body, "fps", 0), DefaultFps),
                    Loop = ReadBool(body, IndexOfValue(body, "loop", 0), true),
                });

                i = bodyClose + 1;
            }

            return list;
        }

        /// <summary>Index of the first character after <c>"key":</c>, or -1.</summary>
        private static int IndexOfValue(string json, string key, int from)
        {
            string quoted = "\"" + key + "\"";
            int at = json.IndexOf(quoted, from, System.StringComparison.Ordinal);
            if (at < 0)
                return -1;

            int colon = json.IndexOf(':', at + quoted.Length);
            return colon < 0 ? -1 : colon + 1;
        }

        /// <remarks>Brace counting only; a state name carrying a brace would break it, and none does.</remarks>
        private static int MatchingBrace(string json, int open)
        {
            int depth = 0;
            for (int i = open; i < json.Length; i++)
            {
                if (json[i] == '{')
                    depth++;
                else if (json[i] == '}' && --depth == 0)
                    return i;
            }

            return -1;
        }

        private static float ReadNumber(string json, int at, float fallback)
        {
            if (at < 0)
                return fallback;

            int start = at;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;

            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == '+' || json[end] == 'e' || json[end] == 'E'))
                end++;

            return end > start && float.TryParse(json.Substring(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }

        private static bool ReadBool(string json, int at, bool fallback)
        {
            if (at < 0)
                return fallback;

            int start = at;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;

            if (start + 4 <= json.Length && string.CompareOrdinal(json, start, "true", 0, 4) == 0)
                return true;
            if (start + 5 <= json.Length && string.CompareOrdinal(json, start, "false", 0, 5) == 0)
                return false;

            return fallback;
        }
    }
}
