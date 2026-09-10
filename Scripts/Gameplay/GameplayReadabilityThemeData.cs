using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The palette, as data. Every colour the slice renders, in one artist-owned file, matching
    /// [[MoodDirection]] section 2. Before this it was thirty-odd literals inside
    /// <see cref="GameplayReadabilityDefaults"/>, which put the look of the game behind a code change.
    ///
    /// Colours only. Sizes, offsets and sorting orders stay in code because they are readability
    /// engineering rather than palette - see <see cref="GameplayReadabilityDefaults.Create"/>. That
    /// split is also why this file needs no unit conversion: a colour is the same number in metres and
    /// in pixels.
    ///
    /// The shipped file is <c>Resources/Art/Readability.json</c>, generated from the code defaults, so
    /// it begins identical to what it replaced. Do not hand-write a fresh one: regenerate, then edit.
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

        /// <summary>The authored palette, or null when the file is missing - which leaves the code defaults standing.</summary>
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

        /// <summary>
        /// Fills this theme from a set of defaults. The inverse of <see cref="ApplyTo"/>, used only by
        /// the generator - and the reason a mismapped field cannot survive: a theme filled from the
        /// base and applied back has to reproduce it exactly, which is what
        /// <c>GameplayReadabilityThemeTests</c> checks.
        /// </summary>
        public void CopyFrom(GameplayReadabilityDefaults defaults)
        {
            if (defaults == null)
                return;

            backgroundColor = defaults.BackgroundColor;
            groundColor = defaults.GroundColor;
            platformColor = defaults.PlatformColor;
            groundRimColor = defaults.GroundRimColor;
            platformRimColor = defaults.PlatformRimColor;
            arenaGateColor = defaults.ArenaGateColor;
            spiritPlatformColor = defaults.SpiritPlatformColor;

            moonColor = defaults.MoonColor;
            distantArchColor = defaults.DistantArchColor;
            distantArchMidColor = defaults.DistantArchMidColor;

            playerColor = defaults.PlayerColor;
            enemyColor = defaults.EnemyColor;
            leaperColor = defaults.LeaperColor;
            casterColor = defaults.CasterColor;
            bossColor = defaults.BossColor;
            swordColor = defaults.SwordColor;
            projectileColor = defaults.ProjectileColor;

            checkpointLabelColor = defaults.CheckpointLabelColor;
            duelFloorLabelColor = defaults.DuelFloorLabelColor;
            wrathAltarLabelColor = defaults.WrathAltarLabelColor;

            playerAttackReadoutColor = defaults.PlayerAttackReadoutColor;
            meleeDangerColor = defaults.MeleeDangerColor;
            leapDangerColor = defaults.LeapDangerColor;
            castDangerColor = defaults.CastDangerColor;
            bossSlashDangerColor = defaults.BossSlashDangerColor;
            bossSlamDangerColor = defaults.BossSlamDangerColor;

            playerHealthBarColor = defaults.PlayerHealthBarColor;
            enemyHealthBarColor = defaults.EnemyHealthBarColor;
            bossHealthBarColor = defaults.BossHealthBarColor;
            meleeRoleColor = defaults.MeleeRoleColor;
            leapRoleColor = defaults.LeapRoleColor;
            castRoleColor = defaults.CastRoleColor;
            bossRoleColor = defaults.BossRoleColor;
        }
    }
}
