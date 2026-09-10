using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// One platform in a layout file. Was <c>GameplaySceneLayoutData.PlatformRow</c>, a nested
    /// <c>[System.Serializable]</c> class; Godot cannot export an array of a nested Godot type, so the
    /// row is a top-level <see cref="Resource"/> now. The JSON shape is unchanged.
    /// </summary>
    public sealed partial class GameplayPlatformRow : Resource
    {
        [Export] public string name = "Platform";

        /// <summary>Unity metres, +Y up - straight out of the design file. Converted by <see cref="GameplaySceneLayoutData.ApplyTo"/>.</summary>
        [Export] public Vector3 position;
        [Export] public Vector2 size = new Vector2(4f, 0.4f);
    }

    /// <summary>
    /// One backdrop prop. Was <c>GameplaySceneLayoutData.SceneryRow</c>; top-level for the same reason
    /// as <see cref="GameplayPlatformRow"/>.
    /// </summary>
    public sealed partial class GameplaySceneryRow : Resource
    {
        [Export] public string name = "Scenery";

        /// <summary>
        /// Name of a <see cref="GameplayVisualFactory.SpriteKind"/> value - Disc, Arch and so on.
        /// Spelled out rather than serialized as a number so the file stays readable when the enum
        /// gains a value.
        /// </summary>
        [Export] public string spriteKind = "Disc";

        /// <summary>Unity metres, +Y up.</summary>
        [Export] public Vector3 position;
        [Export] public Vector2 size = Vector2.One;
    }

    /// <summary>
    /// One placed enemy. A row rather than a bare position so a placement can name its own tuning file
    /// - the same way an arena names its boss - which is what makes an enemy variant a JSON file
    /// instead of a code change. Was <c>GameplaySceneLayoutData.EnemySpawnRow</c>.
    /// </summary>
    public sealed partial class GameplayEnemySpawnRow : Resource
    {
        /// <summary>Unity metres, +Y up.</summary>
        [Export] public Vector3 position;

        /// <summary>
        /// Name of an <c>EnemyTuningData</c> file under <c>Resources/Design</c>, without the extension.
        /// Empty uses the shared archetype tuning, which is what every placement does today.
        /// </summary>
        [Export] public string dataFile = "";
    }

    /// <summary>
    /// The arena, as data. Ground, platforms, scenery, gates, labels, spawn points, camera framing and
    /// the fall line all lived in <see cref="GameplaySceneDefaults.Create"/> as literals, which made a
    /// second arena a code change - the single thing most in the way of chapter two.
    ///
    /// Every field is optional in the sense that matters: <see cref="ApplyTo"/> only writes a section
    /// when its own flag is set, so a designer can move the platforms without inheriting responsibility
    /// for the camera. What is not overridden keeps the shipped values, and a missing file changes
    /// nothing at all.
    ///
    /// UNITS: every field here is the <b>authored Unity number</b> - metres, +Y up - because that is
    /// what is in <c>Resources/Design/SceneLayout*.json</c> and the file is the source of truth. The
    /// conversion to Godot pixels with +Y down happens once, in <see cref="ApplyTo"/>, through the
    /// <c>GameplaySceneDefaults.ToGodot*</c> helpers. Nothing else in the arena code converts.
    /// <c>cameraOrthographicSize</c> is the one field that crosses unconverted; see the remarks on
    /// <see cref="GameplaySceneDefaults"/>.
    /// </summary>
    public sealed partial class GameplaySceneLayoutData : Resource
    {
        // Camera
        [Export] public bool overrideCamera;
        [Export] public Vector3 cameraPosition = new Vector3(0f, 2f, -10f);
        [Export] public float cameraOrthographicSize = 6.8f;
        [Export] public Vector2 cameraHorizontalBounds = new Vector2(-4f, 34f);
        [Export] public Vector2 cameraVerticalBounds = new Vector2(0.5f, 7.5f);
        [Export] public float fallDeathY = -8f;

        // Spawn
        [Export] public bool overrideSpawn;
        [Export] public Vector3 playerSpawnPosition = new Vector3(-2f, 0.5f, 0f);

        /// <summary>
        /// The single checkpoint every arena was written with. Kept as the fallback for
        /// <see cref="checkpointPositions"/>, not as a second way to author one: every shipped
        /// SceneLayout names this field and nothing else.
        /// </summary>
        [Export] public Vector3 checkpointPosition = new Vector3(-2f, 0.5f, 0f);

        /// <summary>
        /// Every bonfire in the chapter, in the order they are met. Empty falls back to
        /// <see cref="checkpointPosition"/>, which is exactly the one-checkpoint arena this file has
        /// always described. Index 0 is where the chapter starts, and the order is what the save
        /// records a rest as.
        /// </summary>
        [Export] public Vector3[] checkpointPositions;

        // Terrain
        [Export] public bool overrideTerrain;
        [Export] public Vector3 backdropPosition = new Vector3(13f, 2f, 10f);
        [Export] public Vector2 backdropSize = new Vector2(48f, 22f);
        [Export] public Vector3 groundPosition = new Vector3(13f, -1f, 0f);
        [Export] public Vector2 groundSize = new Vector2(44f, 1f);
        [Export] public Vector3 groundRimPosition = new Vector3(13f, -0.43f, -0.02f);
        [Export] public Vector2 groundRimSize = new Vector2(44f, 0.08f);
        [Export] public Vector3 leftArenaGatePosition = new Vector3(-8.7f, 1.35f, 0f);
        [Export] public Vector3 rightArenaGatePosition = new Vector3(35.8f, 1.35f, 0f);
        [Export] public Vector2 arenaGateSize = new Vector2(0.32f, 4.6f);

        // Markers
        [Export] public bool overrideMarkers;
        [Export] public Vector3 checkpointLabelPosition = new Vector3(-2f, 1.8f, 0f);
        [Export] public Vector3 duelFloorLabelPosition = new Vector3(7f, 0.35f, 0f);
        [Export] public Vector3 wrathAltarLabelPosition = new Vector3(31f, 3.1f, 0f);
        [Export] public Vector3 spiritPlatformPosition = new Vector3(20f, 5.5f, 0f);
        [Export] public Vector2 spiritPlatformSize = new Vector2(3f, 0.3f);
        [Export] public bool spiritPlatformStartsActive;

        // Enemy Placement
        [Export] public bool overrideEnemyPlacement;

        /// <summary>The position-only grunt list every shipped SceneLayout is written with. Kept as the fallback for <see cref="meleeGruntSpawns"/>.</summary>
        [Export] public Vector3[] meleeGruntSpawnPositions;

        /// <summary>The single leaper every shipped SceneLayout is written with. Kept as the fallback for <see cref="leapingAttackerSpawns"/>.</summary>
        [Export] public Vector3 leapingAttackerSpawnPosition = new Vector3(16f, 0.5f, 0f);

        /// <summary>The single caster every shipped SceneLayout is written with. Kept as the fallback for <see cref="rangedCasterSpawns"/>.</summary>
        [Export] public Vector3 rangedCasterSpawnPosition = new Vector3(24f, 0.5f, 0f);

        /// <summary>Every melee grunt in the chapter, with an optional tuning file each. Empty falls back to <see cref="meleeGruntSpawnPositions"/>.</summary>
        [Export] public GameplayEnemySpawnRow[] meleeGruntSpawns;

        /// <summary>Every leaping attacker in the chapter. Empty falls back to the single <see cref="leapingAttackerSpawnPosition"/>.</summary>
        [Export] public GameplayEnemySpawnRow[] leapingAttackerSpawns;

        /// <summary>Every ranged caster in the chapter. Empty falls back to the single <see cref="rangedCasterSpawnPosition"/>.</summary>
        [Export] public GameplayEnemySpawnRow[] rangedCasterSpawns;

        /// <summary>
        /// Off means the arena holds nothing but its boss. The grunt list can already be emptied, but
        /// the leaping attacker and the ranged caster fall back to single positions that otherwise
        /// always spawn - so this is still the only way to author a room that is just the fight.
        /// </summary>
        [Export] public bool spawnApproachEnemies = true;
        [Export] public Vector3 wrathMiniBossSpawnPosition = new Vector3(29.5f, 1f, 0f);

        /// <summary>
        /// Name of a <c>RainbowChapterBossData</c> file under <c>Resources/Design</c>, without the
        /// extension. Empty keeps the shipped WrathMiniBoss. This is how a second arena gets a second
        /// boss without a line of code.
        /// </summary>
        [Export] public string bossDataFile = "";

        /// <summary>
        /// Name of a <c>BossEncounterData</c> file under <c>Resources/Design</c>, without the extension.
        /// Empty keeps chapter one's encounter. This is how a second arena gets its own reach and punish
        /// window rather than borrowing the first one's.
        /// </summary>
        [Export] public string bossEncounterFile = "";

        // Shortcut

        /// <summary>
        /// A one-way door back to the checkpoint, opened once from the far side and open for the rest of
        /// the chapter. No override flag: there is no shipped shortcut for a silent file to preserve, so
        /// this flag is itself the opt-in.
        /// </summary>
        [Export] public bool hasShortcutGate;

        [Export] public Vector3 shortcutGatePosition = new Vector3(12f, 1.35f, 0f);
        [Export] public Vector2 shortcutGateSize = new Vector2(0.32f, 4.6f);

        /// <summary>
        /// Which side of the door is the far one - the side the level makes the player walk the long way
        /// round to reach, and the only side Interact opens it from. True is the right (+x), which is
        /// every arena laid out bonfire-then-door-then-boss. A file that does not name this field gets
        /// true, so the door is never a hole the player walks straight through on the way out.
        /// </summary>
        [Export] public bool shortcutOpensFromRight = true;

        // Platforms And Scenery
        [Export] public bool overridePlatforms;
        [Export] public GameplayPlatformRow[] platforms;

        [Export] public bool overrideScenery;
        [Export] public GameplaySceneryRow[] backdropScenery;

        /// <summary>
        /// The authored layout, or null when the file is missing. For the shared file that is an error;
        /// for a scene's own <c>SceneLayout_&lt;Name&gt;</c> it is normal - the catalog passes
        /// <paramref name="required"/> false there and falls back to the shared file.
        /// No unit conversion here: the object holds the file, and <see cref="ApplyTo"/> converts.
        /// </summary>
        public static GameplaySceneLayoutData Load(string designPath, bool required = true)
        {
            GameplaySceneLayoutData data = Res.LoadJson<GameplaySceneLayoutData>(designPath, required);
            if (data != null)
            {
                data.ResourceName = designPath.GetFile();
            }

            return data;
        }

        public void ApplyTo(GameplaySceneDefaults defaults)
        {
            if (defaults == null)
                return;

            WarnAboutListsTheFlagsThrowAway();

            if (overrideCamera)
            {
                defaults.CameraPosition = GameplaySceneDefaults.ToGodot(cameraPosition);
                // Unconverted on purpose - a half-height in Unity units, which the bootstrap turns into
                // a Camera2D zoom. See the remarks on GameplaySceneDefaults.
                defaults.CameraOrthographicSize = cameraOrthographicSize > 0f ? cameraOrthographicSize : defaults.CameraOrthographicSize;
                defaults.CameraHorizontalBounds = GameplaySceneDefaults.ToGodotHorizontalBounds(cameraHorizontalBounds);
                defaults.CameraVerticalBounds = GameplaySceneDefaults.ToGodotVerticalBounds(cameraVerticalBounds);
                defaults.FallDeathY = GameplaySceneDefaults.ToGodotY(fallDeathY);
            }

            if (overrideSpawn)
            {
                defaults.PlayerSpawnPosition = GameplaySceneDefaults.ToGodot(playerSpawnPosition);

                // Same rule the grunt list has always used, for the same reason: an empty array is a file
                // that has not been told about checkpoints yet, not a chapter with nowhere to rest. Eight
                // shipped layouts name only the single field, and the JSON reader would leave the array
                // empty without saying so - which is how a rename here would silently move eight arenas.
                defaults.CheckpointPositions = checkpointPositions != null && checkpointPositions.Length > 0
                    ? ToPositions(checkpointPositions)
                    : new[] { GameplaySceneDefaults.ToGodot(checkpointPosition) };
            }

            if (overrideTerrain)
            {
                defaults.BackdropPosition = GameplaySceneDefaults.ToGodot(backdropPosition);
                defaults.BackdropSize = GameplaySceneDefaults.ToGodotSize(backdropSize);
                defaults.GroundPosition = GameplaySceneDefaults.ToGodot(groundPosition);
                defaults.GroundSize = GameplaySceneDefaults.ToGodotSize(groundSize);
                defaults.GroundRimPosition = GameplaySceneDefaults.ToGodot(groundRimPosition);
                defaults.GroundRimSize = GameplaySceneDefaults.ToGodotSize(groundRimSize);
                defaults.LeftArenaGatePosition = GameplaySceneDefaults.ToGodot(leftArenaGatePosition);
                defaults.RightArenaGatePosition = GameplaySceneDefaults.ToGodot(rightArenaGatePosition);
                defaults.ArenaGateSize = GameplaySceneDefaults.ToGodotSize(arenaGateSize);
            }

            if (overrideMarkers)
            {
                defaults.CheckpointLabelPosition = GameplaySceneDefaults.ToGodot(checkpointLabelPosition);
                defaults.DuelFloorLabelPosition = GameplaySceneDefaults.ToGodot(duelFloorLabelPosition);
                defaults.WrathAltarLabelPosition = GameplaySceneDefaults.ToGodot(wrathAltarLabelPosition);
                defaults.SpiritPlatformPosition = GameplaySceneDefaults.ToGodot(spiritPlatformPosition);
                defaults.SpiritPlatformSize = GameplaySceneDefaults.ToGodotSize(spiritPlatformSize);
                defaults.SpiritPlatformStartsActive = spiritPlatformStartsActive;
            }

            if (overrideEnemyPlacement)
            {
                // An empty array here would spawn an arena with no enemies in it, which reads as a
                // broken build rather than as an authoring choice; the shipped placement is kept until
                // the file actually names one. Rows win over the position-only list, and the list wins
                // over the shipped default - so a layout written before rows existed still places what
                // it always placed.
                GameplaySceneDefaults.EnemySpawn[] grunts = ToSpawns(meleeGruntSpawns) ?? ToSpawns(meleeGruntSpawnPositions);
                if (grunts != null)
                    defaults.MeleeGruntSpawns = grunts;

                // The single leaper and caster were written unconditionally before rows existed, and
                // still are: a layout that names neither is the one-of-each arena it has always been.
                defaults.LeapingAttackerSpawns = ToSpawns(leapingAttackerSpawns) ?? Single(leapingAttackerSpawnPosition);
                defaults.RangedCasterSpawns = ToSpawns(rangedCasterSpawns) ?? Single(rangedCasterSpawnPosition);
                defaults.SpawnApproachEnemies = spawnApproachEnemies;
                defaults.WrathMiniBossSpawnPosition = GameplaySceneDefaults.ToGodot(wrathMiniBossSpawnPosition);
                defaults.BossDataFile = bossDataFile ?? string.Empty;
                defaults.BossEncounterFile = bossEncounterFile ?? string.Empty;
            }

            // Written only when the file asks for one. There is no shipped shortcut to preserve, so a
            // layout that says nothing has to keep saying nothing rather than saying no.
            if (hasShortcutGate)
            {
                defaults.HasShortcutGate = true;
                defaults.ShortcutGatePosition = GameplaySceneDefaults.ToGodot(shortcutGatePosition);
                defaults.ShortcutGateSize = GameplaySceneDefaults.ToGodotSize(shortcutGateSize);
                defaults.ShortcutOpensFromRight = shortcutOpensFromRight;
            }

            if (overridePlatforms && platforms != null)
                defaults.Platforms = ToPlatformDefinitions(platforms);

            if (overrideScenery && backdropScenery != null)
                defaults.BackdropScenery = ToSceneryDefinitions(backdropScenery);
        }

        /// <summary>
        /// A list authored under a section flag that is off is dropped on the floor, and the arena that
        /// comes back looks like a working one-checkpoint corridor rather than like a mistake. Same
        /// judgment as <see cref="ParseSpriteKind"/>: quietly building the wrong thing is the worse
        /// failure, because nothing about it is broken enough to investigate.
        /// </summary>
        /// <remarks>
        /// Every shipped layout sets both flags, so this is silent today. The chapter that trips it is the
        /// next one somebody hand-authors - which is exactly the one where the arrays are new and the
        /// flags are easy to forget.
        /// </remarks>
        private void WarnAboutListsTheFlagsThrowAway()
        {
            if (!overrideSpawn && Authored(checkpointPositions))
                WarnIgnored("checkpointPositions", "overrideSpawn");

            if (overrideEnemyPlacement)
                return;

            // The position-only grunt list is checked with the rest: it has carried this trap since long
            // before the rows existed, and a fix that only covered the new fields would leave the oldest
            // one silent.
            if (Authored(meleeGruntSpawnPositions))
                WarnIgnored("meleeGruntSpawnPositions", "overrideEnemyPlacement");

            if (Authored(meleeGruntSpawns))
                WarnIgnored("meleeGruntSpawns", "overrideEnemyPlacement");

            if (Authored(leapingAttackerSpawns))
                WarnIgnored("leapingAttackerSpawns", "overrideEnemyPlacement");

            if (Authored(rangedCasterSpawns))
                WarnIgnored("rangedCasterSpawns", "overrideEnemyPlacement");
        }

        private static bool Authored(System.Array list) => list != null && list.Length > 0;

        private void WarnIgnored(string listName, string flagName)
        {
            GD.PushWarning(
                $"GameplaySceneLayoutData '{ResourceName}': '{listName}' is authored but '{flagName}' is off, so it was " +
                $"ignored and the shipped placement was kept. Add \"{flagName}\": true to this layout file.");
        }

        private static Vector2[] ToPositions(Vector3[] positions)
        {
            var result = new Vector2[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                result[i] = GameplaySceneDefaults.ToGodot(positions[i]);

            return result;
        }

        /// <summary>Null for an unauthored list, so a caller can fall through to the next source.</summary>
        private static GameplaySceneDefaults.EnemySpawn[] ToSpawns(GameplayEnemySpawnRow[] rows)
        {
            if (rows == null || rows.Length == 0)
                return null;

            var result = new GameplaySceneDefaults.EnemySpawn[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                GameplayEnemySpawnRow row = rows[i];
                result[i] = row == null
                    ? default
                    : new GameplaySceneDefaults.EnemySpawn(GameplaySceneDefaults.ToGodot(row.position), row.dataFile);
            }

            return result;
        }

        private static GameplaySceneDefaults.EnemySpawn[] ToSpawns(Vector3[] positions)
        {
            if (positions == null || positions.Length == 0)
                return null;

            var result = new GameplaySceneDefaults.EnemySpawn[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                result[i] = new GameplaySceneDefaults.EnemySpawn(GameplaySceneDefaults.ToGodot(positions[i]), null);

            return result;
        }

        private static GameplaySceneDefaults.EnemySpawn[] Single(Vector3 position)
        {
            return new[] { new GameplaySceneDefaults.EnemySpawn(GameplaySceneDefaults.ToGodot(position), null) };
        }

        private static GameplaySceneDefaults.PlatformDefinition[] ToPlatformDefinitions(GameplayPlatformRow[] rows)
        {
            var result = new GameplaySceneDefaults.PlatformDefinition[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                GameplayPlatformRow row = rows[i];
                result[i] = row == null
                    ? default
                    : new GameplaySceneDefaults.PlatformDefinition(
                        row.name,
                        GameplaySceneDefaults.ToGodot(row.position),
                        GameplaySceneDefaults.ToGodotSize(row.size));
            }

            return result;
        }

        private static GameplaySceneDefaults.SceneryDefinition[] ToSceneryDefinitions(GameplaySceneryRow[] rows)
        {
            var result = new GameplaySceneDefaults.SceneryDefinition[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                GameplaySceneryRow row = rows[i];
                if (row == null)
                {
                    result[i] = default;
                    continue;
                }

                result[i] = new GameplaySceneDefaults.SceneryDefinition(
                    row.name,
                    ParseSpriteKind(row.name, row.spriteKind),
                    GameplaySceneDefaults.ToGodot(row.position),
                    GameplaySceneDefaults.ToGodotSize(row.size));
            }

            return result;
        }

        /// <summary>
        /// A misspelled kind falls back to the disc and says so. Silently drawing the wrong shape is the
        /// worse failure: the arena still builds, so nothing looks broken enough to investigate.
        /// </summary>
        private static GameplayVisualFactory.SpriteKind ParseSpriteKind(string sceneryName, string raw)
        {
            if (System.Enum.TryParse(raw, true, out GameplayVisualFactory.SpriteKind kind))
                return kind;

            GD.PushWarning($"GameplaySceneLayoutData: scenery '{sceneryName}' names sprite kind '{raw}', which does not exist. Falling back to Disc.");
            return GameplayVisualFactory.SpriteKind.Disc;
        }
    }
}
