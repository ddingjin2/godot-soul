using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// Every design file names every field of the type that reads it.
    ///
    /// There is no copy of any tuning number in code any more (PLAN_CLOSEOUT D1-D3): a key a file
    /// leaves out is not "the shipped value", it is zero, and zero is a valid number - a platform of
    /// no size, a boss with no health, a colour of transparent black - so nothing errors and the game
    /// runs wrong. This is the one permanent test that closes that hole (decision D6). It replaced the
    /// two identity tests that compared the palette and layout files against the code copies those
    /// files were once generated from, which lost their baseline when the copies went.
    ///
    /// The file-to-type binding is the same one the editor's design tools use, spelled with
    /// <c>typeof</c> here because the addon is <c>#if TOOLS</c> and the suite is not. A file the table
    /// does not know is itself a failure: an unrecognised file in the designer's folder is how a typo
    /// in a chapter name hides.
    ///
    /// Rows nested inside a file - platforms, scenery, spawns - are checked the same way, because a
    /// row that forgets its size is the same silent zero. Three row types are read for their keys but
    /// not walked: a <see cref="BossAttackProfile"/> that names no <c>lungeSpeed</c> does not lunge,
    /// a <see cref="CutsceneShot"/> with no <c>fade</c> track has no fade, and a
    /// <see cref="SinModifiers"/> row's multipliers default to one - in those three, absence is the
    /// authored meaning and the class default is the contract, not a fallback.
    /// </summary>
    public sealed class DesignFileCompletenessTests
    {
        private static readonly Dictionary<string, Type> Named = new(StringComparer.Ordinal)
        {
            ["PlayerMovement"] = typeof(PlayerMovementData),
            ["PlayerCombat"] = typeof(PlayerCombatData),
            ["PlayerResources"] = typeof(PlayerResourceData),
            ["ProgressionTuning"] = typeof(ProgressionTuningData),
            ["SinTuning"] = typeof(SinTuningData),
            ["WorldTuning"] = typeof(WorldTuningData),
            ["CombatTuning"] = typeof(CombatTuningData),
            ["DifficultyTuning"] = typeof(DifficultyTuningData),
            ["CutsceneTuning"] = typeof(CutsceneTuningData),
            ["SceneLayout"] = typeof(GameplaySceneLayoutData),
            ["ReadabilityLayout"] = typeof(GameplayReadabilityLayoutData),
            ["MeleeGrunt"] = typeof(MeleeGruntData),
            ["LeapingAttacker"] = typeof(LeapingAttackerData),
            ["RangedCaster"] = typeof(RangedCasterData),
            ["WrathMiniBoss"] = typeof(WrathMiniBossData),
            ["WrathEncounter"] = typeof(BossEncounterData),

            // The artist's file, from Resources/Art rather than Resources/Design.
            ["Readability"] = typeof(GameplayReadabilityThemeData),
        };

        /// <summary>Row types whose omitted keys are an authored meaning rather than a gap - see the class remarks.</summary>
        private static readonly HashSet<Type> RowsReadForTheirDefaults = new()
        {
            typeof(BossAttackProfile),
            typeof(CutsceneShot),
            typeof(SinModifiers),
        };

        private static readonly JsonDocumentOptions Lenient = new()
        {
            // The same leniency Res.LoadJson extends, so this reads exactly the files the game reads.
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        [Test]
        public void EveryDesignFile_NamesEveryFieldOfItsType()
        {
            List<string> files = Res.ListFiles("Design", ".json")
                .Select(name => "Design/" + name)
                .Append("Art/" + GameplayTuningCatalog.ReadabilityThemeFile + ".json")
                .ToList();

            Assert.Greater(files.Count, 30, "Resources/Design has all but vanished, so this is checking nothing.");

            var report = new List<string>();
            foreach (string file in files)
            {
                Type type = TypeFor(Path.GetFileNameWithoutExtension(file));
                if (type == null)
                {
                    report.Add($"Resources/{file}: no Data type is bound to this file. Add it to the table in this test, or it is a typo.");
                    continue;
                }

                string text = Res.LoadText(file);
                Assert.NotNull(text, $"Resources/{file} was listed but could not be read.");

                var missing = new List<string>();
                using (JsonDocument document = JsonDocument.Parse(text, Lenient))
                    CollectMissing(document.RootElement, type, string.Empty, missing);

                if (missing.Count > 0)
                    report.Add($"Resources/{file} ({type.Name}) is missing: {string.Join(", ", missing)}");
            }

            Assert.IsTrue(report.Count == 0,
                "A key a design file leaves out reads as zero, silently. " +
                $"{report.Count} file(s) leave out a field of the type that reads them:\n" + string.Join("\n", report));
        }

        private static Type TypeFor(string fileName)
        {
            if (Named.TryGetValue(fileName, out Type type))
                return type;

            if (fileName.StartsWith("SceneLayout_", StringComparison.Ordinal))
                return typeof(GameplaySceneLayoutData);

            if (fileName.EndsWith("_Encounter", StringComparison.Ordinal))
                return typeof(BossEncounterData);

            // Chapter0N_Colour_SomeBoss - the rainbow chapter bosses.
            return fileName.StartsWith("Chapter", StringComparison.Ordinal) ? typeof(RainbowChapterBossData) : null;
        }

        private static void CollectMissing(JsonElement element, Type type, string prefix, List<string> missing)
        {
            foreach (FieldInfo field in BoundFields(type))
            {
                if (!element.TryGetProperty(field.Name, out JsonElement value))
                {
                    missing.Add(prefix + field.Name);
                    continue;
                }

                Type row = RowType(field.FieldType);
                if (row == null || RowsReadForTheirDefaults.Contains(row))
                    continue;

                if (value.ValueKind == JsonValueKind.Object)
                {
                    CollectMissing(value, row, prefix + field.Name + ".", missing);
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    var i = 0;
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                            CollectMissing(item, row, $"{prefix}{field.Name}[{i}].", missing);
                        i++;
                    }
                }
            }
        }

        /// <summary>
        /// What the JSON reader binds: public instance fields, and only the ones this project declared -
        /// JsonData is fields-only, case-sensitive, and the Godot base classes contribute nothing to it.
        /// </summary>
        private static IEnumerable<FieldInfo> BoundFields(Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(field => IsOurs(field.DeclaringType));
        }

        /// <summary>A nested serialisable class, or the element type of an array of them; null for a leaf (number, string, enum, Godot struct).</summary>
        private static Type RowType(Type fieldType)
        {
            Type element = fieldType.IsArray ? fieldType.GetElementType() : fieldType;
            return element != null && element.IsClass && element != typeof(string) && IsOurs(element) ? element : null;
        }

        private static bool IsOurs(Type type) =>
            type?.Namespace != null && type.Namespace.StartsWith("MyGame", StringComparison.Ordinal);
    }
}
