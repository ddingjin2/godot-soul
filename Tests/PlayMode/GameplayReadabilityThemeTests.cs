using System.Linq;
using System.Reflection;
using Godot;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// The palette moved out of code and into an artist-owned file. Two things can go wrong and neither
    /// shows up as an error: the file could stop being read at all, or a field could be wired to the
    /// wrong colour - the boss body painted with the leaper's brown, say. Both produce a game that runs.
    ///
    /// The shipped file is generated from the code defaults, so "applied theme equals code defaults" is
    /// a complete check of both directions of the mapping at once. It stops being an identity the moment
    /// an artist edits a colour - and that is the moment to update this test, deliberately, rather than
    /// the moment to discover the palette was never loading.
    ///
    /// PORT: this is also the first thing that exercises the JSON to Resource round trip on a struct.
    /// Res.LoadJson is System.Text.Json, and every colour in Readability.json is an
    /// r/g/b/a object binding onto a <see cref="Godot.Color"/>. A binding that silently yielded
    /// defaults would leave every field at transparent black, which is exactly what the first test
    /// refuses and what the second would catch field by field.
    ///
    /// UNITS: a colour has none. Godot's Color is the same four 0..1 floats Unity's was, so nothing
    /// here is scaled or flipped.
    /// </summary>
    public sealed class GameplayReadabilityThemeTests
    {
        [Test]
        public void ReadabilityJson_LoadsFromTheArtistsFolder()
        {
            GameplayReadabilityThemeData theme = GameplayTuningCatalog.Load()?.ReadabilityTheme;
            Assert.NotNull(theme,
                "Resources/Art/Readability.json should load through the tuning catalog. Regenerate it from the code defaults.");

            Assert.AreNotEqual(default(Color), theme.playerColor, "A theme of all-transparent-black is an empty file, not a palette.");
        }

        [Test]
        public void AppliedTheme_ReproducesEveryShippedColour()
        {
            GameplayReadabilityDefaults code = GameplayReadabilityDefaults.CreateBase();
            GameplayReadabilityDefaults themed = GameplayReadabilityDefaults.Create();

            AssertColor(code.BackgroundColor, themed.BackgroundColor, nameof(code.BackgroundColor));
            AssertColor(code.GroundColor, themed.GroundColor, nameof(code.GroundColor));
            AssertColor(code.PlatformColor, themed.PlatformColor, nameof(code.PlatformColor));
            AssertColor(code.GroundRimColor, themed.GroundRimColor, nameof(code.GroundRimColor));
            AssertColor(code.PlatformRimColor, themed.PlatformRimColor, nameof(code.PlatformRimColor));
            AssertColor(code.ArenaGateColor, themed.ArenaGateColor, nameof(code.ArenaGateColor));
            AssertColor(code.SpiritPlatformColor, themed.SpiritPlatformColor, nameof(code.SpiritPlatformColor));

            AssertColor(code.MoonColor, themed.MoonColor, nameof(code.MoonColor));
            AssertColor(code.DistantArchColor, themed.DistantArchColor, nameof(code.DistantArchColor));
            AssertColor(code.DistantArchMidColor, themed.DistantArchMidColor, nameof(code.DistantArchMidColor));

            AssertColor(code.PlayerColor, themed.PlayerColor, nameof(code.PlayerColor));
            AssertColor(code.EnemyColor, themed.EnemyColor, nameof(code.EnemyColor));
            AssertColor(code.LeaperColor, themed.LeaperColor, nameof(code.LeaperColor));
            AssertColor(code.CasterColor, themed.CasterColor, nameof(code.CasterColor));
            AssertColor(code.BossColor, themed.BossColor, nameof(code.BossColor));
            AssertColor(code.SwordColor, themed.SwordColor, nameof(code.SwordColor));
            AssertColor(code.ProjectileColor, themed.ProjectileColor, nameof(code.ProjectileColor));

            AssertColor(code.CheckpointLabelColor, themed.CheckpointLabelColor, nameof(code.CheckpointLabelColor));
            AssertColor(code.DuelFloorLabelColor, themed.DuelFloorLabelColor, nameof(code.DuelFloorLabelColor));
            AssertColor(code.WrathAltarLabelColor, themed.WrathAltarLabelColor, nameof(code.WrathAltarLabelColor));

            AssertColor(code.PlayerAttackReadoutColor, themed.PlayerAttackReadoutColor, nameof(code.PlayerAttackReadoutColor));
            AssertColor(code.MeleeDangerColor, themed.MeleeDangerColor, nameof(code.MeleeDangerColor));
            AssertColor(code.LeapDangerColor, themed.LeapDangerColor, nameof(code.LeapDangerColor));
            AssertColor(code.CastDangerColor, themed.CastDangerColor, nameof(code.CastDangerColor));
            AssertColor(code.BossSlashDangerColor, themed.BossSlashDangerColor, nameof(code.BossSlashDangerColor));
            AssertColor(code.BossSlamDangerColor, themed.BossSlamDangerColor, nameof(code.BossSlamDangerColor));

            AssertColor(code.PlayerHealthBarColor, themed.PlayerHealthBarColor, nameof(code.PlayerHealthBarColor));
            AssertColor(code.EnemyHealthBarColor, themed.EnemyHealthBarColor, nameof(code.EnemyHealthBarColor));
            AssertColor(code.BossHealthBarColor, themed.BossHealthBarColor, nameof(code.BossHealthBarColor));
            AssertColor(code.MeleeRoleColor, themed.MeleeRoleColor, nameof(code.MeleeRoleColor));
            AssertColor(code.LeapRoleColor, themed.LeapRoleColor, nameof(code.LeapRoleColor));
            AssertColor(code.CastRoleColor, themed.CastRoleColor, nameof(code.CastRoleColor));
            AssertColor(code.BossRoleColor, themed.BossRoleColor, nameof(code.BossRoleColor));
        }

        /// <summary>
        /// The check the two above cannot make. The shipped theme is generated from the code defaults,
        /// so "applied equals code" is also what an <c>ApplyTo</c> that did nothing at all would produce
        /// - both tests would stay green while the palette file was read and then ignored.
        ///
        /// This one gives every field a value nothing else has and demands it arrive at the property of
        /// the same name, so a field that is dropped, or wired to its neighbour, has nowhere to hide.
        /// Written against the field names rather than as thirty-three assignments because that is
        /// precisely the claim - each colour lands on its namesake - and it keeps covering colours
        /// added after today.
        /// </summary>
        [Test]
        public void EveryThemeColour_LandsOnThePropertyOfTheSameName()
        {
            // Unity needed ScriptableObject.CreateInstance; a Godot Resource is a plain new.
            var theme = new GameplayReadabilityThemeData();

            FieldInfo[] fields = typeof(GameplayReadabilityThemeData)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(Color))
                .ToArray();

            Assert.Greater(fields.Length, 25, "The palette should carry every colour the slice renders.");

            // Distinct per field, and distinct in all four channels, so a swap between two fields cannot
            // coincidentally match.
            for (var i = 0; i < fields.Length; i++)
            {
                float v = (i + 1) / 100f;
                fields[i].SetValue(theme, new Color(v, v + 0.001f, v + 0.002f, v + 0.003f));
            }

            GameplayReadabilityDefaults defaults = GameplayReadabilityDefaults.CreateBase();
            theme.ApplyTo(defaults);

            foreach (FieldInfo field in fields)
            {
                string propertyName = char.ToUpperInvariant(field.Name[0]) + field.Name.Substring(1);
                PropertyInfo property = typeof(GameplayReadabilityDefaults).GetProperty(propertyName);

                Assert.NotNull(property,
                    $"Theme field '{field.Name}' has no '{propertyName}' to land on; it is a colour nobody reads.");

                var expected = (Color)field.GetValue(theme);
                var actual = (Color)property.GetValue(defaults);

                AssertColor(expected, actual, $"{field.Name} -> {propertyName}");
            }
        }

        /// <summary>
        /// Layout is not palette. If sizes or sorting orders ever start coming out of the theme file,
        /// an artist gains a knob that silently breaks which readout draws over which - and the danger
        /// zones stop being readable without a single error.
        /// </summary>
        [Test]
        public void Theme_DoesNotReachSizesOrSortingOrders()
        {
            var theme = new GameplayReadabilityThemeData();
            GameplayReadabilityDefaults defaults = GameplayReadabilityDefaults.CreateBase();

            // Both of these are Godot pixels - CreateBase runs them through World.U. Nothing is asserted
            // about their magnitude, only that applying a palette leaves them exactly where they were,
            // so the conversion does not enter the check.
            Vector2 bossVisual = defaults.BossVisualSize;
            int roleMarkerOrder = defaults.RoleMarkerSortingOrder;
            float hitboxRadius = defaults.PlayerHitboxRadius;

            // An all-default theme would zero every colour it owns. Nothing else may move.
            theme.ApplyTo(defaults);

            Assert.AreEqual(bossVisual, defaults.BossVisualSize, "Body sizes are readability engineering, not palette.");
            Assert.AreEqual(roleMarkerOrder, defaults.RoleMarkerSortingOrder, "Sorting order decides what covers what; a palette must not touch it.");
            Assert.AreEqual(hitboxRadius, defaults.PlayerHitboxRadius, 0.0001f, "Hitbox reach is combat, not colour.");
        }

        private static void AssertColor(Color expected, Color actual, string name)
        {
            Assert.AreEqual(expected.R, actual.R, 0.0001f, $"{name} red channel.");
            Assert.AreEqual(expected.G, actual.G, 0.0001f, $"{name} green channel.");
            Assert.AreEqual(expected.B, actual.B, 0.0001f, $"{name} blue channel.");
            Assert.AreEqual(expected.A, actual.A, 0.0001f, $"{name} alpha - the readouts live on their alpha.");
        }
    }
}
