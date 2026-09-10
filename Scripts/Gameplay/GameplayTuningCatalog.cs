using System.Collections.Generic;
using Godot;
using MyGame.Combat;
using MyGame.Enemy;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Runtime view over the tuning numbers that designers own.
    /// The source of truth is plain JSON under <see cref="DesignResourceFolder"/>, so tuning can be
    /// edited and reviewed without opening an editor; this type only turns that JSON into the typed
    /// objects the gameplay code expects. A missing file yields null so callers keep falling back to
    /// <see cref="GameplayTuningDefaults"/>.
    ///
    /// UNITS: nothing is converted here. Every entry is loaded through its own type's <c>Load</c>,
    /// which is where that type's metres-to-pixels conversion lives, so the catalog cannot double-scale
    /// anything by touching it.
    /// </summary>
    /// <remarks>
    /// A plain C# class rather than a <see cref="Resource"/>: Unity made it a ScriptableObject only so
    /// the fields would show in an inspector, and there is no asset for it - <see cref="Load"/> builds
    /// it from JSON every time.
    /// </remarks>
    public sealed class GameplayTuningCatalog
    {
        public const string DesignResourceFolder = "Design/";

        /// <summary>
        /// The palette is authored by `artist`, whose folder is <c>Resources/Art</c>; the design folder
        /// belongs to `designer`. Loading it from its owner's folder keeps the ownership split in
        /// Ownership.md true of the filesystem rather than only of the docs.
        /// </summary>
        public const string ArtResourceFolder = "Art/";

        public const string ReadabilityThemeFile = "Readability";

        private PlayerMovementData playerMovement;
        private PlayerCombatData playerCombat;
        private PlayerResourceData playerResources;
        private ProgressionTuningData progression;

        private SinTuningData sinTuning;
        private WorldTuningData worldTuning;
        private GameplaySceneLayoutData sceneLayout;
        private GameplayReadabilityThemeData readabilityTheme;

        private MeleeGruntData meleeGrunt;
        private LeapingAttackerData leapingAttacker;
        private RangedCasterData rangedCaster;
        private WrathMiniBossData wrathMiniBoss;
        private BossEncounterData wrathEncounter;

        public PlayerMovementData PlayerMovement => playerMovement;
        public PlayerCombatData PlayerCombat => playerCombat;
        public PlayerResourceData PlayerResources => playerResources;

        /// <summary>
        /// What souls buy. Null when the design file is missing, like every other entry here;
        /// <c>ProgressionTuningData.Load</c> is the path that falls back to defaults instead,
        /// because <c>PlayerProgression</c> has save-backed levels it must still be able to apply.
        /// </summary>
        public ProgressionTuningData Progression => progression;
        public SinTuningData SinTuning => sinTuning;
        public WorldTuningData WorldTuning => worldTuning;
        public GameplaySceneLayoutData SceneLayout => sceneLayout;
        public GameplayReadabilityThemeData ReadabilityTheme => readabilityTheme;

        /// <summary>
        /// A chapter boss's authored stats and attacks, by design file name. Not a stored field like
        /// the rest: there is one of these per chapter, and the arena names the one it wants rather than
        /// the catalog holding all seven.
        /// </summary>
        public RainbowChapterBossData ChapterBoss(string fileName)
        {
            return string.IsNullOrEmpty(fileName)
                ? null
                : RainbowChapterBossData.Load(DesignResourceFolder + fileName);
        }

        /// <summary>
        /// One chapter's encounter - arena reach, intro patience, punish window - by design file name.
        /// Same shape as <see cref="ChapterBoss"/> and for the same reason: the arena names the one it
        /// wants rather than the catalog carrying all seven.
        /// </summary>
        public BossEncounterData ChapterEncounter(string fileName)
        {
            return string.IsNullOrEmpty(fileName)
                ? null
                : BossEncounterData.Load(DesignResourceFolder + fileName);
        }

        /// <summary>
        /// The layout for one scene: <c>SceneLayout_&lt;SceneName&gt;.json</c> if it exists, otherwise the
        /// shared <c>SceneLayout.json</c>. That is the whole of what makes a second arena an authoring
        /// job - a JSON file named after the scene and a scene to open it in, with no code involved.
        /// </summary>
        /// <remarks>
        /// Cached per name because it is asked for on every bootstrap and by two tools, and a miss costs
        /// a resource lookup that returns null every time.
        /// </remarks>
        public GameplaySceneLayoutData SceneLayoutFor(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return sceneLayout;

            if (_sceneLayouts.TryGetValue(sceneName, out GameplaySceneLayoutData cached))
                return cached;

            GameplaySceneLayoutData layout =
                GameplaySceneLayoutData.Load(DesignResourceFolder + SceneLayoutFile + "_" + sceneName) ?? sceneLayout;

            _sceneLayouts[sceneName] = layout;
            return layout;
        }

        private readonly Dictionary<string, GameplaySceneLayoutData> _sceneLayouts = new();

        public MeleeGruntData MeleeGrunt => meleeGrunt;
        public LeapingAttackerData LeapingAttacker => leapingAttacker;
        public RangedCasterData RangedCaster => rangedCaster;
        public WrathMiniBossData WrathMiniBoss => wrathMiniBoss;
        public BossEncounterData WrathEncounter => wrathEncounter;

        /// <summary>Design file names, shared with the tool that writes them.</summary>
        public const string PlayerMovementFile = PlayerMovementData.FileName;
        public const string PlayerCombatFile = PlayerCombatData.FileName;
        public const string PlayerResourcesFile = PlayerResourceData.FileName;

        /// <summary>Spelled once, on the type, because the Player code loads the same file directly.</summary>
        public const string ProgressionTuningFile = ProgressionTuningData.FileName;
        public const string SinTuningFile = "SinTuning";
        public const string WorldTuningFile = "WorldTuning";
        public const string SceneLayoutFile = "SceneLayout";
        public const string MeleeGruntFile = "MeleeGrunt";
        public const string LeapingAttackerFile = "LeapingAttacker";
        public const string RangedCasterFile = "RangedCaster";
        public const string WrathMiniBossFile = "WrathMiniBoss";
        public const string WrathEncounterFile = "WrathEncounter";

        private static GameplayTuningCatalog _cached;

        public static GameplayTuningCatalog Load()
        {
            if (_cached != null)
                return _cached;

            var catalog = new GameplayTuningCatalog
            {
                playerMovement = PlayerMovementData.Load(),
                playerCombat = PlayerCombatData.Load(),
                playerResources = PlayerResourceData.Load(),

                // Deliberately not ProgressionTuningData.Load(): that one substitutes defaults for a
                // missing file, and this property's contract is null. See the Progression remarks.
                progression = MyGame.Core.Res.LoadJson<ProgressionTuningData>(DesignResourceFolder + ProgressionTuningFile),

                sinTuning = SinTuningData.Load(DesignResourceFolder + SinTuningFile),
                worldTuning = WorldTuningData.Load(DesignResourceFolder + WorldTuningFile),
                sceneLayout = GameplaySceneLayoutData.Load(DesignResourceFolder + SceneLayoutFile),
                readabilityTheme = GameplayReadabilityThemeData.Load(ArtResourceFolder + ReadabilityThemeFile),
                meleeGrunt = MeleeGruntData.Load(DesignResourceFolder + MeleeGruntFile),
                leapingAttacker = LeapingAttackerData.Load(DesignResourceFolder + LeapingAttackerFile),
                rangedCaster = RangedCasterData.Load(DesignResourceFolder + RangedCasterFile),
                wrathMiniBoss = WrathMiniBossData.Load(DesignResourceFolder + WrathMiniBossFile),
                wrathEncounter = BossEncounterData.Load(DesignResourceFolder + WrathEncounterFile),
            };

            _cached = catalog;
            return _cached;
        }

        /// <summary>Drops the cache so an edited design file is picked up without a restart.</summary>
        public static void Reload()
        {
            _cached = null;
        }
    }
}
