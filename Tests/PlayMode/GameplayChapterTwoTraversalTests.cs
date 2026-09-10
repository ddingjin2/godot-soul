using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// Chapter two as a level rather than as a file that deserialises. Every other chapter-two suite
    /// proves the arena is <i>built</i>; none of them proves it can be <i>walked</i>, and since the
    /// shortcut door became one-way that difference is the difference between a chapter and a dead end:
    /// the ColonnadeStep / EmberSpan climb is now the only route past the closed door on a first pass,
    /// so a single unjumpable gap in it makes the chapter unfinishable with every existing suite green.
    ///
    /// The jump envelope this leans on is the shipped one - <c>PlayerMovement.json</c> jumpForce 12 over
    /// gravity 20, moveSpeed 6 - but no number from it is written down here. The takeoff points are
    /// derived from the platforms in <c>SceneLayout_Chapter02_Orange.json</c> at run time, so moving a
    /// platform re-derives the route instead of silently testing the old one, and retuning the jump is
    /// caught rather than hard-coded.
    ///
    /// Two of these are measurements as much as assertions - the run-back seconds are the number
    /// [[PlaytimePlan]] Phase 2 asks for and a headless run can actually take. They are logged, and only
    /// bounded by limits so wide that no plausible retune reaches them: putting red on a tuning number
    /// buys a broken suite every time the designer moves it.
    ///
    /// Written during the QA review of the chapter-two authoring pass and NOT verified - nothing here
    /// has been run.
    /// </summary>
    /// <remarks>
    /// PORT - UNITS AND THE Y AXIS. Everything <see cref="GameplaySceneDefaults"/> hands back is already
    /// Godot pixels with +Y down, so no layout coordinate is converted here. Every number this fixture
    /// authors itself is still the Unity metre it was written as and is wrapped in <see cref="World.U"/>
    /// where it is used. The three places the axis flip actually bites are marked at the line:
    /// <see cref="Top"/> (a platform's surface is its <i>smaller</i> y now), the door's top edge, and the
    /// "came down on the arena floor" test, where Unity's <c>y &lt; 1</c> becomes <c>y &gt; -100</c>.
    /// </remarks>
    public sealed class GameplayChapterTwoTraversalTests
    {
        private const string ChapterTwoSceneName = "Chapter02_Orange";

        /// <summary>Loaded back at the end of every test, so the next fixture gets the arena it expects.</summary>
        private const string FallbackSceneName = "GameplayScene";

        /// <summary>
        /// The climb, in the order the level meets it. Named rather than inferred: "which platforms are
        /// the mandatory route" is a design fact, and a run that quietly re-routed itself around a
        /// deleted platform would report a passable level that no longer exists.
        /// </summary>
        private static readonly string[] MandatoryClimb =
        {
            "ColonnadeStepA",
            "ColonnadeStepB",
            "EmberSpanA",
            "EmberSpanB",
            "EmberSpanC",
            "EmberSpanD"
        };

        /// <summary>
        /// How late a hop leaves the platform it is standing on. The reach of a jump is far longer than
        /// any gap in this chain, so the failure mode is always jumping too early and landing short -
        /// leaving at the very edge is both the safest rule and the one a player uses. Unity metres.
        /// </summary>
        private const float LedgeMarginUnits = 0.45f;

        /// <summary>
        /// How far before a platform's near edge a ground launch commits. Unlike a ledge hop this one has
        /// to clear the platform's <i>side</i> on the way up, so it is taken early enough that the rising
        /// arc is past the top before it is past the edge. Unity metres.
        /// </summary>
        private const float GroundLaunchLeadUnits = 3.5f;

        /// <summary>Run-up before the first launch, so the first takeoff happens at full speed rather than mid-acceleration. Unity metres.</summary>
        private const float RunUpUnits = 9f;

        /// <summary>
        /// Physics steps are unaffected by the timescale - only their spacing in wall-clock time is - so
        /// this buys back most of the minute a 300-unit level would otherwise cost the suite without
        /// changing a single simulation result. Never used on a run with live enemies: hit stop writes
        /// the timescale itself and would fight it.
        /// </summary>
        private const float FastForward = 4f;

        /// <summary>How little forward travel counts as being held, and for how long, before the driver hops.</summary>
        private const float StallProgressUnits = 0.5f;
        private const float StallSeconds = 0.5f;

        /// <summary>
        /// SIGN FLIP. Unity called a body "down on the arena floor" at <c>y &lt; 1</c> metre with +Y up.
        /// Godot's +Y points down, so one metre above the origin is -100 px and the test becomes
        /// <c>y &gt; -100</c>. Getting this backwards would call every airborne frame a landing.
        /// </summary>
        private static readonly float FloorLevelY = -World.U(1f);

        private GameSaveData _existingSave;
        private bool _startLoadFlag;
        private bool _startSkipAll;
        private float _startTimeScale;
        private Difficulty _startDifficulty;
        private int _startNewGamePlus;

        [SetUp]
        public void SetUp()
        {
            _existingSave = GameSave.Read();
            _startLoadFlag = GameSave.LoadOnNextGameplayStart;
            _startSkipAll = CutsceneDirector.SkipAll;
            _startTimeScale = GameClock.TimeScale;
            _startDifficulty = DifficultySettings.Current;
            _startNewGamePlus = DifficultySettings.NewGamePlus;

            // PORT: Unity's Time.fixedDeltaTime was saved and restored here. Godot's physics step is a
            // project setting with no setter on GameClock and nothing in the port writes it.

            // The entry shot takes PlayerInputReceiver for its length and switches it back on when it
            // ends. A driven run that started during the shot would have the controls handed back to a
            // receiver reading a dead keyboard part-way through and stop where it stood - which reads
            // exactly like a level that cannot be walked.
            CutsceneDirector.SkipAll = true;

            // A fresh chapter, unless a test writes a slot and asks for it: the resume path reads real
            // PlayerPrefs, and a slot left by another fixture would drop the player at someone else's
            // bonfire with someone else's door open.
            //
            // PORT: DeleteAll rather than GameSave.Clear. PlayerPrefs is a ConfigFile at
            // user://playerprefs.cfg here and survives between headless runs, so "another fixture" now
            // includes a previous run of the whole suite.
            PlayerPrefs.DeleteAll();
        }

        [TearDown]
        public async Task TearDownAndReleaseTheScene()
        {
            // A held action outlives the scene it was pressed in, so it is released before anything else
            // - a leaked Interact would open the next fixture's door for it.
            Input.ActionRelease(InteractAction);

            GameClock.TimeScale = _startTimeScale;
            CutsceneDirector.SkipAll = _startSkipAll;
            DifficultySettings.Set(_startDifficulty, _startNewGamePlus);

            if (_existingSave != null)
                GameSave.Write(_existingSave);
            else
                GameSave.Clear();

            // Off across the fallback load, whatever it was before: restoring the flag first would make
            // this bootstrap resume the slot that was just put back.
            GameSave.LoadOnNextGameplayStart = false;
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(FallbackSceneName));
            GameSave.LoadOnNextGameplayStart = _startLoadFlag;
        }

        // ------------------------------------------------------------------------------------------
        // A. Can the chapter be finished at all
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The one that decides whether chapter two is a chapter. With the door shut, the ColonnadeStep
        /// and EmberSpan platforms are the only way from the last bonfire to the boss, and every gap in
        /// them is known only from arithmetic on paper. This drives the shipped player through the whole
        /// chain under real physics and asks it to come down on the boss's side of a door that is still
        /// closed - then opens that door from the far side, which is the trip the shortcut is paid for by.
        /// </summary>
        /// <remarks>
        /// Enemies are switched off on purpose. The question here is geometry, and a run that died to a
        /// grunt at 218 would report an unpassable level for a reason that has nothing to do with the
        /// platforms. What an armed run costs is measured separately, below.
        /// </remarks>
        [Test]
        public async Task EmberSpanClimb_CarriesThePlayerOverTheClosedDoor_AndOpensItFromTheFarSide()
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterTwoSceneName, null);
            List<Rung> climb = ReadTheClimb(layout);
            Rung lastRung = climb[climb.Count - 1];

            float gateX = layout.ShortcutGatePosition.X;

            // SIGN FLIP: the top of a box is its centre MINUS half its height once +Y points down.
            // Written as `+ size.y * 0.5f` in Unity; a straight copy would name the door's bottom edge and
            // the guard below would then hold for a door that is entirely in the way.
            float gateTop = layout.ShortcutGatePosition.Y - layout.ShortcutGateSize.Y * 0.5f;
            float gateEast = gateX + layout.ShortcutGateSize.X * 0.5f;

            // The route is over the door, not round the end of it, and the two guards say why. A body
            // that leaves a ledge at speed lands a long way ahead of the point it left - far enough that
            // a door placed short of the span's end is one the player is thrown into the west face of,
            // with no ledge left to jump from. So the door belongs past the last rung, and the player
            // clears it in the air.
            Assert.Less(Right(lastRung), gateX,
                $"Fixture guard: the door at {gateX:F1}px is not past the end of {lastRung.Platform.Name} " +
                $"({Right(lastRung):F1}px). A door under the span is reached on foot and blocks on foot; this route " +
                "only works if the player is still falling when they cross it.");

            // SIGN FLIP: Unity asserted `gateTop < platformTop` - the door's top BELOW the surface. Below
            // is a LARGER y in Godot, so the comparison inverts with the axis. Left as Assert.Less this
            // would pass only for a door that towers over the span, which is the opposite level.
            Assert.Greater(gateTop, Top(lastRung),
                $"Fixture guard: the door's top ({gateTop:F2}px) is not below {lastRung.Platform.Name}'s surface " +
                $"({Top(lastRung):F2}px), so there is nothing to fly over - the drop starts below the obstacle.");

            await LoadChapterTwo();

            PlayerController2D player = RequirePlayer();
            PlayerInputReceiver receiver = TakeTheControls(player);
            ShortcutGate gate = RequireGate();
            int disabled = DisableEveryEnemy();
            Assert.Greater(disabled, 0, "Fixture guard: chapter two should have spawned enemies to switch off.");

            Assert.IsFalse(gate.IsOpen,
                "A chapter entered without a slot has to start with its shortcut shut. Started open, this " +
                "test would walk straight through the door and prove nothing about the climb.");
            Assert.IsTrue(gate.OpensFromRight,
                "Chapter two is laid out bonfire, door, boss, so the far side is the boss's. Opening from the " +
                "near side would let the player unlock the shortcut on the way out and never climb at all.");

            GameClock.TimeScale = FastForward;

            float startX = Left(climb[0]) - World.U(RunUpUnits);
            PlaceOn(player, startX, layout.CheckpointPositions[0].Y);
            await Settle(20);

            // Just past the door, not the boss: coming down east of a shut solid wall having started west
            // of it is the whole claim, and there is no way to do it but over the top. How long the leg
            // takes is measured by FirstPass_..., which drives the same ground from the bonfire.
            float target = gateX + World.U(0.5f);
            var trace = new ClimbTrace();

            await DriveTheClimb(player, climb, target, budgetSeconds: 40f, trace);

            Assert.IsTrue(trace.Arrived,
                "Chapter two cannot be finished. The ColonnadeStep/EmberSpan chain is the only route past the " +
                "closed shortcut, and the player never came down on the far side of it. " + trace.Describe(climb));

            Assert.IsFalse(gate.IsOpen,
                "The door opened by itself during the climb, so this run proves nothing: the route past it was " +
                "the hole, not the platforms.");

            await Settle(20);

            Assert.Greater(player.GlobalPosition.X, gateEast,
                "The player has to end up east of a door that is still shut - which they can only have done in " +
                "the air, over the top of it. " + trace.Describe(climb));

            // Interact is judged where the player is standing when they press it, so the press happens
            // where the drop put them. The nudge is only for a drop that overshot the trigger's reach,
            // and it never crosses back west: the east side is the side allowed to open the door.
            if (!gate.PlayerInside)
                await DriveUntil(player, -1f, () => gate.PlayerInside, budgetSeconds: 10f);

            Assert.IsTrue(gate.PlayerInside,
                "The landing is out of the door's reach, so nothing that route can do will ever open it. " +
                "player=" + Fmt(player.GlobalPosition) + $" gateX={gateX:F1}px");
            Assert.Greater(player.GlobalPosition.X, gateEast,
                "Fixture guard: the press has to happen from the east face, which is the far side.");

            await PressInteract(receiver);

            Assert.IsTrue(gate.IsOpen,
                "Interact on the far side of the door has to open it. Refused here, the climb is re-walked after " +
                "every death for the rest of the chapter and the shortcut is decoration.");

            GD.Print($"[QA/ChapterTwo] climb: {trace.Describe(climb)}");
        }

        /// <summary>
        /// The other half of the one-way rule, and the half that makes the climb mandatory. A player
        /// running east from the last bonfire meets the door's west face; the door must refuse them.
        /// Opening from here would fold the level's only mandatory platforming away on the first walk-up.
        /// </summary>
        [Test]
        public async Task ClosedShortcut_RefusesThePlayerWhoWalksIntoItsNearFace()
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterTwoSceneName, null);
            float gateX = layout.ShortcutGatePosition.X;

            await LoadChapterTwo();

            PlayerController2D player = RequirePlayer();
            PlayerInputReceiver receiver = TakeTheControls(player);
            ShortcutGate gate = RequireGate();
            DisableEveryEnemy();

            Assert.IsFalse(gate.IsOpen, "Fixture guard: a chapter entered with no slot starts with its door shut.");

            GameClock.TimeScale = FastForward;

            PlaceOn(player, gateX - World.U(9f), layout.CheckpointPositions[0].Y);
            await Settle(20);

            var run = new Run();
            await DriveUntil(player, 1f, () => gate.PlayerInside, budgetSeconds: 20f, run);

            Assert.IsTrue(gate.PlayerInside,
                $"Fixture guard: walking east into the door should reach its trigger. Reached={run.MaxX:F2}px");

            await PressInteract(receiver);

            Assert.IsFalse(gate.IsOpen,
                "The door opened from the near side. Chapter two's only mandatory climb is then optional: the " +
                "player meets the wall on the way out, presses Interact, and walks through the shortcut it was " +
                "supposed to cost a climb to earn.");

            // Two frames, because a refusal that merely defers is not a refusal.
            await Settle(2);
            Assert.IsFalse(gate.IsOpen, "A refused Interact must not open the door a frame later either.");

            // Now lean on it. Being refused is only half the rule - the other half is that the door is a
            // wall while it is refusing, because a shut door the player can walk through makes the climb
            // decoration and nothing anywhere would say so.
            var push = new Run();
            await DriveUntil(player, 1f, () => player.GlobalPosition.X > gateX + World.U(1f), budgetSeconds: 8f, push);

            Assert.IsFalse(push.Arrived,
                "A closed door has to be a wall. The player walked east through it without opening it, so the " +
                $"only mandatory climb in the chapter can be skipped by holding right. maxX={push.MaxX:F2}px " +
                $"gateX={gateX:F2}px");
            Assert.IsFalse(gate.IsOpen, "Pushing on a door must not open it either; the shortcut is an Interact.");
        }

        // ------------------------------------------------------------------------------------------
        // B. What the run-back actually costs
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The run-back [[PlaytimePlan]] budgets at 30-45 seconds, measured rather than derived: the last
        /// bonfire to the boss with the shortcut already open, which is the shape every death after the
        /// first trip takes. Logged, and bounded only at the two ends where a number means something
        /// broke rather than something was retuned.
        /// </summary>
        [Test]
        public async Task RunBack_FromTheLastBonfireToTheBoss_OnAnOpenShortcut()
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterTwoSceneName, null);
            int lastBonfire = layout.CheckpointPositions.Length - 1;
            float bossX = layout.WrathMiniBossSpawnPosition.X;

            await ResumeChapterTwoAt(lastBonfire, shortcutOpened: true);

            PlayerController2D player = RequirePlayer();
            TakeTheControls(player);
            ShortcutGate gate = RequireGate();
            DisableEveryEnemy();

            Assert.IsTrue(gate.IsOpen,
                "Fixture guard: this measures the post-shortcut run-back, so the door has to be open. Shut, the " +
                "run stalls against it and the seconds below would be a timeout rather than a distance.");

            GameClock.TimeScale = FastForward;

            float startX = player.GlobalPosition.X;
            float target = bossX - World.U(7f);            // 7 Unity metres short of the boss
            var run = new Run();

            await DriveUntil(player, 1f, () => player.GlobalPosition.X >= target, budgetSeconds: 120f, run);

            float distance = target - startX;               // px
            GD.Print($"[QA/ChapterTwo] run-back, no resistance: {World.ToUnits(distance):F1} units in " +
                     $"{run.Seconds:F2}s (avg {World.ToUnits(distance / Mathf.Max(run.Seconds, 0.001f)):F2} u/s), " +
                     $"start={World.ToUnits(startX):F1} target={World.ToUnits(target):F1} arrived={run.Arrived}");

            Assert.IsTrue(run.Arrived,
                $"The player never reached the boss on an open shortcut. Got to {World.ToUnits(run.MaxX):F1} of " +
                $"{World.ToUnits(target):F1} in {run.Seconds:F1}s, which means something on the flat ground between " +
                "the last bonfire and the arena is stopping them.");

            // Deliberately not a check on the 30-45s budget. Six units a second over this distance cannot
            // land outside these two, so reaching either one is a broken level or a broken controller,
            // never a designer moving a bonfire. Seconds are seconds in both engines - no conversion.
            Assert.Greater(run.Seconds, 1f,
                "The run-back took no time at all, so the player was already at the boss - the resume did not put " +
                "them at the bonfire this measurement is taken from.");
            Assert.Less(run.Seconds, 110f,
                $"The run-back took {run.Seconds:F1}s with nothing in the way. At the shipped move speed this " +
                "distance is under half a minute, so the player is being held up by something.");
        }

        /// <summary>
        /// The same run-back with the chapter as authored - five placements between the last bonfire and
        /// the boss - so the difference between the two numbers is what the enemies actually cost. This
        /// one is a measurement first: whether a player who declines to fight survives it is a tuning
        /// question, and a suite that answered it would go red every time a grunt moved.
        /// </summary>
        /// <remarks>
        /// The timescale is left alone here. Hit stop writes <see cref="GameClock.TimeScale"/> itself and
        /// restores it to a hard-coded 1, so a fast-forwarded run would quietly drop back to real speed on
        /// the first hit and report a distance nobody can interpret.
        /// </remarks>
        [Test]
        public async Task RunBack_WithTheChapterAsAuthored_TerminatesRatherThanHangs()
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterTwoSceneName, null);
            int lastBonfire = layout.CheckpointPositions.Length - 1;
            float bossX = layout.WrathMiniBossSpawnPosition.X;

            await ResumeChapterTwoAt(lastBonfire, shortcutOpened: true);

            PlayerController2D player = RequirePlayer();
            TakeTheControls(player);
            ShortcutGate gate = RequireGate();

            Assert.IsTrue(gate.IsOpen, "Fixture guard: the post-shortcut run-back needs the door open.");

            float startX = player.GlobalPosition.X;
            float target = bossX - World.U(7f);
            int onTheWay = CountPlacementsBetween(layout, startX, target);
            var run = new Run();

            // Real time, not fast-forwarded, so the budget is also wall clock: kept under the runner's
            // per-test ceiling with the scene load allowed for.
            await DriveUntil(
                player,
                1f,
                () => player.GlobalPosition.X >= target,
                budgetSeconds: 90f,
                run,
                unstickWithJumps: true);

            float distance = target - startX;               // px
            GD.Print($"[QA/ChapterTwo] run-back, as authored: {World.ToUnits(distance):F1} units, " +
                     $"{onTheWay} placements on the way, arrived={run.Arrived} in {run.Seconds:F2}s " +
                     $"(avg {World.ToUnits(distance / Mathf.Max(run.Seconds, 0.001f)):F2} u/s), " +
                     $"maxX={World.ToUnits(run.MaxX):F1}, hops to get past bodies={run.Jumps}, " +
                     $"deepest health dip={run.HealthLost:F0}, deaths={run.Deaths}" +
                     (run.Deaths > 0 ? " - SECONDS INCLUDE A DEATH AND A RESTART, read them with that." : string.Empty));

            // The only thing here that is not a tuning opinion. Either the player gets there or the chapter
            // kills them; standing still for two minutes at full pace with neither outcome means something
            // is holding the body - an enemy that is a wall, a stuck state, a trap in the floor.
            Assert.IsTrue(run.Arrived || run.Died,
                $"The run-back neither finished nor killed the player in {run.Seconds:F0}s. They stopped at " +
                $"{World.ToUnits(run.MaxX):F1} of {World.ToUnits(target):F1} having lost {run.HealthLost:F0} " +
                "health, which is a body being held rather than a fight being lost.");
        }

        /// <summary>
        /// The same leg the two run-backs above take, but with the door shut - so the difference between
        /// this number and the open-shortcut one is what the shortcut is worth in seconds, which is the
        /// question the bonfire and door placement gets decided on.
        ///
        /// Same start, same finish, same empty road: only the door differs. Nothing here asserts a budget,
        /// because the seconds are the deliverable and a bound on them is a bound on the designer.
        /// </summary>
        [Test]
        public async Task FirstPass_FromTheLastBonfireOverTheClosedDoor_IsWhatTheShortcutFoldsUp()
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterTwoSceneName, null);
            List<Rung> climb = ReadTheClimb(layout);
            float lastBonfireX = layout.CheckpointPositions[layout.CheckpointPositions.Length - 1].X;
            float bossX = layout.WrathMiniBossSpawnPosition.X;
            float target = bossX - World.U(7f);

            // No slot, so the door builds shut - which is the state a first pass is in.
            await LoadChapterTwo();

            PlayerController2D player = RequirePlayer();
            TakeTheControls(player);
            ShortcutGate gate = RequireGate();
            DisableEveryEnemy();

            Assert.IsFalse(gate.IsOpen, "Fixture guard: this is the closed-door pass, so the door has to be shut.");

            GameClock.TimeScale = FastForward;

            PlaceOn(player, lastBonfireX, layout.CheckpointPositions[0].Y);
            await Settle(20);

            var trace = new ClimbTrace();
            await DriveTheClimb(player, climb, target, budgetSeconds: 60f, trace);

            float distance = target - lastBonfireX;         // px
            GD.Print($"[QA/ChapterTwo] first pass, door shut: {World.ToUnits(distance):F1} units from the last " +
                     $"bonfire in {trace.Seconds:F2}s " +
                     $"(avg {World.ToUnits(distance / Mathf.Max(trace.Seconds, 0.001f)):F2} u/s), " +
                     $"arrived={trace.Arrived}. Compare against the open-shortcut run over the same two points.");

            Assert.IsTrue(trace.Arrived,
                "The last bonfire has to reach the boss with the door shut, or the chapter cannot be finished on " +
                "a first pass at all. " + trace.Describe(climb));
        }

        // ------------------------------------------------------------------------------------------
        // C. The multi-bonfire resume, which no shipped layout could exercise until this one
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Chapter two is the first arena to author more than one bonfire, so it is the first thing that
        /// can prove <c>CreateCheckpoints(activeIndex &gt; 0)</c> and
        /// <c>GameplaySaveBridge.ResumeShortcutOpened</c> at all. Both fail the same silent way - the
        /// player wakes up at the chapter's first bonfire behind a door they already opened, which reads
        /// as "the save did not take" and loses a 300-unit walk.
        /// </summary>
        [Test]
        public async Task ResumedChapterTwo_StartsAtTheRecordedBonfire_WithItsDoorAlreadyOpen()
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterTwoSceneName, null);

            Assert.GreaterOrEqual(layout.CheckpointPositions.Length, 3,
                "Fixture guard: this is the only test of a resume at a bonfire that is not the first, so chapter " +
                "two has to still author more than one.");

            int lastBonfire = layout.CheckpointPositions.Length - 1;
            float expectedX = layout.CheckpointPositions[lastBonfire].X;
            float firstX = layout.CheckpointPositions[0].X;

            await ResumeChapterTwoAt(lastBonfire, shortcutOpened: true);

            PlayerController2D player = RequirePlayer();

            // Tolerance was 1 Unity unit; both sides are pixels now.
            Assert.AreEqual(expectedX, player.GlobalPosition.X, World.U(1f),
                $"A slot recording bonfire {lastBonfire} has to resume there. The player woke up at " +
                $"{World.ToUnits(player.GlobalPosition.X):F1} instead of {World.ToUnits(expectedX):F1}; the " +
                $"chapter's first bonfire is at {World.ToUnits(firstX):F1}, and landing on it means the recorded " +
                "index was thrown away and the whole level is in front of the player again.");

            ShortcutGate gate = RequireGate();
            Assert.IsTrue(gate.IsOpen,
                "A slot that recorded the shortcut open has to build the door open. Shut, the player is behind a " +
                "one-way door they already paid the climb for, on the wrong side to open it again.");

            // Open means gone, not merely faded: the run-back walks through the space the door was in.
            //
            // PORT: Unity walked every Collider2D on the gate GameObject and asserted the non-trigger ones
            // were disabled. Here the door is the StaticBody2D the environment builder made and the gate
            // is a child node of it, so the solid shape is the parent body's CollisionShape2D; the Interact
            // reach is a separate child Area2D and is meant to stay live. ShortcutGate.SetOpen writes the
            // shape deferred - Godot forbids re-shaping a collider inside a physics callback - so this
            // needs a step to settle before it can be read.
            await Settle(2);

            var doorShape = gate.GetParent().GetComponent<CollisionShape2D>();
            Assert.NotNull(doorShape, "The gate has to hang under the door body whose collider it disables.");
            Assert.IsTrue(doorShape.Disabled,
                "An open door still has its body collider on, so it is a wall that only looks passable.");
        }

        // ------------------------------------------------------------------------------------------
        // Driving
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Holds a direction until it arrives or the budget runs out. One step per physics tick rather
        /// than per frame, so the timescale cannot coarsen the decisions - at six units a second a
        /// frame-paced loop under fast-forward would be deciding every half metre.
        /// </summary>
        private static async Task DriveUntil(
            PlayerController2D player,
            float direction,
            Func<bool> arrived,
            float budgetSeconds,
            Run run = null,
            bool unstickWithJumps = false)
        {
            run ??= new Run();
            var health = player.GetComponentInParent<Health>();
            float startHealth = health != null ? health.CurrentHealth : 0f;
            float lowest = startHealth;
            var wasDead = false;

            // A death and its respawn both land inside one physics step, so the IsDead edge below can miss
            // one entirely - and the respawn refills the bar, so the health floor misses it too. A run that
            // died and walked back would report "arrived, no deaths". The event is the only witness.
            // See GameplayCombatCostTests, where this same hole read a boss as harmless for a session.
            var deathState = player.GetComponentInParent<DeathStateController>();
            Action onRespawn = () =>
            {
                run.Died = true;
                run.Deaths++;
            };
            if (deathState != null)
                deathState.OnRespawn += onRespawn;                // PORT: UnityEvent.AddListener

            float start = GameClock.Time;
            float deadline = start + budgetSeconds;
            run.MaxX = player.GlobalPosition.X;

            float markX = run.MaxX;
            float markTime = start;
            float stallProgress = World.U(StallProgressUnits);    // metres of travel -> pixels

            while (GameClock.Time < deadline)
            {
                // x is unchanged between the two spaces; only a y component would need flipping.
                player.SetMoveInput(new Vector2(direction, 0f));

                float x = player.GlobalPosition.X;
                run.MaxX = Mathf.Max(run.MaxX, x);

                // An enemy body is a wall in this game: the 2026-08-16 run stopped dead at the grunt on
                // 218 with 90 seconds and no deaths to show for it. A player gets past one by killing it
                // or hopping it, and hopping is the one that keeps this a measurement of travel rather
                // than of combat.
                //
                // ponytail: a stall timer, not a sensor. It cannot tell an enemy from a wall from a ledge
                // it keeps sliding back down - it just jumps whenever forward progress stops. Swap it for
                // a forward cast if a level ever needs to know which of those it hit.
                if (unstickWithJumps)
                {
                    if ((x - markX) * direction > stallProgress)
                    {
                        markX = x;
                        markTime = GameClock.Time;
                    }
                    else if (GameClock.Time - markTime > StallSeconds && player.IsGrounded)
                    {
                        player.RequestJump();
                        run.Jumps++;
                        markX = x;
                        markTime = GameClock.Time;
                    }
                }

                if (health != null)
                {
                    lowest = Mathf.Min(lowest, health.CurrentHealth);

                    // Counted off OnRespawn above, not here: this sample sees a death only when the run
                    // happens to catch it mid-death, and the respawn already counted that one.
                    bool dead = health.IsDead;
                    if (dead && !wasDead && deathState == null)
                    {
                        run.Died = true;
                        run.Deaths++;
                    }

                    wasDead = dead;
                }

                if (arrived())
                {
                    run.Arrived = true;
                    break;
                }

                await TestContext.Runner.NextPhysicsFrame();
            }

            if (deathState != null)
                deathState.OnRespawn -= onRespawn;

            run.Seconds = GameClock.Time - start;
            run.HealthLost = startHealth - lowest;
            player.SetMoveInput(Vector2.Zero);
        }

        /// <summary>
        /// Runs east and jumps at each rung's takeoff point, which is the route a player takes rather
        /// than a route a solver finds: the level is authored, and what is under test is whether the
        /// authored line is physically walkable, not whether an agent can discover it.
        /// </summary>
        private static async Task DriveTheClimb(
            PlayerController2D player,
            List<Rung> climb,
            float target,
            float budgetSeconds,
            ClimbTrace trace)
        {
            var hop = 0;
            bool wasGrounded = player.IsGrounded;
            float chainEnd = Right(climb[climb.Count - 1]);
            float start = GameClock.Time;
            float deadline = start + budgetSeconds;

            trace.StartedAt = player.GlobalPosition;

            while (GameClock.Time < deadline)
            {
                player.SetMoveInput(Vector2.Right);

                Vector2 at = player.GlobalPosition;
                bool grounded = player.IsGrounded;

                // Only on the transition. A jump is still reported grounded on the frame it is asked for
                // - the ground probe runs before the body has moved - so a state test here would call
                // every takeoff a landing.
                if (grounded && !wasGrounded)
                {
                    trace.Landings.Add(at);

                    // Coming down on the arena floor while the chain is still ahead is a fall, not a
                    // route. Caught here rather than left to the budget so the failure names the rung it
                    // fell short of instead of reporting a timeout at a position nobody can place. Past
                    // the last rung the floor is the route: that drop is how the player gets down beyond
                    // the door.
                    //
                    // SIGN FLIP: Unity's `at.y < 1f` is `at.Y > FloorLevelY` here. See FloorLevelY.
                    if (hop > 0 && at.Y > FloorLevelY && at.X < chainEnd)
                    {
                        trace.FellAfterHop = hop - 1;
                        break;
                    }
                }

                if (grounded && hop < climb.Count && at.X >= climb[hop].TakeoffX)
                {
                    player.RequestJump();
                    trace.Takeoffs.Add(at);
                    hop++;
                }

                // SIGN FLIP, same one: "past the target and back down on the floor".
                if (at.X >= target && at.Y > FloorLevelY)
                {
                    trace.Arrived = true;
                    break;
                }

                wasGrounded = grounded;
                await TestContext.Runner.NextPhysicsFrame();
            }

            trace.HopsTaken = hop;
            trace.EndedAt = player.GlobalPosition;
            trace.Seconds = GameClock.Time - start;
            player.SetMoveInput(Vector2.Zero);
        }

        // ------------------------------------------------------------------------------------------
        // Scene and fixture plumbing
        // ------------------------------------------------------------------------------------------

        private const string InteractAction = "interact";

        /// <summary>
        /// PORT: Unity's <c>receiver.OnInteract.Invoke()</c> has no counterpart - the ported
        /// <see cref="PlayerInputReceiver.OnInteract"/> is a C# <c>event</c>, and only its declaring class
        /// may raise one. So the press goes through the real route instead: the receiver is switched back
        /// on for the length of it and the <c>interact</c> action is driven synthetically, which also
        /// covers the subscription the Unity version stepped over.
        /// </summary>
        /// <remarks>
        /// The action is re-pressed on every frame of the window rather than held. Godot's
        /// <c>IsActionJustPressed</c> is true only on the process frame the press was recorded in, and a
        /// test that presses from outside <c>_Process</c> cannot know whether the receiver has already
        /// ticked this frame - re-pressing makes the edge land whichever way that falls.
        /// </remarks>
        private static async Task PressInteract(PlayerInputReceiver receiver)
        {
            bool wasEnabled = receiver.Enabled;
            receiver.Enabled = true;

            for (var frame = 0; frame < 3; frame++)
            {
                Input.ActionRelease(InteractAction);
                Input.ActionPress(InteractAction);
                await TestContext.Runner.NextFrame();
            }

            Input.ActionRelease(InteractAction);
            await TestContext.Runner.NextFrame();
            receiver.Enabled = wasEnabled;
        }

        private static async Task LoadChapterTwo()
        {
            // LoadScene already spends the two frames ChangeSceneToFile needs before the new tree is
            // usable, which is what the Unity helper's trailing `yield return null` was for.
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(ChapterTwoSceneName));
        }

        /// <summary>
        /// Enters chapter two the way Continue does: a slot naming this chapter, a recorded bonfire, and
        /// the flag the title menu sets. Nothing else reaches the multi-checkpoint resume path.
        /// </summary>
        private static async Task ResumeChapterTwoAt(int checkpointIndex, bool shortcutOpened)
        {
            GameSave.Write(new GameSaveData
            {
                chapterScene = ChapterTwoSceneName,
                checkpointIndex = checkpointIndex,
                shortcutOpened = shortcutOpened,

                // The three sentinels Apply is documented to leave alone, so this slot restores a place
                // and a door and says nothing about the player's condition.
                health = -1f,
                humanity = -1f,
                souls = -1
            });

            GameSave.LoadOnNextGameplayStart = true;
            await LoadChapterTwo();
        }

        private static async Task Settle(int fixedSteps)
        {
            for (var i = 0; i < fixedSteps; i++)
                await TestContext.Runner.NextPhysicsFrame();
        }

        private static PlayerController2D RequirePlayer()
        {
            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "Chapter two has to spawn a player.");
            return player;
        }

        private static ShortcutGate RequireGate()
        {
            var gate = SceneQuery.FindFirst<ShortcutGate>();
            Assert.NotNull(gate,
                "Chapter two authors hasShortcutGate, so the arena has to build the door. Without it the level " +
                "has no wall, the climb is optional, and everything else here is measuring an easier chapter.");
            return gate;
        }

        /// <summary>
        /// Takes the controls the way the PlayMode agents do. Not optional: the receiver writes the move
        /// input from a dead keyboard every frame, so a driven run with it enabled is a player being
        /// told to stand still between every physics step.
        /// </summary>
        private static PlayerInputReceiver TakeTheControls(PlayerController2D player)
        {
            var receiver = player.GetComponentInParent<PlayerInputReceiver>();
            Assert.NotNull(receiver, "The shipped player carries the input receiver these runs have to switch off.");
            receiver.Enabled = false;                             // PORT: Unity's Behaviour.enabled
            return receiver;
        }

        /// <summary>
        /// PORT: <c>gameObject.SetActive(false)</c> is <c>GameplayBuildShim.SetActive</c> - hide, stop
        /// processing and disable the descendant collision shapes. A Godot node that is only hidden is
        /// still solid, and a solid "disabled" grunt is the wall these runs exist to rule out.
        /// </summary>
        private static int DisableEveryEnemy()
        {
            List<EnemyStateMachine> machines = SceneQuery.FindAll<EnemyStateMachine>();

            foreach (EnemyStateMachine machine in machines)
                machine.SetActive(false);

            return machines.Count;
        }

        /// <summary>
        /// Puts the player somewhere. Both coordinates are Godot pixels already - the callers take them
        /// off the layout or wrap their own metres in <see cref="World.U"/>.
        /// </summary>
        /// <remarks>
        /// PORT: Unity had to switch the Rigidbody2D's simulation off across the move, then write both
        /// <c>transform.position</c> and <c>Rigidbody2D.position</c> so interpolation could not drag the
        /// body back. None of that has a counterpart. A <see cref="CharacterBody2D"/> is only ever moved
        /// by <c>MoveAndSlide</c> inside its own physics tick, so writing <c>GlobalPosition</c> between
        /// steps <i>is</i> the teleport, and it sweeps nothing on the way. The velocity is still zeroed:
        /// carrying the old one into a 200-unit jump would fling the body off the landing.
        /// </remarks>
        private static void PlaceOn(PlayerController2D player, float x, float y)
        {
            var body = player.GetComponentInParent<PlayerMotor2D>();
            if (body == null)
            {
                player.GlobalPosition = new Vector2(x, y);
                return;
            }

            body.Velocity = Vector2.Zero;
            body.GlobalPosition = new Vector2(x, y);
            body.Velocity = Vector2.Zero;
        }

        private static int CountPlacementsBetween(GameplaySceneDefaults layout, float fromX, float toX)
        {
            var count = 0;
            count += CountIn(layout.MeleeGruntSpawns, fromX, toX);
            count += CountIn(layout.LeapingAttackerSpawns, fromX, toX);
            count += CountIn(layout.RangedCasterSpawns, fromX, toX);
            return count;
        }

        private static int CountIn(GameplaySceneDefaults.EnemySpawn[] spawns, float fromX, float toX)
        {
            if (spawns == null)
                return 0;

            var count = 0;
            foreach (GameplaySceneDefaults.EnemySpawn spawn in spawns)
            {
                if (spawn.Position.X > fromX && spawn.Position.X < toX)
                    count++;
            }

            return count;
        }

        /// <summary>Godot's Vector2 has no NUnit-style format overload the tests can lean on, so this is it.</summary>
        private static string Fmt(Vector2 v) => $"({v.X:F2}, {v.Y:F2})";

        // ------------------------------------------------------------------------------------------
        // The climb, read off the layout
        // ------------------------------------------------------------------------------------------

        /// <summary>One platform of the mandatory chain, plus the point a run leaves the previous one at.</summary>
        private readonly struct Rung
        {
            public Rung(GameplaySceneDefaults.PlatformDefinition platform, float takeoffX)
            {
                Platform = platform;
                TakeoffX = takeoffX;
            }

            public GameplaySceneDefaults.PlatformDefinition Platform { get; }

            /// <summary>Where the hop that lands on this rung is launched from. Godot pixels.</summary>
            public float TakeoffX { get; }
        }

        private static float Left(Rung rung) => rung.Platform.Position.X - rung.Platform.Size.X * 0.5f;
        private static float Right(Rung rung) => rung.Platform.Position.X + rung.Platform.Size.X * 0.5f;

        /// <summary>
        /// SIGN FLIP: the surface of a platform. Unity added half the height to reach the top; with +Y
        /// down it is subtracted. Copied straight across, this would name the platform's underside and
        /// every geometry guard built on it would be measuring the wrong edge.
        /// </summary>
        private static float Top(Rung rung) => rung.Platform.Position.Y - rung.Platform.Size.Y * 0.5f;

        /// <summary>
        /// Derives the route from the shipped layout rather than restating it. Two rules, both about
        /// where a jump is committed rather than how far it goes, so retuning the jump is measured by the
        /// run instead of being baked into the plan the run follows.
        /// </summary>
        private static List<Rung> ReadTheClimb(GameplaySceneDefaults layout)
        {
            var climb = new List<Rung>(MandatoryClimb.Length);

            for (var i = 0; i < MandatoryClimb.Length; i++)
            {
                GameplaySceneDefaults.PlatformDefinition platform = RequirePlatform(layout, MandatoryClimb[i]);

                // The first rung is entered off the arena floor, which runs under the whole chain: there
                // is no ledge to leave, so the launch is placed relative to the platform being aimed at.
                // Every later one is a ledge hop, taken as late as the platform allows. Both leads are
                // authored in metres and converted here; the platform coordinates are already pixels.
                float takeoffX = i == 0
                    ? platform.Position.X - platform.Size.X * 0.5f - World.U(GroundLaunchLeadUnits)
                    : Right(climb[i - 1]) - World.U(LedgeMarginUnits);

                climb.Add(new Rung(platform, takeoffX));
            }

            return climb;
        }

        private static GameplaySceneDefaults.PlatformDefinition RequirePlatform(GameplaySceneDefaults layout, string name)
        {
            foreach (GameplaySceneDefaults.PlatformDefinition platform in layout.Platforms)
            {
                if (platform.Name == name)
                    return platform;
            }

            Assert.Fail(
                $"Chapter two's layout no longer has a platform called '{name}'. That name is one rung of the only " +
                "route past the closed shortcut, so either the climb was re-authored - in which case this list needs " +
                "updating - or the chapter just lost its way through.");
            return default;
        }

        private sealed class Run
        {
            public bool Arrived;
            public bool Died;
            public int Deaths;
            public int Jumps;
            public float Seconds;
            public float MaxX;
            public float HealthLost;
        }

        private sealed class ClimbTrace
        {
            public bool Arrived;
            public int HopsTaken;
            public int FellAfterHop = -1;
            public float Seconds;
            public Vector2 StartedAt;
            public Vector2 EndedAt;
            public readonly List<Vector2> Takeoffs = new();
            public readonly List<Vector2> Landings = new();

            public string Describe(List<Rung> climb)
            {
                var text = new StringBuilder();
                text.Append("hops=").Append(HopsTaken).Append('/').Append(climb.Count);
                text.Append(" seconds=").Append(Seconds.ToString("F2"));
                text.Append(" startedAt=").Append(Fmt(StartedAt));
                text.Append(" endedAt=").Append(Fmt(EndedAt));

                if (FellAfterHop >= 0)
                {
                    string from = FellAfterHop == 0 ? "the arena floor" : climb[FellAfterHop - 1].Platform.Name;
                    text.Append(" FELL: the hop from ").Append(from)
                        .Append(" to ").Append(climb[FellAfterHop].Platform.Name)
                        .Append(" came down on the floor instead of the platform.");
                }

                for (var i = 0; i < Takeoffs.Count; i++)
                {
                    text.Append(" | ").Append(climb[i].Platform.Name)
                        .Append(" takeoff=").Append(Fmt(Takeoffs[i]));

                    if (i < Landings.Count)
                        text.Append(" landing=").Append(Fmt(Landings[i]));
                }

                return text.ToString();
            }
        }
    }
}
