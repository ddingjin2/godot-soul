#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Imports everything under <c>res://Resources/PixelActors</c> and <c>res://Resources/Art</c> as
    /// pixel art: nearest-neighbour filtering, no compression, no mipmaps.
    /// </summary>
    /// <remarks>
    /// Unity did this from an <c>AssetPostprocessor</c>, so frames dropped in while the editor was open
    /// were corrected on the spot. Godot's import settings live in a <c>.png.import</c> file beside each
    /// texture, written by the importer the first time it sees the file, so the same job is a pass over
    /// those files. It is a button rather than an automatic hook because a Godot import that changes
    /// settings triggers a reimport, and doing that unasked in the middle of someone's editing session
    /// is worse than the blurry frame it prevents. Run it after dropping in a batch of frames.
    ///
    /// Three of Unity's four settings map onto the <c>.import</c> file:
    /// <list type="bullet">
    /// <item><description><c>textureCompression = Uncompressed</c> -> <c>compress/mode=0</c>
    /// (lossless), plus <c>detect_3d/compress_to=0</c>: left at its default of 1, Godot silently
    /// re-imports a texture to VRAM compression the first time it is used in 3D, which is exactly the
    /// quiet quality loss the Unity setting existed to prevent.</description></item>
    /// <item><description><c>mipmapEnabled = false</c> -> <c>mipmaps/generate=false</c>.</description></item>
    /// <item><description><c>alphaIsTransparency</c> -> <c>process/fix_alpha_border=true</c>, the
    /// default; it is what stops a bilinear sample bleeding black out of a transparent
    /// edge.</description></item>
    /// </list>
    ///
    /// Two do not, and are not settings any more:
    /// <c>filterMode = Point</c> is <c>rendering/textures/canvas_textures/default_texture_filter</c> in
    /// project.godot, already Nearest for the whole project, overridable per node; and
    /// <c>spritePixelsPerUnit</c>, which the Unity importer read out of each actor's <c>anim.json</c>,
    /// has no import-time counterpart at all - Godot draws a texture at one pixel per pixel and the
    /// runtime <c>PixelActorAnim</c> reads that same <c>ppu</c> to scale the actor.
    /// </remarks>
    public static class PixelActorTextureImportSettings
    {
        private static readonly string[] Folders =
        {
            "res://Resources/PixelActors",
            "res://Resources/Art",
        };

        /// <summary>The parameter lines this enforces, by key.</summary>
        private static readonly Dictionary<string, string> Required = new()
        {
            ["compress/mode"] = "0",
            ["mipmaps/generate"] = "false",
            ["detect_3d/compress_to"] = "0",
        };

        public static void Run()
        {
            var fixedCount = 0;
            var seen = 0;
            var unimported = new List<string>();

            foreach (string resFolder in Folders)
            {
                string folder = DesignDataFiles.Abs(resFolder);
                if (!Directory.Exists(folder))
                {
                    GD.PushWarning($"PixelActorTextureImportSettings: {resFolder} does not exist.");
                    continue;
                }

                foreach (string png in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
                {
                    seen++;
                    string importFile = png + ".import";
                    if (!File.Exists(importFile))
                    {
                        unimported.Add(Path.GetFileName(png));
                        continue;
                    }

                    if (Enforce(importFile))
                    {
                        fixedCount++;
                    }
                }
            }

            GD.Print($"PixelActorTextureImportSettings: checked {seen} textures, corrected {fixedCount}.");

            if (unimported.Count > 0)
            {
                // A .import file only exists after Godot has scanned the texture once. Nothing to fix
                // until then, and the defaults it will write are the project's, which are already right.
                GD.Print($"PixelActorTextureImportSettings: {unimported.Count} texture(s) not imported yet ({string.Join(", ", unimported.GetRange(0, Math.Min(5, unimported.Count)))}...). Re-run after the editor has scanned them.");
            }

            if (fixedCount > 0)
            {
                GD.Print("PixelActorTextureImportSettings: reimport the changed textures (Project > Reload Current Project, or re-run the editor) for the new settings to take.");
            }
        }

        /// <summary>Rewrites only the lines that are wrong, so an untouched file keeps its timestamp.</summary>
        private static bool Enforce(string importFile)
        {
            string[] lines = File.ReadAllLines(importFile);
            var changed = false;

            for (var i = 0; i < lines.Length; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = lines[i][..eq].Trim();
                if (!Required.TryGetValue(key, out string want) || lines[i][(eq + 1)..].Trim() == want)
                {
                    continue;
                }

                lines[i] = key + "=" + want;
                changed = true;
            }

            if (changed)
            {
                File.WriteAllLines(importFile, lines);
            }

            return changed;
        }
    }
}
#endif
