using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The arena the builders read: one flat object holding every position, size and bound the world is
    /// made of.
    ///
    /// UNITS - this is the conversion boundary for the arena. Everything the Unity project authored in
    /// metres with +Y up is <b>already in Godot pixels with +Y down</b> by the time it is a property on
    /// this object. Two places write it and both convert: the literals in <see cref="Create"/> (through
    /// <see cref="ToGodot(float,float)"/> and <see cref="ToGodotSize"/>) and
    /// <see cref="GameplaySceneLayoutData.ApplyTo"/>, which converts what it read out of the design
    /// JSON. Nothing downstream of this class converts anything again.
    ///
    /// The one exception is <see cref="CameraOrthographicSize"/>, which stays in Unity units. It is not
    /// a position: Godot's Camera2D has no orthographic size at all, and the bootstrap turns it into a
    /// <c>Zoom</c> with <c>viewportHeight * 0.5f / (size * World.Ppu)</c>. Converting it here would
    /// make that formula wrong by a factor of a hundred.
    /// </summary>
    public sealed class GameplaySceneDefaults
    {
        /// <summary>Unity metres with +Y up -> Godot pixels with +Y down. The only place a position flips.</summary>
        internal static Vector2 ToGodot(float unityX, float unityY) => World.V(new Vector2(unityX, unityY));

        /// <summary>The same for a Unity <c>Vector3</c> position out of the design JSON; z was only ever draw order.</summary>
        internal static Vector2 ToGodot(Vector3 unityPosition) => World.V(new Vector2(unityPosition.X, unityPosition.Y));

        /// <summary>A size scales but never flips - there is no such thing as a negative height.</summary>
        internal static Vector2 ToGodotSize(Vector2 unitySize) => new Vector2(World.U(unitySize.X), World.U(unitySize.Y));

        internal static Vector2 ToGodotSize(float unityWidth, float unityHeight) =>
            new Vector2(World.U(unityWidth), World.U(unityHeight));

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
        /// before 300-unit levels existed, and off means no door is built at all.
        /// </summary>
        public bool HasShortcutGate { get; internal set; }
        public Vector2 ShortcutGatePosition { get; internal set; }
        public Vector2 ShortcutGateSize { get; internal set; } = ToGodotSize(0.32f, 4.6f);

        /// <summary>
        /// Which side of that door is the far one - the side the level makes the player earn. True is
        /// the right (+x), which is the only shape authored so far: bonfire, door, boss, left to right.
        /// </summary>
        public bool ShortcutOpensFromRight { get; internal set; } = true;

        /// <summary>
        /// Whether the arena holds anything besides its boss. False is a room that is only the fight.
        /// </summary>
        public bool SpawnApproachEnemies { get; internal set; } = true;
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

        public static GameplaySceneDefaults Create()
        {
            // Every literal below is the Unity number, converted on the spot. Read them as metres.
            return new GameplaySceneDefaults
            {
                CameraPosition = ToGodot(0f, 2f),
                CameraOrthographicSize = 6.8f,
                CameraHorizontalBounds = ToGodotHorizontalBounds(new Vector2(-4f, 34f)),
                CameraVerticalBounds = ToGodotVerticalBounds(new Vector2(0.5f, 7.5f)),
                FallDeathY = ToGodotY(-8f),

                PlayerSpawnPosition = ToGodot(-2f, 0.5f),
                CheckpointPositions = new[] { ToGodot(-2f, 0.5f) },

                BackdropPosition = ToGodot(13f, 2f),
                BackdropSize = ToGodotSize(48f, 22f),
                GroundPosition = ToGodot(13f, -1f),
                GroundSize = ToGodotSize(44f, 1f),
                GroundRimPosition = ToGodot(13f, -0.43f),
                GroundRimSize = ToGodotSize(44f, 0.08f),
                LeftArenaGatePosition = ToGodot(-8.7f, 1.35f),
                RightArenaGatePosition = ToGodot(35.8f, 1.35f),
                ArenaGateSize = ToGodotSize(0.32f, 4.6f),

                CheckpointLabelPosition = ToGodot(-2f, 1.8f),
                DuelFloorLabelPosition = ToGodot(7f, 0.35f),
                WrathAltarLabelPosition = ToGodot(31f, 3.1f),

                SpiritPlatformPosition = ToGodot(20f, 5.5f),
                SpiritPlatformSize = ToGodotSize(3f, 0.3f),
                SpiritPlatformStartsActive = false,

                MeleeGruntSpawns = new[]
                {
                    new EnemySpawn(ToGodot(6f, 0.5f), null),
                    new EnemySpawn(ToGodot(13f, 0.5f), null),
                    new EnemySpawn(ToGodot(21f, 0.5f), null),
                },
                LeapingAttackerSpawns = new[] { new EnemySpawn(ToGodot(16f, 0.5f), null) },
                RangedCasterSpawns = new[] { new EnemySpawn(ToGodot(24f, 0.5f), null) },
                WrathMiniBossSpawnPosition = ToGodot(29.5f, 1f),

                Platforms = new[]
                {
                    new PlatformDefinition("LowerPlatform", ToGodot(4f, 2.5f), ToGodotSize(4f, 0.4f)),
                    new PlatformDefinition("UpperPlatform", ToGodot(12f, 4f), ToGodotSize(4.5f, 0.4f)),
                    new PlatformDefinition("MidBridgePlatform", ToGodot(19f, 2.4f), ToGodotSize(4f, 0.4f)),
                    new PlatformDefinition("RightPlatform", ToGodot(28f, 2f), ToGodotSize(4f, 0.4f)),
                    new PlatformDefinition("CheckpointRunPlatform", ToGodot(-4.9f, 1.45f), ToGodotSize(2.6f, 0.35f)),
                },
                BackdropScenery = new[]
                {
                    new SceneryDefinition("Moon", GameplayVisualFactory.SpriteKind.Disc, ToGodot(27f, 7.5f), ToGodotSize(2.2f, 2.2f)),
                    new SceneryDefinition("DistantArchLeft", GameplayVisualFactory.SpriteKind.Arch, ToGodot(1.2f, 1.2f), ToGodotSize(2.5f, 5.5f)),
                    new SceneryDefinition("DistantArchMid", GameplayVisualFactory.SpriteKind.Arch, ToGodot(10f, 1.7f), ToGodotSize(3.5f, 6.5f)),
                    new SceneryDefinition("DistantArchFar", GameplayVisualFactory.SpriteKind.Arch, ToGodot(20f, 1.35f), ToGodotSize(3.2f, 6f)),
                    new SceneryDefinition("DistantArchRight", GameplayVisualFactory.SpriteKind.Arch, ToGodot(31f, 1.1f), ToGodotSize(3f, 5.8f)),
                },
            };
        }

        /// <summary>
        /// Two layers over the shipped values, applied in order. The layout JSON is the arena a designer
        /// edits as a diff - <c>SceneLayout_&lt;SceneName&gt;.json</c> for the open scene if there is one,
        /// otherwise the shared <c>SceneLayout.json</c>. The per-scene asset is the narrow override a
        /// scene carries for itself, so it goes last and wins. Either being absent is normal.
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
            GameplaySceneDefaults defaults = Create();

            GameplayTuningCatalog.Load()?.SceneLayoutFor(sceneName)?.ApplyTo(defaults);
            asset?.ApplyTo(defaults);

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
