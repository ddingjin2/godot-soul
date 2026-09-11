using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The palette, as data. Every colour the slice renders, in one artist-owned file, matching
    /// [[MoodDirection]] section 2. Before this it was thirty-odd literals inside
    /// <see cref="GameplayReadabilityDefaults"/>, which put the look of the game behind a code change.
    ///
    /// Colours only. Sizes and offsets are the designer's, in <see cref="GameplayReadabilityLayoutData"/>;
    /// sorting orders are code, in <see cref="GameplayReadabilityDefaults.Create"/>. That split is also
    /// why this file needs no unit conversion: a colour is the same number in metres and in pixels.
    ///
    /// The shipped file is <c>Resources/Art/Readability.json</c>, and it is the only copy of the palette
    /// - there are no colours in code behind it. <c>DesignFileCompletenessTests</c> holds it to naming
    /// every field here.
    /// </summary>
    public sealed partial class GameplayReadabilityThemeData : Resource
    {
        // World
        [Export] public Color backgroundColor;
        [Export] public Color groundColor;
        [Export] public Color platformColor;
        [Export] public Color groundRimColor;
        [Export] public Color platformRimColor;
        [Export] public Color arenaGateColor;
        [Export] public Color spiritPlatformColor;

        // Backdrop
        [Export] public Color moonColor;
        [Export] public Color distantArchColor;
        [Export] public Color distantArchMidColor;

        // Actors
        [Export] public Color playerColor;
        [Export] public Color enemyColor;
        [Export] public Color leaperColor;
        [Export] public Color casterColor;
        [Export] public Color bossColor;
        [Export] public Color swordColor;
        [Export] public Color projectileColor;

        // World Labels
        [Export] public Color checkpointLabelColor;
        [Export] public Color duelFloorLabelColor;
        [Export] public Color wrathAltarLabelColor;

        // Danger Readouts
        [Export] public Color playerAttackReadoutColor;
        [Export] public Color meleeDangerColor;
        [Export] public Color leapDangerColor;
        [Export] public Color castDangerColor;
        [Export] public Color bossSlashDangerColor;
        [Export] public Color bossSlamDangerColor;

        // Health Bars And Role Markers
        [Export] public Color playerHealthBarColor;
        [Export] public Color enemyHealthBarColor;
        [Export] public Color bossHealthBarColor;
        [Export] public Color meleeRoleColor;
        [Export] public Color leapRoleColor;
        [Export] public Color castRoleColor;
        [Export] public Color bossRoleColor;

        /// <summary>The authored palette, or null when the file is missing - which the loader has reported and <see cref="GameplayReadabilityDefaults.Create"/> passes on as null.</summary>
        public static GameplayReadabilityThemeData Load(
            string resourcePath = GameplayTuningCatalog.ArtResourceFolder + GameplayTuningCatalog.ReadabilityThemeFile)
        {
            return Res.LoadJson<GameplayReadabilityThemeData>(resourcePath);
        }

        public void ApplyTo(GameplayReadabilityDefaults defaults)
        {
            if (defaults == null)
                return;

            defaults.BackgroundColor = backgroundColor;
            defaults.GroundColor = groundColor;
            defaults.PlatformColor = platformColor;
            defaults.GroundRimColor = groundRimColor;
            defaults.PlatformRimColor = platformRimColor;
            defaults.ArenaGateColor = arenaGateColor;
            defaults.SpiritPlatformColor = spiritPlatformColor;

            defaults.MoonColor = moonColor;
            defaults.DistantArchColor = distantArchColor;
            defaults.DistantArchMidColor = distantArchMidColor;

            defaults.PlayerColor = playerColor;
            defaults.EnemyColor = enemyColor;
            defaults.LeaperColor = leaperColor;
            defaults.CasterColor = casterColor;
            defaults.BossColor = bossColor;
            defaults.SwordColor = swordColor;
            defaults.ProjectileColor = projectileColor;

            defaults.CheckpointLabelColor = checkpointLabelColor;
            defaults.DuelFloorLabelColor = duelFloorLabelColor;
            defaults.WrathAltarLabelColor = wrathAltarLabelColor;

            defaults.PlayerAttackReadoutColor = playerAttackReadoutColor;
            defaults.MeleeDangerColor = meleeDangerColor;
            defaults.LeapDangerColor = leapDangerColor;
            defaults.CastDangerColor = castDangerColor;
            defaults.BossSlashDangerColor = bossSlashDangerColor;
            defaults.BossSlamDangerColor = bossSlamDangerColor;

            defaults.PlayerHealthBarColor = playerHealthBarColor;
            defaults.EnemyHealthBarColor = enemyHealthBarColor;
            defaults.BossHealthBarColor = bossHealthBarColor;
            defaults.MeleeRoleColor = meleeRoleColor;
            defaults.LeapRoleColor = leapRoleColor;
            defaults.CastRoleColor = castRoleColor;
            defaults.BossRoleColor = bossRoleColor;
        }
    }
}
