#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Writes <c>res://Resources/Art/Readability.json</c> from the code defaults, so the palette file
    /// starts life byte-identical to what it replaced instead of being transcribed by hand thirty-three
    /// times.
    ///
    /// It refuses to overwrite an existing file. Once the artist has edited the palette, the code
    /// defaults are the older document, and regenerating would quietly hand the game back its greybox
    /// colours - the RE-WRITE command is the one that asks first, matching every other generator here.
    /// </summary>
    /// <remarks>
    /// The Unity version instantiated the theme ScriptableObject directly. This reaches
    /// <c>GameplayReadabilityDefaults</c> by reflection instead, for the same reason the design table
    /// names its types as strings: the plugin has to compile while <c>MyGame.Gameplay</c> is still being
    /// ported. Reflection, not a second copy of the palette - the defaults have exactly one home, and
    /// a tool that duplicated them would be generating last month's colours the first time anyone
    /// touched them.
    /// </remarks>
    public static class ReadabilityThemeWriter
    {
        public const string SuccessMarker = "ReadabilityThemeWriter: done.";

        private const string Folder = "res://Resources/Art";
        private const string FileName = "Readability.json";

        public static void Run() => Write(overwrite: false);

        /// <summary>
        /// Discards palette edits. Unity put a confirmation dialog in front of this; the dock has no
        /// safe place for one, so it is not on a button - a human who means it calls it from a test or
        /// a one-off script, which is a higher bar than a mis-click.
        /// </summary>
        public static void Rewrite() => Write(overwrite: true);

        private static void Write(bool overwrite)
        {
            string resPath = Folder + "/" + FileName;
            string path = DesignDataFiles.Abs(resPath);

            if (File.Exists(path) && !overwrite)
            {
                GD.Print($"ReadabilityThemeWriter: {resPath} already exists, left untouched.");
                GD.Print(SuccessMarker);
                return;
            }

            object theme = BuildTheme();
            if (theme == null)
            {
                return;
            }

            Directory.CreateDirectory(DesignDataFiles.Abs(Folder));
            File.WriteAllText(path, MyGame.Core.JsonData.ToJson(ToJsonShape(theme)) + "\n");

            GD.Print($"ReadabilityThemeWriter: wrote {resPath}.");
            GD.Print(SuccessMarker);
        }

        private static object BuildTheme()
        {
            Type defaultsType = DesignDataFiles.Resolve("MyGame.Gameplay.GameplayReadabilityDefaults");
            Type themeType = DesignDataFiles.Resolve("MyGame.Gameplay.GameplayReadabilityThemeData");

            if (defaultsType == null || themeType == null)
            {
                GD.PushError("ReadabilityThemeWriter: MyGame.Gameplay.GameplayReadabilityDefaults / ...ThemeData are not in the build yet; nothing written.");
                return null;
            }

            // CreateBase, not Create: Create applies the file being written, so generating from it
            // would echo whatever is already on disk instead of the code defaults.
            object defaults = defaultsType.GetMethod("CreateBase", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, null);

            MethodInfo copyFrom = themeType.GetMethod("CopyFrom", BindingFlags.Public | BindingFlags.Instance);
            if (defaults == null || copyFrom == null)
            {
                GD.PushError("ReadabilityThemeWriter: expected GameplayReadabilityDefaults.CreateBase() and GameplayReadabilityThemeData.CopyFrom(defaults); the port has changed shape.");
                return null;
            }

            object theme = Activator.CreateInstance(themeType);
            copyFrom.Invoke(theme, new[] { defaults });
            return theme;
        }

        /// <summary>
        /// Colours are written as <c>{r,g,b,a}</c> objects, which is what the Unity JsonUtility file
        /// looked like and what the loader still reads. Serialising a <c>Godot.Color</c> straight would
        /// spill its whole property surface (H, S, V, R8, ...) into the palette.
        /// </summary>
        private static Dictionary<string, object> ToJsonShape(object theme)
        {
            var json = new Dictionary<string, object>();

            foreach (FieldInfo field in theme.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = field.GetValue(theme);
                json[field.Name] = value is Color c
                    ? new Dictionary<string, float> { ["r"] = c.R, ["g"] = c.G, ["b"] = c.B, ["a"] = c.A }
                    : value;
            }

            return json;
        }
    }
}
#endif
