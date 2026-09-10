using System.Collections.Generic;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// The design files that describe the arenas and the fight around the boss. Every one of them is live
    /// - the override flags are on, so the file really is what builds the level.
    ///
    /// That is the whole risk: a typo in any of them moves the world silently, because a wrong number is
    /// still a valid number. What catches it is no longer a comparison against the code fallback - eight
    /// authored chapters are all supposed to differ from that - but the coherence check below, which asks
    /// every chapter whether the arena it describes is one its own actors can stand in.
    ///
    /// UNITS - this fixture straddles the conversion boundary and says so at every assertion:
    ///  - <see cref="BossEncounterData"/> comes out of <c>Load</c>, which has already run
    ///    <c>ScaleToPixels</c>, so its three spatial fields are Godot pixels (Unity metres x 100) and
    ///    its timings are unchanged seconds.
    ///  - <see cref="GameplaySceneDefaults"/> is the arena after <c>ApplyTo</c>, so every position is
    ///    Godot pixels with <b>+Y down</b>. The floor's surface is therefore its <i>smallest</i> y and
    ///    "standing on the floor" is y less than that, not greater. <c>CameraOrthographicSize</c> is the
    ///    one field that stays in Unity units, so the one-screen budget is converted at the comparison.
    /// </summary>
    public sealed class GameplayEncounterDataTests
    {
        [Test]
        public void WrathEncounterJson_LoadsAndMatchesTheConstantsItReplaced()
        {
            BossEncounterData encounter = GameplayTuningCatalog.Load()?.WrathEncounter;
            Assert.NotNull(encounter, "WrathEncounter.json should load through the tuning catalog.");

            // The three arena/engagement numbers are metres in the file and pixels here: the catalog
            // loads them through BossEncounterData.Load, which runs ScaleToPixels. 5 -> 500, 1.1 -> 110,
            // 8 -> 800. A raw Res.LoadJson would still read 5 and this test would be asserting the
            // wrong thing.
            Assert.AreEqual(World.U(5f), encounter.arenaLeftOffset, 0.1f, "The boss's room to retreat is what makes the fight readable; it was 5 units.");
            Assert.AreEqual(World.U(1.1f), encounter.arenaRightOffset, 0.1f, "The wall behind the boss was 1.1 units back.");
            Assert.AreEqual(World.U(8f), encounter.detectionRange, 0.1f, "The fight starts when the player enters the arena, at 8 units.");

            // Seconds, unconverted.
            Assert.AreEqual(2.7f, encounter.introHoldTimeout, 0.001f, "The intro cap follows CutsceneDirection's 4.0s budget minus the sequence's own beats.");
            Assert.AreEqual(0.5f, encounter.postAttackRecoveryTime, 0.001f, "The punish window is the single most feel-critical number in the file.");
            Assert.AreEqual(1.5f, encounter.victoryPresentationDelay, 0.001f, "The victory panel waits on this beat.");
        }

        /// <summary>
        /// The weights are relative and normalised, so what a designer controls is which band a roll
        /// lands in. Rolls sit clear of the two boundaries on purpose: at exactly 0.4 or 0.7 the answer
        /// turns on the last bit of a float sum, and pinning it would be pinning arithmetic noise rather
        /// than a decision anybody made.
        ///
        /// UNITS: a weight is a ratio. Nothing here converts.
        /// </summary>
        [Test]
        public void PhaseTwoWeights_SplitTheRollWhereTheyClaimTo()
        {
            // Unity needed ScriptableObject.CreateInstance; a Godot Resource is a plain new, and being
            // ref-counted it needs no DestroyImmediate at the end either.
            var encounter = new BossEncounterData
            {
                phaseTwoSlashWeight = 0.4f,
                phaseTwoSlamWeight = 0.3f,
                phaseTwoRushWeight = 0.3f,
            };

            Assert.AreEqual(0, encounter.SelectPhaseTwoPattern(0f), "The bottom of the roll is the slash.");
            Assert.AreEqual(0, encounter.SelectPhaseTwoPattern(0.39f), "Inside the slash band.");
            Assert.AreEqual(1, encounter.SelectPhaseTwoPattern(0.45f), "Inside the slam band.");
            Assert.AreEqual(1, encounter.SelectPhaseTwoPattern(0.69f), "Still inside the slam band.");
            Assert.AreEqual(2, encounter.SelectPhaseTwoPattern(0.75f), "Inside the rush band.");
            Assert.AreEqual(2, encounter.SelectPhaseTwoPattern(1f), "The top of the roll is the rush.");

            // Scale invariance is the point of normalising: 40/30/30 has to mean 0.4/0.3/0.3.
            encounter.phaseTwoSlashWeight = 40f;
            encounter.phaseTwoSlamWeight = 30f;
            encounter.phaseTwoRushWeight = 30f;
            Assert.AreEqual(1, encounter.SelectPhaseTwoPattern(0.5f), "Weights are relative, so scaling them all must change nothing.");

            encounter.phaseTwoSlashWeight = 0f;
            encounter.phaseTwoSlamWeight = 0f;
            encounter.phaseTwoRushWeight = 0f;
            Assert.AreEqual(0, encounter.SelectPhaseTwoPattern(0.9f),
                "An all-zero table must still pick something; a boss that stops attacking is the worse failure.");
        }

        /// <summary>
        /// Every chapter's layout, checked against itself rather than against a list of coordinates.
        ///
        /// This used to pin <c>SceneLayout.json</c> field by field to <c>GameplaySceneDefaults.Create()</c>,
        /// which worked while the shared file was a copy of the code defaults and stopped working the day
        /// chapter one became a 300-unit level: an authored arena is *supposed* to differ from the 36-unit
        /// fallback, so that test would have gone red on every legitimate edit from here on. The risk it
        /// covered is unchanged though - a mistyped coordinate is still a valid number, and it still moves
        /// the world in silence - so the same risk is covered by asking whether the arena the file
        /// describes is one its own actors can stand in.
        ///
        /// Derived end to end: the chapter list comes from <see cref="ChapterRoute.Scenes"/> and every
        /// bound from the file being checked, so a designer moving a bonfire re-derives the check instead
        /// of reddening it - and a chapter scene added to <c>res://Scenes/</c> is checked the moment it
        /// exists. (Unity read that list out of Build Settings; the folder is what replaced it.)
        /// </summary>
        [Test]
        public void EveryChapterLayout_DescribesAnArenaItsOwnActorsCanStandIn()
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            Assert.NotNull(catalog, "The tuning catalog should load.");

            string[] chapters = ChapterRoute.Scenes();
            Assert.Greater(chapters.Length, 0,
                "res://Scenes/ carries no chapters, so this test is checking nothing at all.");

            foreach (string chapter in chapters)
            {
                GameplaySceneLayoutData file = catalog.SceneLayoutFor(chapter);
                Assert.NotNull(file, $"{chapter} resolved to no layout at all, not even the shared one.");

                // A chapter that ships a file of its own has to actually be handed it. This is the failure
                // an array-isation or a rename causes: SceneLayoutFor falls through to the shared arena and
                // the chapter quietly builds someone else's level.
                //
                // Unity asked Resources.Load<TextAsset>; the design files are read as text here, so the
                // question "does this file exist" is Res.LoadText returning something.
                bool hasOwnFile = Res.LoadText(
                    GameplayTuningCatalog.DesignResourceFolder + GameplayTuningCatalog.SceneLayoutFile + "_" + chapter,
                    ".json") != null;
                if (hasOwnFile)
                {
                    Assert.AreNotSame(catalog.SceneLayout, file,
                        $"SceneLayout_{chapter}.json exists but the catalog handed back the shared arena, so this " +
                        "chapter is building the wrong level. Check the file name against the scene name.");
                }

                AssertTheFlagCoversTheList(chapter, file.overrideSpawn, "overrideSpawn", file.checkpointPositions, "checkpointPositions");
                AssertTheFlagCoversTheList(chapter, file.overrideEnemyPlacement, "overrideEnemyPlacement", file.meleeGruntSpawns, "meleeGruntSpawns");
                AssertTheFlagCoversTheList(chapter, file.overrideEnemyPlacement, "overrideEnemyPlacement", file.leapingAttackerSpawns, "leapingAttackerSpawns");
                AssertTheFlagCoversTheList(chapter, file.overrideEnemyPlacement, "overrideEnemyPlacement", file.rangedCasterSpawns, "rangedCasterSpawns");
                AssertTheFlagCoversTheList(chapter, file.overridePlatforms, "overridePlatforms", file.platforms, "platforms");
                AssertTheFlagCoversTheList(chapter, file.overrideScenery, "overrideScenery", file.backdropScenery, "backdropScenery");

                AssertSceneryKindsAllParse(chapter, file);
                AssertTheArenaHoldsItsOwnActors(chapter, GameplaySceneDefaults.CreateForScene(chapter, null));
            }
        }

        /// <summary>
        /// The documented trap in <see cref="GameplaySceneLayoutData.ApplyTo"/>: a list authored under a
        /// section flag that is off is dropped on the floor, and what comes back is a working
        /// one-checkpoint corridor rather than anything that looks broken. The runtime warns; here it is
        /// red, because the warning scrolls past in a headless log nobody reads.
        /// </summary>
        private static void AssertTheFlagCoversTheList(
            string chapter, bool flag, string flagName, System.Array list, string listName)
        {
            if (list == null || list.Length == 0)
                return;

            Assert.IsTrue(flag,
                $"{chapter} authors '{listName}' but '{flagName}' is off, so the whole list is ignored and the " +
                $"chapter silently builds the shipped 36-unit arena's placement instead. Add \"{flagName}\": true.");
        }

        /// <summary>
        /// A misspelled sprite kind falls back to a disc with a warning, which means an arch typed
        /// <c>"Arc"</c> renders as a circle and nothing fails. Spelled out in the JSON on purpose, so this
        /// is the check that keeps that readable form from being a silent one.
        /// </summary>
        private static void AssertSceneryKindsAllParse(string chapter, GameplaySceneLayoutData file)
        {
            if (!file.overrideScenery || file.backdropScenery == null)
                return;

            foreach (GameplaySceneryRow row in file.backdropScenery)
            {
                if (row == null)
                    continue;

                Assert.IsTrue(System.Enum.TryParse(row.spriteKind, true, out GameplayVisualFactory.SpriteKind _),
                    $"{chapter}: scenery '{row.name}' names sprite kind '{row.spriteKind}', which does not exist. " +
                    "It will silently render as a disc.");
            }
        }

        /// <summary>
        /// The coherence rules an arena has to satisfy whatever its numbers are: everything the chapter
        /// places stands on the floor it authored, the bonfires are in the order they are met, the boss is
        /// ahead of the last one, the fall line is under the floor and the camera can follow the level to
        /// its end. Each of these is a wrong number that is still a valid number.
        ///
        /// Every comparison below is in Godot pixels with +Y down, which is what flips three of them:
        /// the floor's surface is <c>GroundPosition.Y - GroundSize.Y * 0.5</c> (Unity added), standing on
        /// it means a <i>smaller</i> y (Unity, larger), and the fall line is <i>below</i> the floor as a
        /// <i>greater</i> y (Unity, less).
        /// </summary>
        private static void AssertTheArenaHoldsItsOwnActors(string chapter, GameplaySceneDefaults arena)
        {
            float groundLeft = arena.GroundPosition.X - arena.GroundSize.X * 0.5f;
            float groundRight = arena.GroundPosition.X + arena.GroundSize.X * 0.5f;

            // +Y down: the surface a body stands on is the top edge, which is the minimum y.
            float groundTop = arena.GroundPosition.Y - arena.GroundSize.Y * 0.5f;

            // Unity's "wider than 1 unit" is 100 px here.
            Assert.Greater(arena.GroundSize.X, World.U(1f), $"{chapter} has no floor to speak of - groundSize.x is {arena.GroundSize.X}.");

            OnTheFloor(chapter, "the player spawn", arena.PlayerSpawnPosition, groundLeft, groundRight, groundTop);
            OnTheFloor(chapter, "the boss spawn", arena.WrathMiniBossSpawnPosition, groundLeft, groundRight, groundTop);

            // Unity's Vector3[] - z was only ever draw order, and the arena drops it.
            Vector2[] bonfires = arena.CheckpointPositions;
            Assert.IsTrue(bonfires != null && bonfires.Length > 0, $"{chapter} authors no bonfire, so a death has nowhere to send the player.");

            for (int i = 0; i < bonfires.Length; i++)
            {
                OnTheFloor(chapter, $"bonfire {i}", bonfires[i], groundLeft, groundRight, groundTop);

                if (i > 0)
                {
                    // x does not flip, so "in the order they are met" is still increasing x.
                    Assert.Greater(bonfires[i].X, bonfires[i - 1].X,
                        $"{chapter}'s bonfires are not in the order they are met: {i} is at {bonfires[i].X:F1}, behind " +
                        $"{i - 1} at {bonfires[i - 1].X:F1}. The save records the index, so an out-of-order list " +
                        "resumes the player somewhere they have not walked to.");
                }
            }

            Assert.Greater(arena.WrathMiniBossSpawnPosition.X, bonfires[bonfires.Length - 1].X,
                $"{chapter}'s boss stands behind its last bonfire, so the run-back runs backwards.");

            foreach (GameplaySceneDefaults.EnemySpawn spawn in AllPlacements(arena))
                OnTheFloor(chapter, "an enemy placement", spawn.Position, groundLeft, groundRight, groundTop);

            if (arena.HasShortcutGate)
                OnTheFloor(chapter, "the shortcut door", arena.ShortcutGatePosition, groundLeft, groundRight, groundTop);

            // Unity asserted FallDeathY < groundTop - 1. Down is +y here, so "under the floor" is a
            // greater y, and the one-unit clearance is 100 px.
            Assert.Greater(arena.FallDeathY, groundTop + World.U(1f),
                $"{chapter}'s fall line is at {arena.FallDeathY:F1} with the floor's surface at {groundTop:F1}. " +
                "Standing on the ground is a death.");

            float backdropLeft = arena.BackdropPosition.X - arena.BackdropSize.X * 0.5f;
            float backdropRight = arena.BackdropPosition.X + arena.BackdropSize.X * 0.5f;
            Assert.LessOrEqual(backdropLeft, groundLeft, $"{chapter}'s backdrop stops short of the left end of its floor.");
            Assert.GreaterOrEqual(backdropRight, groundRight, $"{chapter}'s backdrop stops short of the right end of its floor.");

            // The same rule GameplayEnvironmentBuilder only warns about. Nothing derives the camera bounds
            // from the ground - they are two hand-authored numbers - so a level that grows past them leaves
            // the player walking off the side of a screen that stopped following them.
            //
            // CameraOrthographicSize is the arena's one deliberately unconverted field (a half-height in
            // Unity units, because Godot's Camera2D has Zoom instead), so the screen budget is scaled here
            // to meet the pixel bounds it is compared against.
            float oneScreen = World.U(arena.CameraOrthographicSize * 2f);
            Assert.LessOrEqual(groundRight - arena.CameraHorizontalBounds.Y, oneScreen,
                $"{chapter}'s floor runs to {groundRight:F1} but the camera stops at {arena.CameraHorizontalBounds.Y:F1}, " +
                $"more than one screen ({oneScreen:F1}) short. The end of the level is walked off-camera.");
            Assert.LessOrEqual(arena.CameraHorizontalBounds.X - groundLeft, oneScreen,
                $"{chapter}'s floor starts at {groundLeft:F1} but the camera stops at {arena.CameraHorizontalBounds.X:F1}, " +
                $"more than one screen ({oneScreen:F1}) in. The start of the level is walked off-camera.");
        }

        private static void OnTheFloor(
            string chapter, string what, Vector2 position, float groundLeft, float groundRight, float groundTop)
        {
            // Half a unit inside each end: the player's body is 0.58 wide, so a placement on the very edge
            // is half off it. Half a unit is 50 px.
            Assert.IsTrue(position.X > groundLeft + World.U(0.5f) && position.X < groundRight - World.U(0.5f),
                $"{chapter}: {what} is at x {position.X:F1}, off a floor that runs {groundLeft:F1} to {groundRight:F1}. " +
                "Whatever stands there falls to its death on the first frame.");

            // Unity asked for y greater than the surface (up was +y). Down is +y here, so standing on or
            // above the floor is a y no greater than the surface, within the same 0.01-unit slack (1 px).
            Assert.Less(position.Y, groundTop + World.U(0.01f),
                $"{chapter}: {what} is at y {position.Y:F2}, below the floor's surface at {groundTop:F2}.");
        }

        private static IEnumerable<GameplaySceneDefaults.EnemySpawn> AllPlacements(GameplaySceneDefaults arena)
        {
            foreach (GameplaySceneDefaults.EnemySpawn[] list in new[]
                     { arena.MeleeGruntSpawns, arena.LeapingAttackerSpawns, arena.RangedCasterSpawns })
            {
                if (list == null)
                    continue;

                foreach (GameplaySceneDefaults.EnemySpawn spawn in list)
                    yield return spawn;
            }
        }

        /// <summary>
        /// The mechanism that makes a second arena an authoring job: a scene gets its own layout by
        /// name, and falls back to the shared one when it has none. Only the fallback is exercised
        /// here - shipping a second layout file to test the other branch would mean inventing an arena
        /// nobody designed.
        /// </summary>
        [Test]
        public void SceneLayout_FallsBackToTheSharedArena_ForASceneWithNoFileOfItsOwn()
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            Assert.NotNull(catalog, "The tuning catalog should load.");

            Assert.AreSame(catalog.SceneLayout, catalog.SceneLayoutFor("__NoSuchScene"),
                "A scene with no layout of its own has to get the shared arena, not nothing.");
            Assert.AreSame(catalog.SceneLayout, catalog.SceneLayoutFor(null),
                "An unnamed scene - which is what a tool with nothing open sees - falls back too.");

            GameplaySceneDefaults viaScene = GameplaySceneDefaults.CreateForScene("__NoSuchScene", null);
            GameplaySceneDefaults viaShared = GameplaySceneDefaults.Create();
            catalog.SceneLayout?.ApplyTo(viaShared);

            Assert.AreEqual(viaShared.GroundPosition, viaScene.GroundPosition,
                "The fallback has to produce the shared arena, not a half-applied one.");
        }

        /// <summary>
        /// A section nobody opted into must not be written. Without this, adding a field to the data
        /// class would quietly start overriding an arena the file never meant to describe.
        ///
        /// UNITS: 999 is the authored Unity number, which is what the layout class holds - the
        /// conversion happens inside ApplyTo, and the point of the test is that it never runs.
        /// </summary>
        [Test]
        public void SceneLayout_WithNoOverrideFlags_ChangesNothing()
        {
            var layout = new GameplaySceneLayoutData
            {
                groundPosition = new Vector3(999f, 999f, 999f),
                playerSpawnPosition = new Vector3(999f, 999f, 999f),
            };

            GameplaySceneDefaults defaults = GameplaySceneDefaults.Create();
            Vector2 groundBefore = defaults.GroundPosition;
            Vector2 spawnBefore = defaults.PlayerSpawnPosition;

            layout.ApplyTo(defaults);

            Assert.AreEqual(groundBefore, defaults.GroundPosition, "Terrain must stay put while overrideTerrain is off.");
            Assert.AreEqual(spawnBefore, defaults.PlayerSpawnPosition, "Spawn must stay put while overrideSpawn is off.");
        }
    }
}
