using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The arena the builders read: one flat object holding every position, size and bound the world is
    /// made of.
    ///
    /// UNITS - this is the conversion boundary for the arena. Everything the designer authored in
    /// metres with +Y up is <b>already in Godot pixels with +Y down</b> by the time it is a property on
    /// this object. One place writes it: <see cref="GameplaySceneLayoutData.ApplyTo"/>, which converts
    /// what it read out of <c>Resources/Design/SceneLayout*.json</c> through the <c>ToGodot*</c>
    /// helpers below. Nothing downstream of this class converts anything again, and nothing in this
    /// class holds a number of its own - the arena is the file or it is null.
    ///
    /// The one exception is <see cref="CameraOrthographicSize"/>, which stays in Unity units. It is not
    /// a position: Godot's Camera2D has no orthographic size at all, and the bootstrap turns it into a
    /// <c>Zoom</c> with <c>viewportHeight * 0.5f / (size * World.Ppu)</c>. Converting it here would
    /// make that formula wrong by a factor of a hundred.
    /// </summary>
    public sealed class GameplaySceneDefaults
    {
        /// <summary>
        /// Unity metres with +Y up -> Godot pixels with +Y down, for a <c>Vector3</c> position out of the
        /// design JSON; z was only ever draw order. The only place a position flips.
        /// </summary>
        internal static Vector2 ToGodot(Vector3 unityPosition) => World.V(new Vector2(unityPosition.X, unityPosition.Y));

        /// <summary>A size scales but never flips - there is no such thing as a negative height.</summary>
        internal static Vector2 ToGodotSize(Vector2 unitySize) => new Vector2(World.U(unitySize.X), World.U(unitySize.Y));

        /// <summary>
        /// A camera's horizontal clamp: same order, just scaled. <c>x</c> is still the left edge.
        /// </summary>
        internal static Vector2 ToGodotHorizontalBounds(Vector2 unityBounds) =>
            new Vector2(World.U(unityBounds.X), World.U(unityBounds.Y));

        /// <summary>
        /// A camera's vertical clamp. Flipping the axis turns the Unity minimum into the Godot maximum,
        /// so the pair swaps as well as changing sign: Unity <c>(0.5, 7.5)</c> is Godot <c>(-750, -50)</c>.
        /// A clamp written the other way round clamps everything to one line.
        /// </summary>
        internal static Vector2 ToGodotVerticalBounds(Vector2 unityBounds) =>
            new Vector2(-World.U(unityBounds.Y), -World.U(unityBounds.X));

        /// <summary>Unity <c>-8</c> is Godot <c>+800</c>, and "fell below" becomes "y greater than".</summary>
        internal static float ToGodotY(float unityY) => -World.U(unityY);

        public readonly struct PlatformDefinition
        {
            public PlatformDefinition(string name, Vector2 position, Vector2 size)
            {
                Name = name;
                Position = position;
                Size = size;
            }

            public string Name { get; }

            /// <summary>Godot pixels. Was a Unity <c>Vector3</c> in metres; z was draw order and is a sorting order now.</summary>
            public Vector2 Position { get; }
            public Vector2 Size { get; }
        }

        public readonly struct SceneryDefinition
        {
            public SceneryDefinition(string name, GameplayVisualFactory.SpriteKind spriteKind, Vector2 position, Vector2 size)
            {
                Name = name;
                SpriteKind = spriteKind;
                Position = position;
                Size = size;
            }

            public string Name { get; }
            public GameplayVisualFactory.SpriteKind SpriteKind { get; }
            public Vector2 Position { get; }
            public Vector2 Size { get; }
        }

        /// <summary>
        /// One placed enemy: where it stands, and optionally which <c>EnemyTuningData</c> file under
        /// <c>Resources/Design</c> it reads instead of its archetype's shared one. Empty is every
        /// placement shipped today.
        /// </summary>
        public readonly struct EnemySpawn
        {
            public EnemySpawn(Vector2 position, string dataFile)
            {
                Position = position;
                DataFile = string.IsNullOrWhiteSpace(dataFile) ? string.Empty : dataFile;
            }

            public Vector2 Position { get; }
            public string DataFile { get; }
        }

        public Vector2 CameraPosition { get; internal set; }

        /// <summary>
        /// Visible half-height in <b>Unity units</b> - deliberately unconverted, see the class remarks.
        /// </summary>
        public float CameraOrthographicSize { get; internal set; }
        public Vector2 CameraHorizontalBounds { get; internal set; }

        /// <summary>Godot pixels, and already flipped and swapped: <c>x</c> is the lower (more negative) bound.</summary>
        public Vector2 CameraVerticalBounds { get; internal set; }

        /// <summary>Godot pixels. A player has fallen out of the world when their y is <b>greater</b> than this.</summary>
        public float FallDeathY { get; internal set; }

        public Vector2 PlayerSpawnPosition { get; internal set; }

        /// <summary>
        /// Every bonfire in the chapter, in the order they are met. Never empty - a chapter that names
        /// none still gets the one it was written with - because index 0 is what a chapter starts at and
        /// what everything downstream falls back to.
        /// </summary>
        public Vector2[] CheckpointPositions { get; internal set; }

        /// <summary>The first checkpoint. Kept because it is what a one-checkpoint arena means.</summary>
        public Vector2 CheckpointPosition =>
            CheckpointPositions != null && CheckpointPositions.Length > 0 ? CheckpointPositions[0] : Vector2.Zero;

        public Vector2 BackdropPosition { get; internal set; }
        public Vector2 BackdropSize { get; internal set; }
        public Vector2 GroundPosition { get; internal set; }
        public Vector2 GroundSize { get; internal set; }
        public Vector2 GroundRimPosition { get; internal set; }
        public Vector2 GroundRimSize { get; internal set; }
        public Vector2 LeftArenaGatePosition { get; internal set; }
        public Vector2 RightArenaGatePosition { get; internal set; }
        public Vector2 ArenaGateSize { get; internal set; }

        public Vector2 CheckpointLabelPosition { get; internal set; }
        public Vector2 DuelFloorLabelPosition { get; internal set; }
        public Vector2 WrathAltarLabelPosition { get; internal set; }

        public Vector2 SpiritPlatformPosition { get; internal set; }
        public Vector2 SpiritPlatformSize { get; internal set; }
        public bool SpiritPlatformStartsActive { get; internal set; }

        public EnemySpawn[] MeleeGruntSpawns { get; internal set; }
        public EnemySpawn[] LeapingAttackerSpawns { get; internal set; }
        public EnemySpawn[] RangedCasterSpawns { get; internal set; }

        // The position-only views the arena was described with before a placement could carry its own
        // tuning file. Derived rather than stored, so there is still one list per archetype.
        public Vector2[] MeleeGruntSpawnPositions => PositionsOf(MeleeGruntSpawns);
        public Vector2 LeapingAttackerSpawnPosition => FirstPositionOf(LeapingAttackerSpawns);
        public Vector2 RangedCasterSpawnPosition => FirstPositionOf(RangedCasterSpawns);

        private static Vector2[] PositionsOf(EnemySpawn[] spawns)
        {
            if (spawns == null)
                return System.Array.Empty<Vector2>();

            var positions = new Vector2[spawns.Length];
            for (int i = 0; i < spawns.Length; i++)
                positions[i] = spawns[i].Position;

            return positions;
        }

        private static Vector2 FirstPositionOf(EnemySpawn[] spawns)
        {
            return spawns != null && spawns.Length > 0 ? spawns[0].Position : Vector2.Zero;
        }

        /// <summary>
        /// The one-way door back to the checkpoint, if this chapter has one. Off is every arena shipped
        /// before 300-unit levels existed, and off means no door is built at all - and that the two
        /// properties after this one are never read.
        /// </summary>
        public bool HasShortcutGate { get; internal set; }
        public Vector2 ShortcutGatePosition { get; internal set; }
        public Vector2 ShortcutGateSize { get; internal set; }

        /// <summary>
        /// Which side of that door is the far one - the side the level makes the player earn. True is
        /// the right (+x), which is the only shape authored so far: bonfire, door, boss, left to right.
        /// </summary>
        public bool ShortcutOpensFromRight { get; internal set; }

        /// <summary>
        /// Whether the arena holds anything besides its boss. False is a room that is only the fight.
        /// </summary>
        public bool SpawnApproachEnemies { get; internal set; }
        public Vector2 WrathMiniBossSpawnPosition { get; internal set; }

        /// <summary>
        /// Which boss stands in this arena. Empty means the shipped chapter-one fight,
        /// <c>MyGame.Enemy.WrathMiniBoss</c>; anything else names a
        /// <c>RainbowChapterBossData</c> file under <c>Resources/Design</c>, and the spawner builds an
        /// authored chapter boss instead. The arena decides, because a boss without a room to stand in
        /// is not an encounter.
        /// </summary>
        public string BossDataFile { get; internal set; } = string.Empty;

        /// <summary>
        /// The encounter around that boss - arena reach, intro patience, punish window - as a
        /// <c>BossEncounterData</c> file under <c>Resources/Design</c>. Empty means chapter one's, which
        /// is what every arena used before there was a second chapter to want its own.
        /// </summary>
        public string BossEncounterFile { get; internal set; } = string.Empty;

        public PlatformDefinition[] Platforms { get; internal set; }
        public SceneryDefinition[] BackdropScenery { get; internal set; }

        /// <summary>
        /// The shared arena, <c>Resources/Design/SceneLayout.json</c>, converted. Null when that file is
        /// missing: the loader has already said so, and the bootstrap refuses to build on an incomplete
        /// catalog (PLAN_CLOSEOUT D1). There is no arena in code to fall back to.
        /// </summary>
        public static GameplaySceneDefaults Create() => FromLayout(GameplayTuningCatalog.Load().SceneLayout);

        /// <summary>
        /// The arena for the open scene - <c>SceneLayout_&lt;SceneName&gt;.json</c> if there is one,
        /// otherwise the shared <c>SceneLayout.json</c> - with the per-scene asset's narrow override
        /// applied last, so it wins. The asset being absent is normal; the file is not.
        /// </summary>
        public static GameplaySceneDefaults CreateFromAsset(GameplaySceneDefaultsAsset asset)
        {
            return CreateForScene(GameplayBuildShim.ActiveSceneName, asset);
        }

        /// <summary>
        /// The scene-name-explicit form. A tool building assets for a scene it has not opened - and a
        /// test asking what a second arena would look like - has no active scene to read.
        /// </summary>
        public static GameplaySceneDefaults CreateForScene(string sceneName, GameplaySceneDefaultsAsset asset)
        {
            GameplaySceneDefaults defaults = FromLayout(GameplayTuningCatalog.Load().SceneLayoutFor(sceneName));
            asset?.ApplyTo(defaults);
            return defaults;
        }

        private static GameplaySceneDefaults FromLayout(GameplaySceneLayoutData layout)
        {
            if (layout == null)
                return null;

            var defaults = new GameplaySceneDefaults();
            layout.ApplyTo(defaults);
            return defaults;
        }

        /// <summary>Both arguments are already in Godot space; the asset converts before it calls.</summary>
        internal void OverrideCamera(Vector2 position, float orthographicSize)
        {
            CameraPosition = position;
            CameraOrthographicSize = orthographicSize > 0f ? orthographicSize : CameraOrthographicSize;
        }

        /// <summary>
        /// The per-scene asset's narrow spawn override. It carries one checkpoint and always did, so it
        /// replaces the list rather than editing it - a scene asset that names a checkpoint is asserting
        /// the whole spawn, and silently keeping bonfires it has never heard of would be the stranger
        /// answer.
        /// </summary>
        internal void OverrideSpawn(Vector2 playerSpawnPosition, Vector2 checkpointPosition)
        {
            PlayerSpawnPosition = playerSpawnPosition;
            CheckpointPositions = new[] { checkpointPosition };
        }
    }
}
