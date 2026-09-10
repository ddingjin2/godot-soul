using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// How the greybox reads: what colour everything is, how big it is, where it sits relative to the
    /// actor wearing it, and what draws in front of what.
    ///
    /// UNITS - this is one of the two conversion boundaries in the arena code (the other is
    /// <see cref="GameplaySceneDefaults"/>). Everything in <see cref="CreateBase"/> is authored in
    /// Unity metres with +Y up and converted <b>here, once</b>:
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

        public Vector2 PlayerHitboxAnchorLocalPosition { get; internal set; }
        public float PlayerHitboxRadius { get; internal set; }
        public Vector2 PlayerHitboxOffset { get; internal set; }
        public Vector2 SwordLocalPosition { get; internal set; }

        /// <summary>
        /// Was Unity's <c>SwordLocalEulerAngles</c>, a Vector3 whose x and y were always zero. Godot's
        /// <c>Node2D.Rotation</c> is a single value in radians, and it turns the other way because +Y
        /// is down - so Unity's -28 degrees is +28 here.
        /// </summary>
        public float SwordLocalRotation { get; internal set; }
        public Vector2 SwordSize { get; internal set; }

        public Vector2 PlayerHealthBarSize { get; internal set; }
        public Vector2 PlayerHealthBarOffset { get; internal set; }
        public Vector2 EnemyHealthBarSize { get; internal set; }
        public Vector2 BossHealthBarSize { get; internal set; }
        public Vector2 MeleeHealthBarOffset { get; internal set; }
        public Vector2 LeaperHealthBarOffset { get; internal set; }
        public Vector2 CasterHealthBarOffset { get; internal set; }
        public Vector2 BossHealthBarOffset { get; internal set; }

        public Vector2 AttackArcLocalPosition { get; internal set; }
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
        /// The greybox read, with the artist-owned palette applied over it. Sizes, offsets and sorting
        /// orders are deliberately not in that file: they are readability engineering - what overlaps
        /// what, how big a danger zone has to be to be seen - and moving them would hand out a knob that
        /// silently breaks the reads the whole slice is built to prove. Colour is the palette; the rest
        /// is layout.
        /// </summary>
        public static GameplayReadabilityDefaults Create()
        {
            GameplayReadabilityDefaults defaults = CreateBase();
            GameplayTuningCatalog.Load()?.ReadabilityTheme?.ApplyTo(defaults);
            return defaults;
        }

        // The three unit conversions, spelled once so every literal below can stay the Unity number a
        // designer authored and can still be diffed against the Unity source line for line.
        private static Vector2 P(float x, float y) => World.V(new Vector2(x, y));
        private static Vector2 S(float x, float y) => new Vector2(World.U(x), World.U(y));

        /// <summary>
        /// The shipped values with no theme applied. This is what <c>Readability.json</c> is generated
        /// from, so the file starts life identical to the code and any drift is the artist's edit rather
        /// than a transcription mistake.
        /// </summary>
        public static GameplayReadabilityDefaults CreateBase()
        {
            return new GameplayReadabilityDefaults
            {
                BackgroundColor = new Color(0.043137256f, 0.047058824f, 0.05882353f),
                GroundColor = new Color(0.12156863f, 0.11764706f, 0.105882354f),
                PlatformColor = new Color(0.16470589f, 0.15686275f, 0.14509805f),
                PlayerColor = new Color(0.68235296f, 0.72156864f, 0.7607843f),
                EnemyColor = new Color(0.41960785f, 0.22745098f, 0.2f),
                LeaperColor = new Color(0.47843137f, 0.29411766f, 0.17254902f),
                CasterColor = new Color(0.29411766f, 0.27058825f, 0.3764706f),
                BossColor = new Color(0.54901963f, 0.20392157f, 0.15686275f),

                MoonColor = new Color(0.43137255f, 0.45490196f, 0.5019608f, 0.42f),
                DistantArchColor = new Color(0.08235294f, 0.08627451f, 0.105882354f, 0.9f),
                DistantArchMidColor = new Color(0.0627451f, 0.06666667f, 0.08627451f, 0.95f),
                GroundRimColor = new Color(0.43137255f, 0.40784314f, 0.36078432f, 0.5f),
                ArenaGateColor = new Color(0.2f, 0.1882353f, 0.16862746f),
                PlatformRimColor = new Color(0.36862746f, 0.34901962f, 0.30588236f, 0.45f),
                SpiritPlatformColor = new Color(0.36862746f, 0.41960785f, 0.45882353f, 0.6f),
                SwordColor = new Color(0.5568628f, 0.54901963f, 0.50980395f),
                ProjectileColor = new Color(0.86f, 0.42f, 1f),

                CheckpointLabelColor = new Color(0.5176471f, 0.57254905f, 0.627451f),
                DuelFloorLabelColor = new Color(0.43137255f, 0.40784314f, 0.36078432f),
                WrathAltarLabelColor = new Color(0.65882355f, 0.29411766f, 0.2f),

                PlayerAttackReadoutColor = new Color(0.82f, 0.9f, 1f, 0.16f),
                MeleeDangerColor = new Color(1f, 0.08f, 0.04f, 0.2f),
                LeapDangerColor = new Color(1f, 0.62f, 0.08f, 0.18f),
                CastDangerColor = new Color(0.62f, 0.35f, 1f, 0.24f),
                BossSlashDangerColor = new Color(1f, 0.06f, 0.02f, 0.22f),
                BossSlamDangerColor = new Color(1f, 0.22f, 0.02f, 0.13f),

                PlayerHealthBarColor = new Color(0.5176471f, 0.57254905f, 0.627451f),
                EnemyHealthBarColor = new Color(0.49411765f, 0.18039216f, 0.13333334f),
                BossHealthBarColor = new Color(0.65882355f, 0.29411766f, 0.2f),
                MeleeRoleColor = new Color(0.43137255f, 0.40784314f, 0.36078432f),
                LeapRoleColor = new Color(0.43137255f, 0.40784314f, 0.36078432f),
                CastRoleColor = new Color(0.43137255f, 0.40784314f, 0.36078432f),
                BossRoleColor = new Color(0.43137255f, 0.40784314f, 0.36078432f),

                PlayerColliderSize = S(0.58f, 1.28f),
                PlayerVisualSize = S(0.9f, 1.45f),
                MeleeColliderSize = S(0.6f, 1f),
                MeleeVisualSize = S(0.88f, 1.18f),
                LeaperColliderSize = S(0.55f, 0.9f),
                LeaperVisualSize = S(0.9f, 1.08f),
                CasterColliderSize = S(0.5f, 0.9f),
                CasterVisualSize = S(0.82f, 1.1f),
                BossColliderSize = S(1.2f, 2f),
                BossVisualSize = S(1.75f, 2.35f),

                PlayerHitboxAnchorLocalPosition = P(0.4f, 0f),
                PlayerHitboxRadius = World.U(0.4f),
                PlayerHitboxOffset = P(0.4f, 0f),
                SwordLocalPosition = P(0.12f, 0.22f),
                // Unity: new Vector3(0f, 0f, -28f) euler. Sign flips with the Y axis.
                SwordLocalRotation = Mathf.DegToRad(28f),
                SwordSize = S(0.95f, 0.22f),

                PlayerHealthBarSize = S(1.2f, 0.12f),
                PlayerHealthBarOffset = P(0f, 1.24f),
                EnemyHealthBarSize = S(1f, 0.1f),
                BossHealthBarSize = S(2.25f, 0.16f),
                MeleeHealthBarOffset = P(0f, 0.9f),
                LeaperHealthBarOffset = P(0f, 0.86f),
                CasterHealthBarOffset = P(0f, 0.84f),
                BossHealthBarOffset = P(0f, 1.65f),

                AttackArcLocalPosition = Vector2.Zero,
                AttackArcSize = S(1.05f, 0.75f),
                MeleeDangerLocalPosition = P(0.55f, 0f),
                MeleeDangerSize = S(1.55f, 1.05f),
                LeapDangerLocalPosition = P(0f, -0.08f),
                LeapDangerSize = S(1.9f, 1.25f),
                CastDangerLocalPosition = P(0f, -0.1f),
                CastDangerSize = S(2.35f, 0.22f),
                BossSlashDangerLocalPosition = P(0.85f, 0f),
                BossSlashDangerSize = S(2.7f, 1.55f),
                BossSlamDangerLocalPosition = P(0f, -0.15f),
                BossSlamDangerSize = S(4.2f, 1.45f),

                MeleeRoleLocalPosition = P(0f, -0.72f),
                LeapRoleLocalPosition = P(0f, -0.68f),
                CastRoleLocalPosition = P(0f, -0.68f),
                BossRoleLocalPosition = P(0f, -1.35f),
                WorldLabelCharacterSize = World.U(0.22f),
                WorldLabelFontSize = 42,
                RoleMarkerCharacterSize = World.U(0.16f),
                RoleMarkerFontSize = 34,

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
}
