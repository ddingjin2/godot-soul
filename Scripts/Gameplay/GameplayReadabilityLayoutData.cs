using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The layout half of the greybox read, as data: how big every actor, hitbox, bar and danger
    /// readout is and where it sits on the actor wearing it. The sibling of
    /// <see cref="GameplayReadabilityThemeData"/>, and split from it on purpose - colour is the
    /// artist's (<c>Resources/Art/Readability.json</c>), size is the designer's
    /// (<c>Resources/Design/ReadabilityLayout.json</c>), and <see cref="GameplayTuningCatalog"/>
    /// loads each from its owner's folder.
    ///
    /// Sorting orders stay in code: they decide what covers what, and a knob there breaks the reads
    /// the slice exists to prove (numbers audit §3.6).
    /// </summary>
    /// <remarks>
    /// UNITS - every spatial field is authored in Unity metres with +Y up and converted once, in
    /// <see cref="ApplyTo"/>, exactly as <see cref="GameplayReadabilityDefaults.CreateBase"/> converts
    /// its own literals:
    /// <list type="bullet">
    /// <item><description><c>*Size</c>, <c>*Radius</c>, <c>*CharacterSize</c>, <c>*Thickness</c> and
    /// <c>*Margin</c> are scaled by <see cref="World.U"/> - no sign change, a size has none;</description></item>
    /// <item><description><c>*Offset</c> and <c>*LocalPosition</c> go through <see cref="World.V"/>,
    /// scaled <i>and</i> vertically flipped - a bar 1.24 m above the actor lands 124 px below its
    /// origin; <c>lockOnMarkerHeight</c> is a height above and becomes a flipped offset the same way;</description></item>
    /// <item><description>font point sizes, the low-health threshold and blend, the rim height
    /// fraction and the four <c>markerPulse*</c> feel numbers are not spatial and cross
    /// untouched.</description></item>
    /// </list>
    /// No field carries a default: a missing file leaves the code defaults standing (the loader returns
    /// null and <see cref="GameplayReadabilityDefaults.Create"/> skips the apply), and a missing key
    /// reads as zero, which <c>GameplayReadabilityLayoutTests</c> catches by demanding the shipped file
    /// reproduce the code defaults exactly.
    /// </remarks>
    public sealed partial class GameplayReadabilityLayoutData : Resource
    {
        public const string FileName = "ReadabilityLayout";

        // Collider and visual sizes
        [Export] public Vector2 playerColliderSize;
        [Export] public Vector2 playerVisualSize;
        [Export] public Vector2 meleeColliderSize;
        [Export] public Vector2 meleeVisualSize;
        [Export] public Vector2 leaperColliderSize;
        [Export] public Vector2 leaperVisualSize;
        [Export] public Vector2 casterColliderSize;
        [Export] public Vector2 casterVisualSize;
        [Export] public Vector2 bossColliderSize;
        [Export] public Vector2 bossVisualSize;

        // Player hitbox and sword
        [Export] public float playerHitboxRadius;
        [Export] public Vector2 playerHitboxOffset;
        [Export] public Vector2 swordSize;
        [Export] public Vector2 attackArcSize;

        // Health bars
        [Export] public Vector2 playerHealthBarSize;
        [Export] public Vector2 playerHealthBarOffset;
        [Export] public Vector2 enemyHealthBarSize;
        [Export] public Vector2 bossHealthBarSize;
        [Export] public Vector2 meleeHealthBarOffset;
        [Export] public Vector2 leaperHealthBarOffset;
        [Export] public Vector2 casterHealthBarOffset;
        [Export] public Vector2 bossHealthBarOffset;
        [Export] public float healthBarFrameMargin;
        [Export] public float healthBarLowHealthThreshold;
        [Export] public float healthBarLowHealthTintBlend;

        // Danger readouts
        [Export] public Vector2 meleeDangerLocalPosition;
        [Export] public Vector2 meleeDangerSize;
        [Export] public Vector2 leapDangerLocalPosition;
        [Export] public Vector2 leapDangerSize;
        [Export] public Vector2 castDangerLocalPosition;
        [Export] public Vector2 castDangerSize;
        [Export] public Vector2 bossSlashDangerLocalPosition;
        [Export] public Vector2 bossSlashDangerSize;
        [Export] public Vector2 bossSlamDangerLocalPosition;
        [Export] public Vector2 bossSlamDangerSize;

        // Role markers and world labels
        [Export] public Vector2 meleeRoleLocalPosition;
        [Export] public Vector2 leapRoleLocalPosition;
        [Export] public Vector2 castRoleLocalPosition;
        [Export] public Vector2 bossRoleLocalPosition;
        [Export] public float worldLabelCharacterSize;
        [Export] public int worldLabelFontSize;
        [Export] public float roleMarkerCharacterSize;
        [Export] public int roleMarkerFontSize;

        // World marks
        [Export] public float lockOnMarkerHeight;
        [Export] public float platformRimThickness;
        [Export] public float platformRimHeightFraction;

        // The breathing every live mark shares - bonfire disc, portal, soul stain, danger readout.
        // Rad/s, a scale fraction and two alphas: nothing spatial.
        [Export] public float markerPulseSpeed;
        [Export] public float markerPulseAmount;
        [Export] public float markerPulseAlphaMin;
        [Export] public float markerPulseAlphaMax;

        /// <summary>The authored layout, or null when the file is missing - which leaves the code defaults standing.</summary>
        public static GameplayReadabilityLayoutData Load(string resourcePath = GameplayTuningCatalog.DesignResourceFolder + FileName)
        {
            return Res.LoadJson<GameplayReadabilityLayoutData>(resourcePath);
        }

        public void ApplyTo(GameplayReadabilityDefaults defaults)
        {
            if (defaults == null)
                return;

            defaults.PlayerColliderSize = S(playerColliderSize);
            defaults.PlayerVisualSize = S(playerVisualSize);
            defaults.MeleeColliderSize = S(meleeColliderSize);
            defaults.MeleeVisualSize = S(meleeVisualSize);
            defaults.LeaperColliderSize = S(leaperColliderSize);
            defaults.LeaperVisualSize = S(leaperVisualSize);
            defaults.CasterColliderSize = S(casterColliderSize);
            defaults.CasterVisualSize = S(casterVisualSize);
            defaults.BossColliderSize = S(bossColliderSize);
            defaults.BossVisualSize = S(bossVisualSize);

            defaults.PlayerHitboxRadius = World.U(playerHitboxRadius);
            defaults.PlayerHitboxOffset = World.V(playerHitboxOffset);
            defaults.SwordSize = S(swordSize);
            defaults.AttackArcSize = S(attackArcSize);

            defaults.PlayerHealthBarSize = S(playerHealthBarSize);
            defaults.PlayerHealthBarOffset = World.V(playerHealthBarOffset);
            defaults.EnemyHealthBarSize = S(enemyHealthBarSize);
            defaults.BossHealthBarSize = S(bossHealthBarSize);
            defaults.MeleeHealthBarOffset = World.V(meleeHealthBarOffset);
            defaults.LeaperHealthBarOffset = World.V(leaperHealthBarOffset);
            defaults.CasterHealthBarOffset = World.V(casterHealthBarOffset);
            defaults.BossHealthBarOffset = World.V(bossHealthBarOffset);
            defaults.HealthBarFrameMargin = World.U(healthBarFrameMargin);
            defaults.HealthBarLowHealthThreshold = healthBarLowHealthThreshold;
            defaults.HealthBarLowHealthTintBlend = healthBarLowHealthTintBlend;

            defaults.MeleeDangerLocalPosition = World.V(meleeDangerLocalPosition);
            defaults.MeleeDangerSize = S(meleeDangerSize);
            defaults.LeapDangerLocalPosition = World.V(leapDangerLocalPosition);
            defaults.LeapDangerSize = S(leapDangerSize);
            defaults.CastDangerLocalPosition = World.V(castDangerLocalPosition);
            defaults.CastDangerSize = S(castDangerSize);
            defaults.BossSlashDangerLocalPosition = World.V(bossSlashDangerLocalPosition);
            defaults.BossSlashDangerSize = S(bossSlashDangerSize);
            defaults.BossSlamDangerLocalPosition = World.V(bossSlamDangerLocalPosition);
            defaults.BossSlamDangerSize = S(bossSlamDangerSize);

            defaults.MeleeRoleLocalPosition = World.V(meleeRoleLocalPosition);
            defaults.LeapRoleLocalPosition = World.V(leapRoleLocalPosition);
            defaults.CastRoleLocalPosition = World.V(castRoleLocalPosition);
            defaults.BossRoleLocalPosition = World.V(bossRoleLocalPosition);
            defaults.WorldLabelCharacterSize = World.U(worldLabelCharacterSize);
            defaults.WorldLabelFontSize = worldLabelFontSize;
            defaults.RoleMarkerCharacterSize = World.U(roleMarkerCharacterSize);
            defaults.RoleMarkerFontSize = roleMarkerFontSize;

            defaults.LockOnMarkerOffset = World.V(new Vector2(0f, lockOnMarkerHeight));
            defaults.PlatformRimThickness = World.U(platformRimThickness);
            defaults.PlatformRimHeightFraction = platformRimHeightFraction;

            defaults.MarkerPulseSpeed = markerPulseSpeed;
            defaults.MarkerPulseAmount = markerPulseAmount;
            defaults.MarkerPulseAlphaMin = markerPulseAlphaMin;
            defaults.MarkerPulseAlphaMax = markerPulseAlphaMax;
        }

        private static Vector2 S(Vector2 metres) => new(World.U(metres.X), World.U(metres.Y));
    }
}
