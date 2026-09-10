#if TOOLS
using System;
using System.IO;
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// The one table mapping a designer-facing tuning file to its runtime type. Every editor tool that
    /// touches design data reads this, so adding a tuning group is a single-line change.
    /// </summary>
    /// <remarks>
    /// Two things changed in the port.
    ///
    /// The folder moved from <c>Assets/_Project/Resources/Design</c> to <c>res://Resources/Design</c>;
    /// the file names did not, so the JSON came over untouched.
    ///
    /// The runtime type is named by string rather than by <c>typeof</c>. This plugin has to compile
    /// on its own - the gameplay assemblies are still being ported - and an editor addon that fails to
    /// build takes the whole project's build with it. <see cref="Resolve"/> looks the type up at run
    /// time and returns null when it is not there yet, which the tools report rather than crash on.
    /// </remarks>
    public static class DesignDataFiles
    {
        public const string JsonFolder = "res://Resources/Design";

        public readonly struct Entry
        {
            public Entry(string fileName, string typeName)
            {
                FileName = fileName;
                TypeName = typeName;
            }

            /// <summary>Base name of the JSON file, without extension.</summary>
            public string FileName { get; }

            /// <summary>Assembly-qualified-ish name of the Data class the file deserialises into.</summary>
            public string TypeName { get; }

            public string JsonPath => JsonFolder + "/" + FileName + ".json";
        }

        public static readonly Entry[] All =
        {
            new Entry("PlayerMovement", "MyGame.Player.PlayerMovementData"),
            new Entry("PlayerCombat", "MyGame.Player.PlayerCombatData"),
            new Entry("PlayerResources", "MyGame.Player.PlayerResourceData"),

            // Authored as JSON from the start rather than migrated off a ScriptableObject. It is on
            // this table because the cost curve is the one number in the soul sink a designer actually
            // retunes. (Unity tracked the legacy .asset each file was seeded from; the port has no
            // ScriptableObjects, so that column is gone with the seeder that used it.)
            new Entry("ProgressionTuning", "MyGame.Player.ProgressionTuningData"),
            new Entry("SinTuning", "MyGame.Combat.SinTuningData"),

            // Combat feel - hit stop, shake, flash, pack spacing, swing knockback. The class existed
            // from the port; the file did not, so until it was written every one of those numbers
            // rested on a C# field.
            new Entry("CombatTuning", "MyGame.Combat.CombatTuningData"),
            new Entry("WorldTuning", "MyGame.Gameplay.WorldTuningData"),
            new Entry("SceneLayout", "MyGame.Gameplay.GameplaySceneLayoutData"),
            new Entry("MeleeGrunt", "MyGame.Enemy.MeleeGruntData"),
            new Entry("LeapingAttacker", "MyGame.Enemy.LeapingAttackerData"),
            new Entry("RangedCaster", "MyGame.Enemy.RangedCasterData"),
            new Entry("WrathMiniBoss", "MyGame.Enemy.WrathMiniBossData"),
            new Entry("WrathEncounter", "MyGame.Enemy.BossEncounterData")
        };

        /// <summary>
        /// The Data class for any design file on disk, including the per-chapter ones that are not on
        /// the table: there are seven of each and they share three shapes, so they are matched by name
        /// rather than listed one by one.
        /// </summary>
        public static string TypeNameFor(string fileName)
        {
            foreach (Entry entry in All)
            {
                if (entry.FileName == fileName)
                {
                    return entry.TypeName;
                }
            }

            if (fileName.StartsWith("SceneLayout_", StringComparison.Ordinal))
            {
                return "MyGame.Gameplay.GameplaySceneLayoutData";
            }

            if (fileName.EndsWith("_Encounter", StringComparison.Ordinal))
            {
                return "MyGame.Enemy.BossEncounterData";
            }

            // Chapter0N_Colour_SomeBoss - the rainbow chapter bosses.
            return fileName.StartsWith("Chapter", StringComparison.Ordinal)
                ? "MyGame.Enemy.RainbowChapterBossData"
                : null;
        }

        /// <summary>The runtime type, or null while that folder of the port is still being written.</summary>
        public static Type Resolve(string typeName)
        {
            return typeName == null ? null : Type.GetType(typeName + ", MyGame");
        }

        /// <summary>A <c>res://</c> path as something <c>System.IO</c> can open.</summary>
        public static string Abs(string resPath) => ProjectSettings.GlobalizePath(resPath);

        /// <summary>Every design file on disk, in name order.</summary>
        public static string[] AllFileNames()
        {
            string folder = Abs(JsonFolder);
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            string[] files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
            for (var i = 0; i < files.Length; i++)
            {
                files[i] = Path.GetFileNameWithoutExtension(files[i]);
            }

            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
    }
}
#endif
