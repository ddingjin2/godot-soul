using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// How the greybox reads: what colour everything is, how big it is, where it sits relative to the
    /// actor wearing it, and what draws in front of what.
    ///
    /// Two files own it - the artist's palette (<c>Resources/Art/Readability.json</c>) and the
    /// designer's layout (<c>Resources/Design/ReadabilityLayout.json</c>) - and the only numbers in
    /// this class are the twenty sorting orders in <see cref="WithSortingOrders"/>.
    ///
    /// UNITS - this is one of the two conversion boundaries in the arena code (the other is
    /// <see cref="GameplaySceneDefaults"/>). Every spatial value is authored in Unity metres with +Y
    /// up and converted <b>once</b>, in <see cref="GameplayReadabilityLayoutData.ApplyTo"/>, into the
    /// pixels these properties hold:
    /// <list type="bullet">
    /// <item>sizes (<c>*Size</c>) are scaled by <see cref="World.Ppu"/> - no sign change, a size has none;</item>
    /// <item>local positions and offsets (<c>*LocalPosition</c>, <c>*Offset</c>) go through
    /// <see cref="World.V"/>, so they are scaled <i>and</i> vertically flipped - a health bar 1.24
    /// metres above the actor is 124 pixels below its origin in Godot;</item>
    /// <item>radii and character sizes are scaled by <see cref="World.U"/>;</item>
    /// <item>colours, sorting orders and font point sizes are not spatial and are left alone.</item>
    /// </list>
    /// Every consumer - the environment builder, both spawners - therefore reads pixels and never
    /// converts again.
    /// </summary>
    public sealed class GameplayReadabilityDefaults
    {
        public Color BackgroundColor { get; internal set; }
        public Color GroundColor { get; internal set; }
        public Color PlatformColor { get; internal set; }
        public Color PlayerColor { get; internal set; }
        public Color EnemyColor { get; internal set; }
        public Color LeaperColor { get; internal set; }
        public Color CasterColor { get; internal set; }
        public Color BossColor { get; internal set; }

        public Color MoonColor { get; internal set; }
        public Color DistantArchColor { get; internal set; }
        public Color DistantArchMidColor { get; internal set; }
        public Color GroundRimColor { get; internal set; }
        public Color ArenaGateColor { get; internal set; }
        public Color PlatformRimColor { get; internal set; }
        public Color SpiritPlatformColor { get; internal set; }
        public Color SwordColor { get; internal set; }
        public Color ProjectileColor { get; internal set; }

        public Color CheckpointLabelColor { get; internal set; }
        public Color DuelFloorLabelColor { get; internal set; }
        public Color WrathAltarLabelColor { get; internal set; }

        public Color PlayerAttackReadoutColor { get; internal set; }
        public Color MeleeDangerColor { get; internal set; }
        public Color LeapDangerColor { get; internal set; }
        public Color CastDangerColor { get; internal set; }
        public Color BossSlashDangerColor { get; internal set; }
        public Color BossSlamDangerColor { get; internal set; }

        public Color PlayerHealthBarColor { get; internal set; }

        /// <summary>
        /// Archetype health bar fill. Deliberately independent of the body tint: the spawner used to
        /// pass the body colour, which makes a muted enemy's bar vanish into the enemy.
        /// </summary>
        public Color EnemyHealthBarColor { get; internal set; }
        public Color BossHealthBarColor { get; internal set; }

        /// <summary>
        /// The bar's frame, and the bone its fill lifts toward at low health. Both were
        /// <c>static readonly Color</c> on <see cref="GameplayWorldHealthBar"/>, with the frame's copy
        /// authored a second time in <c>Scenes/World/WorldHealthBar.tscn</c> (PLAN_CLOSEOUT B3).
        /// </summary>
        public Color HealthBarFrameColor { get; internal set; }
        public Color HealthBarLowHealthTint { get; internal set; }

        public Color MeleeRoleColor { get; internal set; }
        public Color LeapRoleColor { get; internal set; }
        public Color CastRoleColor { get; internal set; }
        public Color BossRoleColor { get; internal set; }

        public Vector2 PlayerColliderSize { get; internal set; }
        public Vector2 PlayerVisualSize { get; internal set; }
        public Vector2 MeleeColliderSize { get; internal set; }
        public Vector2 MeleeVisualSize { get; internal set; }
        public Vector2 LeaperColliderSize { get; internal set; }
        public Vector2 LeaperVisualSize { get; internal set; }
        public Vector2 CasterColliderSize { get; internal set; }
        public Vector2 CasterVisualSize { get; internal set; }
        public Vector2 BossColliderSize { get; internal set; }
        public Vector2 BossVisualSize { get; internal set; }

        // The hitbox anchor's, the sword's and the attack arc's local transforms are authored in
        // Scenes/Actors/Player.tscn and have no property here: a number nothing reads is worse than
        // a literal, because it looks like tuning.
        public float PlayerHitboxRadius { get; internal set; }
        public Vector2 PlayerHitboxOffset { get; internal set; }
        public Vector2 SwordSize { get; internal set; }

        public Vector2 PlayerHealthBarSize { get; internal set; }
        public Vector2 PlayerHealthBarOffset { get; internal set; }
        public Vector2 EnemyHealthBarSize { get; internal set; }
        public Vector2 BossHealthBarSize { get; internal set; }
        public Vector2 MeleeHealthBarOffset { get; internal set; }
        public Vector2 LeaperHealthBarOffset { get; internal set; }
        public Vector2 CasterHealthBarOffset { get; internal set; }
        public Vector2 BossHealthBarOffset { get; internal set; }

        /// <summary>Pixels the frame extends past the fill on every side.</summary>
        public float HealthBarFrameMargin { get; internal set; }

        /// <summary>Normalised health at or below which the fill lifts toward bone.</summary>
        public float HealthBarLowHealthThreshold { get; internal set; }

        /// <summary>How far toward bone the fill lifts, 0-1.</summary>
        public float HealthBarLowHealthTintBlend { get; internal set; }

        public Vector2 AttackArcSize { get; internal set; }
        public Vector2 MeleeDangerLocalPosition { get; internal set; }
        public Vector2 MeleeDangerSize { get; internal set; }
        public Vector2 LeapDangerLocalPosition { get; internal set; }
        public Vector2 LeapDangerSize { get; internal set; }
        public Vector2 CastDangerLocalPosition { get; internal set; }
        public Vector2 CastDangerSize { get; internal set; }
        public Vector2 BossSlashDangerLocalPosition { get; internal set; }
        public Vector2 BossSlashDangerSize { get; internal set; }
        public Vector2 BossSlamDangerLocalPosition { get; internal set; }
        public Vector2 BossSlamDangerSize { get; internal set; }

        public Vector2 MeleeRoleLocalPosition { get; internal set; }
        public Vector2 LeapRoleLocalPosition { get; internal set; }
        public Vector2 CastRoleLocalPosition { get; internal set; }
        public Vector2 BossRoleLocalPosition { get; internal set; }

        /// <summary>Unity <c>TextMesh.characterSize</c>, in pixels.</summary>
        public float WorldLabelCharacterSize { get; internal set; }

        /// <summary>Unity <c>TextMesh.fontSize</c> - a point size, not a distance, so it is not scaled.</summary>
        public int WorldLabelFontSize { get; internal set; }
        public float RoleMarkerCharacterSize { get; internal set; }
        public int RoleMarkerFontSize { get; internal set; }

        /// <summary>Where the lock-on disc hovers relative to its target, in pixels with +Y down.</summary>
        public Vector2 LockOnMarkerOffset { get; internal set; }

        /// <summary>The bright edge along the top of a platform: its thickness in pixels, and where it sits as a fraction of the platform's height above centre.</summary>
        public float PlatformRimThickness { get; internal set; }
        public float PlatformRimHeightFraction { get; internal set; }

        /// <summary>The slow breathing on every live mark: rad/s, a scale fraction, and the alpha floor and ceiling.</summary>
        public float MarkerPulseSpeed { get; internal set; }
        public float MarkerPulseAmount { get; internal set; }
        public float MarkerPulseAlphaMin { get; internal set; }
        public float MarkerPulseAlphaMax { get; internal set; }

        /// <summary>
        /// The pixel font size a Godot <see cref="Label"/> needs to render a world label at the height
        /// Unity's TextMesh did. Unity scaled its generated mesh by <c>characterSize / 10</c> and then
        /// by the font's point size, so the world height of a glyph was
        /// <c>fontSize * characterSize / 10</c> metres - and a metre is <see cref="World.Ppu"/> pixels.
        /// The character size is already in pixels here, hence no second <c>World.U</c>.
        /// </summary>
        public int WorldLabelFontSizePx => Mathf.RoundToInt(WorldLabelFontSize * WorldLabelCharacterSize * 0.1f);

        /// <summary>The same conversion for the role markers under each enemy.</summary>
        public int RoleMarkerFontSizePx => Mathf.RoundToInt(RoleMarkerFontSize * RoleMarkerCharacterSize * 0.1f);

        public int BackdropSortingOrder { get; internal set; }
        public int MoonSortingOrder { get; internal set; }
        public int DistantArchSortingOrder { get; internal set; }
        public int DistantArchMidSortingOrder { get; internal set; }
        public int GroundSortingOrder { get; internal set; }
        public int GroundRimSortingOrder { get; internal set; }
        public int GateSortingOrder { get; internal set; }
        public int PlatformSortingOrder { get; internal set; }
        public int PlatformRimSortingOrder { get; internal set; }
        public int SpiritPlatformSortingOrder { get; internal set; }
        public int ActorSortingOrder { get; internal set; }
        public int PlayerSortingOrder { get; internal set; }
        public int SwordSortingOrder { get; internal set; }
        public int ProjectileSortingOrder { get; internal set; }
        public int PlayerReadoutSortingOrder { get; internal set; }
        public int EnemyReadoutSortingOrder { get; internal set; }
        public int BossSlashReadoutSortingOrder { get; internal set; }
        public int BossSlamReadoutSortingOrder { get; internal set; }
        public int WorldLabelSortingOrder { get; internal set; }
        public int RoleMarkerSortingOrder { get; internal set; }

        /// <summary>
        /// The greybox read: the artist-owned palette (<c>Resources/Art/Readability.json</c>) and the
        /// designer-owned layout (<c>Resources/Design/ReadabilityLayout.json</c>) applied over the
        /// sorting orders. Two files because two owners. Null when either file is missing - the loader
        /// has already said which, and the bootstrap refuses to build on an incomplete catalog
        /// (PLAN_CLOSEOUT D1). There are no colours or sizes in code to fall back to.
        /// </summary>
        public static GameplayReadabilityDefaults Create()
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            if (catalog.ReadabilityTheme == null || catalog.ReadabilityLayout == null)
                return null;

            GameplayReadabilityDefaults defaults = WithSortingOrders();
            catalog.ReadabilityTheme.ApplyTo(defaults);
            catalog.ReadabilityLayout.ApplyTo(defaults);
            return defaults;
        }

        /// <summary>
        /// The twenty sorting orders, and nothing else. They are in neither file on purpose: a z-order
        /// is a contract between the things drawn - what covers what - and a wrong one is a bug rather
        /// than a taste, so it stays code (numbers audit §3.6, PLAN_CLOSEOUT D2). Every other number
        /// on this class comes from a file.
        /// </summary>
        private static GameplayReadabilityDefaults WithSortingOrders() => new()
        {
            BackdropSortingOrder = -100,
            MoonSortingOrder = -95,
            DistantArchSortingOrder = -90,
            DistantArchMidSortingOrder = -91,
            GroundSortingOrder = -1,
            GroundRimSortingOrder = 1,
            GateSortingOrder = 2,
            PlatformSortingOrder = 0,
            PlatformRimSortingOrder = 2,
            SpiritPlatformSortingOrder = 5,
            ActorSortingOrder = 5,
            PlayerSortingOrder = 10,
            SwordSortingOrder = 12,
            ProjectileSortingOrder = 10,
            PlayerReadoutSortingOrder = 8,
            EnemyReadoutSortingOrder = 3,
            BossSlashReadoutSortingOrder = 4,
            BossSlamReadoutSortingOrder = 2,
            WorldLabelSortingOrder = 25,
            RoleMarkerSortingOrder = 30,
        };
    }
}
