using System;
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
    /// Every chapter, walked. <see cref="GameplayChapterTwoTraversalTests"/> proved chapter two can be
    /// finished and measured what it costs; chapters three to eight were authored to the same shape with
    /// nothing but arithmetic behind them, and chapter one is being re-authored now. This is that harness
    /// generalised: two runs per chapter, both driving the shipped player through the shipped arena.
    ///
    /// 1. <b>Can it be finished.</b> First bonfire to the boss's engagement line with the arena emptied,
    ///    so the answer is about the level and not about a grunt that killed the run at 218. This is the
    ///    one that matters: six of these levels have never had a body moved through them.
    /// 2. <b>What the run-back costs.</b> Last bonfire to the same line with the chapter as authored -
    ///    seconds, average speed, hops spent climbing over bodies, deaths. That is the number
    ///    [[PlaytimePlan]] Phase 2 asks for, and the number three rounds of hand arithmetic got wrong on
    ///    chapter two before this harness existed.
    ///
    /// <b>The output is the log, not the green.</b> Every coordinate is derived from
    /// <see cref="GameplaySceneDefaults.CreateForScene"/> and every boss line from the chapter's own
    /// encounter file, so a designer moving a bonfire re-derives the run instead of reddening it. The
    /// assertions are only arrival and bounds no plausible retune can reach; the seconds are logged under
    /// <c>[QA/ChNN]</c>.
    ///
    /// One test method per chapter per question rather than one parameterised pair, so a chapter that
    /// cannot be walked names itself in the results and one hanging chapter cannot swallow the other
    /// seven's numbers.
    ///
    /// Written during the QA review of the chapter 3-8 authoring pass and NOT verified - nothing here has
    /// been run.
    /// </summary>
    /// <remarks>
    /// PORT - UNITS. Everything <see cref="GameplaySceneDefaults"/> hands back is already Godot pixels
    /// with +Y down, so the bonfire and boss coordinates need no conversion. Every number this file
    /// authors itself is still the Unity metre it was written as and is wrapped in
    /// <see cref="World.U"/> at the point of use; anything logged as "units" is divided back down by
    /// <see cref="World.ToUnits"/>. Nothing here reads a y coordinate, so no sign flips - the runs are
    /// horizontal and x is unchanged between the two spaces.
    /// </remarks>
    public sealed class GameplayChapterTraversalTests
    {
        private const string ChapterOne = "GameplayScene";
        private const string ChapterTwo = "Chapter02_Orange";
        private const string ChapterThree = "Chapter03_Yellow";
        private const string ChapterFour = "Chapter04_Green";
        private const string ChapterFive = "Chapter05_Blue";
        private const string ChapterSix = "Chapter06_Indigo";
        private const string ChapterSeven = "Chapter07_Violet";
        private const string ChapterEight = "Chapter08_White";

        /// <summary>
        /// The chapters this file drives, in road order. Held against <see cref="ChapterRoute.Scenes"/> by
        /// <see cref="EveryChapterOnTheRoad_IsDrivenByThisHarness"/>, so a ninth gate cannot ship
        /// unmeasured.
        /// </summary>
        private static readonly string[] DrivenChapters =
        {
            ChapterOne, ChapterTwo, ChapterThree, ChapterFour,
            ChapterFive, ChapterSix, ChapterSeven, ChapterEight
        };

        /// <summary>Loaded back at the end of every test, so the next fixture gets the arena it expects.</summary>
        private const string FallbackSceneName = ChapterOne;

        /// <summary>
        /// How far short of the boss's own detection range a run stops. The line is where the fight would
        /// start, and stopping a unit before it means the measurement ends with travel rather than with an
        /// intro cutscene and a boss walking into frame.
        /// </summary>
        private const float EngagementMarginUnits = 1f;

        /// <summary>
        /// Physics steps are unaffected by the timescale - only their spacing in wall-clock time is - so
        /// this buys back most of the minutes eight 300-unit levels would otherwise cost the suite without
        /// changing a simulation result. Only ever used on an emptied arena: hit stop writes the timescale
        /// itself, and a run with live enemies would be dropped back to 1 by the first hit that lands.
        /// </summary>
        private const float FastForward = 4f;

        /// <summary>
        /// Game seconds, not wall clock, because <see cref="GameClock.Time"/> is what the budget is
        /// checked against. 300 units at the shipped speed is under a minute; this is wide enough that
        /// only a level that cannot be crossed reaches it.
        /// </summary>
        private const float WalkBudgetSeconds = 90f;

        /// <summary>
        /// Real seconds here, because the armed run cannot be fast-forwarded. Chapter two's measured
        /// run-back is 25.9s and the legs here are 110-150 units, so this is roughly twice what a bad run
        /// should need - and still well under the runner's three-minute per-test ceiling with the two
        /// scene loads allowed for.
        /// </summary>
        private const float RunBackBudgetSeconds = 100f;

        /// <summary>
        /// Nothing in this project travels faster than this, so a run that averages more has not travelled
        /// at all - the target was already behind the start, or the body was teleported onto it. Unity
        /// units per second; compared against a px/s speed through <see cref="World.U"/>.
        /// </summary>
        private const float ImpossibleSpeedUnits = 15f;

        /// <summary>How little forward travel counts as being held, and for how long, before the driver hops.</summary>
        private const float StallProgressUnits = 0.5f;
        private const float StallSeconds = 0.5f;

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

            // PORT: Unity's Time.fixedDeltaTime was writable and was saved/restored here. Godot's physics
            // step is a project setting with no setter on GameClock and nothing in the port writes it, so
            // there is nothing to hold onto.

            // The entry shot takes PlayerInputReceiver for its length and hands it back when it ends. A
            // driven run that started during the shot would have the controls returned to a receiver
            // reading a dead keyboard part-way through and stop where it stood - which reads exactly like
            // a level that cannot be walked.
            CutsceneDirector.SkipAll = true;

            // Every run-back second below is a Normal-difficulty second at a clock of 1. Left to whatever
            // the previous fixture set, the same chapter would measure differently between two runs of the
            // same suite, and the deaths in the log would be unreadable.
            DifficultySettings.Set(Difficulty.Normal, 0);
            GameClock.TimeScale = 1f;

            // A fresh chapter unless a test writes a slot and asks for one: the resume path reads real
            // PlayerPrefs, and a slot left by another fixture would drop the player at someone else's
            // bonfire with someone else's door open.
            //
            // PORT: DeleteAll rather than GameSave.Clear. The Godot PlayerPrefs is a ConfigFile at
            // user://playerprefs.cfg that survives between headless runs, so "another fixture" now
            // includes yesterday's run - the whole store is wiped, and the slot read above is put back in
            // teardown for the sibling fixtures in this process.
            PlayerPrefs.DeleteAll();
        }

        [TearDown]
        public async Task TearDownAndReleaseTheScene()
        {
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
        // A. Can each chapter be finished at all - the emptied arena, first bonfire to the boss line
        // ------------------------------------------------------------------------------------------

        [Test] public Task ChapterOne_IsWalkableFromItsFirstBonfireToItsBoss() => WalkTheChapter(ChapterOne);
        [Test] public Task ChapterTwo_IsWalkableFromItsFirstBonfireToItsBoss() => WalkTheChapter(ChapterTwo);
        [Test] public Task ChapterThree_IsWalkableFromItsFirstBonfireToItsBoss() => WalkTheChapter(ChapterThree);
        [Test] public Task ChapterFour_IsWalkableFromItsFirstBonfireToItsBoss() => WalkTheChapter(ChapterFour);
        [Test] public Task ChapterFive_IsWalkableFromItsFirstBonfireToItsBoss() => WalkTheChapter(ChapterFive);
        [Test] public Task ChapterSix_IsWalkableFromItsFirstBonfireToItsBoss() => WalkTheChapter(ChapterSix);
        [Test] public Task ChapterSeven_IsWalkableFromItsFirstBonfireToItsBoss() => WalkTheChapter(ChapterSeven);
        [Test] public Task ChapterEight_IsWalkableFromItsFirstBonfireToItsBoss() => WalkTheChapter(ChapterEight);

        // ------------------------------------------------------------------------------------------
        // B. What each chapter's run-back costs - the arena as authored, last bonfire to the boss line
        // ------------------------------------------------------------------------------------------

        [Test] public Task ChapterOne_RunBackFromTheLastBonfire_AsAuthored() => MeasureTheRunBack(ChapterOne);
        [Test] public Task ChapterTwo_RunBackFromTheLastBonfire_AsAuthored() => MeasureTheRunBack(ChapterTwo);
        [Test] public Task ChapterThree_RunBackFromTheLastBonfire_AsAuthored() => MeasureTheRunBack(ChapterThree);
        [Test] public Task ChapterFour_RunBackFromTheLastBonfire_AsAuthored() => MeasureTheRunBack(ChapterFour);
        [Test] public Task ChapterFive_RunBackFromTheLastBonfire_AsAuthored() => MeasureTheRunBack(ChapterFive);
        [Test] public Task ChapterSix_RunBackFromTheLastBonfire_AsAuthored() => MeasureTheRunBack(ChapterSix);
        [Test] public Task ChapterSeven_RunBackFromTheLastBonfire_AsAuthored() => MeasureTheRunBack(ChapterSeven);
        [Test] public Task ChapterEight_RunBackFromTheLastBonfire_AsAuthored() => MeasureTheRunBack(ChapterEight);

        /// <summary>
        /// The list above against the road itself. A ninth gate added to the scene folder and not to this
        /// file would otherwise ship with nobody having walked it, which is the exact hole chapters three
        /// to eight sat in until now.
        /// </summary>
        [Test]
        public void EveryChapterOnTheRoad_IsDrivenByThisHarness()
        {
            string[] road = ChapterRoute.Scenes();

            Assert.Greater(road.Length, 0,
                "The scene folder carries no chapters at all, so nothing below is driving the shipped campaign.");

            // PORT: NUnit's CollectionAssert.AreEqual is not in the harness. Comparing the two as one
            // joined string keeps the ordered, whole-list comparison and puts both lists in the message,
            // which is what the Unity failure printed anyway.
            Assert.AreEqual(string.Join(", ", DrivenChapters), string.Join(", ", road),
                "The chapters this file drives are not the chapters the road is made of. A gate that is on the " +
                "road and not in DrivenChapters has never had a body moved through it; one that is here and not " +
                "on the road is a scene the campaign cannot reach. Add the missing pair of tests, or drop them.");
        }

        // ------------------------------------------------------------------------------------------
        // The two runs
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Can this chapter be finished. Drives the shipped player from where the chapter starts them to
        /// the line the boss notices them at, with every enemy switched off - so the answer is about
        /// ground, platforms, doors and the fall line rather than about combat, which the run-back below
        /// measures separately.
        /// </summary>
        /// <remarks>
        /// Hops are allowed and counted rather than forbidden. A chapter whose road is clear reports
        /// <c>hops=0</c> and is walked; one that reports hops is still finishable, but something on the
        /// authored line has to be climbed - a platform's underside in the walking envelope, a door, a
        /// step - and that is a fact worth having in the log rather than a failure.
        ///
        /// Entered through the resume path at bonfire 0 rather than as a fresh run, so any shortcut the
        /// chapter authors is already open. That is deliberate: chapter two's door is a wall on a first
        /// pass and is meant to be, and whether its climb clears it is
        /// <see cref="GameplayChapterTwoTraversalTests"/>'s question, not this one's. Here the question is
        /// whether the road itself goes all the way.
        /// </remarks>
        private async Task WalkTheChapter(string sceneName)
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(sceneName, null);
            float bossLine = EngagementLine(layout);
            float firstBonfireX = layout.CheckpointPositions[0].X;
            string tag = Tag(sceneName);

            await ResumeChapterAt(sceneName, 0);

            PlayerController2D player = RequirePlayer(sceneName);
            TakeTheControls(player);

            int switchedOff = DisableEveryEnemy();
            Assert.Greater(switchedOff, 0,
                $"Fixture guard: {sceneName} spawned no enemies at all, so this run is not measuring the shipped " +
                "arena. Either the layout stopped placing them or the bootstrap stopped building them.");

            float startX = player.GlobalPosition.X;

            // Tolerance was 2 Unity units; both sides of the comparison are pixels now.
            Assert.AreEqual(firstBonfireX, startX, World.U(2f),
                $"Fixture guard: {sceneName} should start the player at its first bonfire " +
                $"({World.ToUnits(firstBonfireX):F1}); they woke up at {World.ToUnits(startX):F1} instead, so the " +
                "distance below is not the one the chapter asks a player to walk.");
            Assert.Less(startX, bossLine,
                $"Fixture guard: {sceneName} starts the player at {World.ToUnits(startX):F1}, which is already past " +
                $"the boss line at {World.ToUnits(bossLine):F1}. There is no chapter in front of them to walk.");

            GameClock.TimeScale = FastForward;

            var run = new Run();
            await DriveEast(player, () => player.GlobalPosition.X >= bossLine, WalkBudgetSeconds, run);

            float distance = bossLine - startX;                                  // px
            float speed = distance / Mathf.Max(run.Seconds, 0.001f);             // px/s

            GD.Print($"[QA/{tag}] {sceneName} walkable, arena emptied and any shortcut open: " +
                     $"{World.ToUnits(distance):F1} units from the first bonfire ({World.ToUnits(startX):F1}) to the " +
                     $"boss line ({World.ToUnits(bossLine):F1}) in {run.Seconds:F2}s " +
                     $"(avg {World.ToUnits(speed):F2} u/s), hops={run.Jumps}, arrived={run.Arrived}, " +
                     $"maxX={World.ToUnits(run.MaxX):F1}, deaths={run.Deaths}, " +
                     $"enemiesDisabled={switchedOff}, bonfires={layout.CheckpointPositions.Length}");

            Assert.IsTrue(run.Arrived,
                $"{sceneName} cannot be finished. With every enemy switched off the player still never reached the " +
                $"boss line at {World.ToUnits(bossLine):F1} - they stopped at {World.ToUnits(run.MaxX):F1} after " +
                $"{run.Seconds:F0}s and {run.Jumps} hops, having died {run.Deaths} time(s). Something on the " +
                "authored road is a wall, a hole or a fall line.");

            // Not a check on the seconds - those are a tuning number. This only catches the two ways the
            // measurement itself can be a lie: a distance nobody could travel that fast, or a chapter that
            // is not a level at all. Both thresholds are Unity units scaled to pixels.
            Assert.Less(speed, World.U(ImpossibleSpeedUnits),
                $"{sceneName} was crossed at {World.ToUnits(speed):F1} u/s, which nothing in this project can do. " +
                "The boss line was behind the start, or the player was placed on it rather than walking to it.");
            Assert.Greater(distance, World.U(5f),
                $"{sceneName} puts its first bonfire {World.ToUnits(distance):F1} units from its boss line. That is " +
                "not a level - the layout has probably collapsed back to the shipped 36-unit arena's defaults.");
        }

        /// <summary>
        /// What a death costs in this chapter: the last bonfire to the boss line with every placement
        /// where the designer put it, entered the way Continue enters it. The seconds are the deliverable
        /// - [[PlaytimePlan]] budgets 30-45 - and nothing here asserts them, because a bound on a tuning
        /// number is a suite that goes red every time a bonfire moves.
        /// </summary>
        /// <remarks>
        /// Real time, not fast-forwarded: hit stop writes <see cref="GameClock.TimeScale"/> itself and
        /// restores it to a hard-coded 1, so a fast-forwarded armed run would quietly drop to real speed
        /// on the first hit that lands.
        /// </remarks>
        private async Task MeasureTheRunBack(string sceneName)
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(sceneName, null);
            int lastBonfire = layout.CheckpointPositions.Length - 1;
            float expectedX = layout.CheckpointPositions[lastBonfire].X;
            float bossLine = EngagementLine(layout);
            string tag = Tag(sceneName);

            await ResumeChapterAt(sceneName, lastBonfire);

            PlayerController2D player = RequirePlayer(sceneName);
            TakeTheControls(player);

            float startX = player.GlobalPosition.X;

            // Also the only coverage the multi-bonfire resume has outside chapter two: every chapter now
            // authors three, and waking up at the first one after resting at the third is a lost 300-unit
            // walk that reads as "the save did not take". Tolerance was 2 Unity units.
            Assert.AreEqual(expectedX, startX, World.U(2f),
                $"A slot recording bonfire {lastBonfire} of {sceneName} has to resume there. The player woke up at " +
                $"{World.ToUnits(startX):F1} instead of {World.ToUnits(expectedX):F1} - the recorded index was " +
                "thrown away, and everything below would be measuring the wrong leg of the level.");

            if (layout.HasShortcutGate)
            {
                var gate = SceneQuery.FindFirst<ShortcutGate>();
                Assert.NotNull(gate, $"{sceneName} authors hasShortcutGate, so the arena has to build the door.");
                Assert.IsTrue(gate.IsOpen,
                    "Fixture guard: this is the post-shortcut run-back, so the door has to have built open. Shut, " +
                    "the seconds below are a run into a wall rather than a distance.");
            }

            int onTheWay = CountPlacementsBetween(layout, startX, bossLine);
            var run = new Run();

            await DriveEast(player, () => player.GlobalPosition.X >= bossLine, RunBackBudgetSeconds, run);

            float distance = bossLine - startX;                                  // px
            float speed = distance / Mathf.Max(run.Seconds, 0.001f);             // px/s

            GD.Print($"[QA/{tag}] {sceneName} run-back as authored: {World.ToUnits(distance):F1} units from bonfire " +
                     $"{lastBonfire} ({World.ToUnits(startX):F1}) to the boss line ({World.ToUnits(bossLine):F1}), " +
                     $"{onTheWay} placements on the way, arrived={run.Arrived} in {run.Seconds:F2}s " +
                     $"(avg {World.ToUnits(speed):F2} u/s), maxX={World.ToUnits(run.MaxX):F1}, " +
                     $"hops to get past bodies={run.Jumps}, deepest health dip={run.HealthLost:F0}, " +
                     $"deaths={run.Deaths}" +
                     (run.Deaths > 0 ? " - SECONDS INCLUDE A DEATH AND A WALK BACK, read them with that." : string.Empty));

            // The only thing here that is not a tuning opinion: the road back to the boss has to be
            // walkable. Deaths on the way are allowed - a respawn puts the player at the bonfire this run
            // started from, so the budget is theirs to spend again - but never arriving is a chapter the
            // player cannot get through.
            //
            // This used to read `Arrived || Died`, which was a live assertion only for as long as Died
            // could not be set: the death counter never fired, so arrival was the only way to pass. The
            // moment a working death detector landed, "died once" started passing for "impassable" - and
            // four chapters went green at 100s having never reached the boss.
            Assert.IsTrue(run.Arrived,
                $"{sceneName}'s run-back never reached the boss in {run.Seconds:F0}s. They stopped at " +
                $"{World.ToUnits(run.MaxX):F1} of {World.ToUnits(bossLine):F1} having lost {run.HealthLost:F0} " +
                $"health across {run.Jumps} hops and {run.Deaths} death(s), with {onTheWay} placements on the way.");
        }

        // ------------------------------------------------------------------------------------------
        // Driving
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Holds right until it arrives or the budget runs out, hopping whenever forward progress stops.
        /// One step per physics tick rather than per frame, so the timescale cannot coarsen the decisions -
        /// at six units a second a frame-paced loop under fast-forward would be deciding every half metre.
        /// </summary>
        /// <remarks>
        /// ponytail: the unstick is a stall timer, not a sensor. It cannot tell an enemy from a wall from a
        /// ledge it keeps sliding back down - it jumps whenever forward progress stops, which is what a
        /// player does. Swap it for a forward cast if a level ever needs to know which of those it hit.
        /// </remarks>
        private static async Task DriveEast(
            PlayerController2D player,
            Func<bool> arrived,
            float budgetSeconds,
            Run run)
        {
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
                deathState.OnRespawn += onRespawn;                 // PORT: UnityEvent.AddListener

            float start = GameClock.Time;
            float deadline = start + budgetSeconds;
            run.MaxX = player.GlobalPosition.X;

            float markX = run.MaxX;
            float markTime = start;

            // Unity metres of forward travel, in pixels: hoisted out of the loop so the conversion is
            // named once rather than at every comparison.
            float stallProgress = World.U(StallProgressUnits);

            while (GameClock.Time < deadline)
            {
                // +x is east in both spaces; Vector2.Right needs no flip. Only y would.
                player.SetMoveInput(Vector2.Right);

                float x = player.GlobalPosition.X;
                run.MaxX = Mathf.Max(run.MaxX, x);

                // An enemy body is a wall in this game: chapter two's 2026-08-16 run stopped dead at the
                // grunt on 218 with ninety seconds and no deaths to show for it. A player gets past one by
                // killing it or hopping it, and hopping is the one that keeps this a measurement of travel
                // rather than of combat.
                if (x - markX > stallProgress)
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

        // ------------------------------------------------------------------------------------------
        // Scene and fixture plumbing
        // ------------------------------------------------------------------------------------------

        private static async Task LoadChapter(string sceneName)
        {
            // LoadScene already spends the two frames ChangeSceneToFile needs before the new tree is
            // usable; the extra `yield return null` the Unity helper carried is inside it.
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(sceneName));
        }

        /// <summary>
        /// Enters a chapter the way Continue does: a slot naming it, a recorded bonfire, and the flag the
        /// title menu sets. Nothing else reaches the multi-checkpoint resume path.
        /// </summary>
        private static async Task ResumeChapterAt(string sceneName, int checkpointIndex)
        {
            GameSave.Write(new GameSaveData
            {
                chapterScene = sceneName,
                checkpointIndex = checkpointIndex,

                // Only chapter two has a door; for the rest this field is inert, and it has to be true
                // there because a run-back after a first clear is a run-back through an open shortcut.
                shortcutOpened = true,

                // The three sentinels Apply is documented to leave alone, so this slot restores a place
                // and a door and says nothing about the player's condition. Difficulty comes with the
                // slot's own default, which is Normal.
                health = -1f,
                humanity = -1f,
                souls = -1
            });

            GameSave.LoadOnNextGameplayStart = true;
            await LoadChapter(sceneName);
        }

        private static PlayerController2D RequirePlayer(string sceneName)
        {
            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, $"{sceneName} has to spawn a player.");
            return player;
        }

        /// <summary>
        /// Takes the controls the way the PlayMode agents do. Not optional: the receiver writes the move
        /// input from a dead keyboard every frame, so a driven run with it enabled is a player being told
        /// to stand still between every physics step.
        /// </summary>
        /// <remarks>
        /// PORT: Unity's <c>Behaviour.enabled</c> is <see cref="PlayerInputReceiver.Enabled"/>, which maps
        /// onto whether the node processes - processing is the whole of what the component does.
        /// </remarks>
        private static void TakeTheControls(PlayerController2D player)
        {
            var receiver = player.GetComponentInParent<PlayerInputReceiver>();
            Assert.NotNull(receiver, "The shipped player carries the input receiver these runs have to switch off.");
            receiver.Enabled = false;
        }

        /// <summary>
        /// Every enemy including the boss - <see cref="RainbowChapterBossBehaviour"/> and
        /// <see cref="WrathMiniBoss"/> are both <see cref="EnemyStateMachine"/>s - so a walkability run
        /// ends at the boss line instead of in the fight behind it.
        /// </summary>
        /// <remarks>
        /// PORT: <c>gameObject.SetActive(false)</c> is <c>GameplayBuildShim.SetActive</c>, which hides the
        /// node, stops it processing <i>and</i> disables its descendant collision shapes. All three are
        /// needed - a Godot node that is only hidden still ticks and is still solid, and a solid "disabled"
        /// grunt is exactly the wall this run is measuring the absence of.
        /// </remarks>
        private static int DisableEveryEnemy()
        {
            System.Collections.Generic.List<EnemyStateMachine> machines = SceneQuery.FindAll<EnemyStateMachine>();

            foreach (EnemyStateMachine machine in machines)
                machine.SetActive(false);

            return machines.Count;
        }

        /// <summary>
        /// Where the fight would start, read off the chapter's own encounter file rather than off chapter
        /// one's eight units. Every chapter authors its own reach - nine, ten, eleven - and stopping at a
        /// shared number would drive some runs into the intro and leave others short of it.
        /// </summary>
        /// <remarks>
        /// Returns Godot pixels. <c>BossEncounterData.detectionRange</c> is scaled by <c>World.Ppu</c>
        /// when the file loads, and the spawn position comes out of the layout already converted, so the
        /// only thing needing a wrap here is this file's own one-unit margin.
        /// </remarks>
        private static float EngagementLine(GameplaySceneDefaults layout)
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            BossEncounterData encounter = catalog != null
                ? catalog.ChapterEncounter(layout.BossEncounterFile) ?? catalog.WrathEncounter
                : null;

            Assert.NotNull(encounter,
                "No boss encounter loaded for this chapter, so there is no engagement line to drive to. Either " +
                $"'{layout.BossEncounterFile}' is missing from Resources/Design or the catalog failed to load.");

            return layout.WrathMiniBossSpawnPosition.X - encounter.detectionRange - World.U(EngagementMarginUnits);
        }

        /// <summary>
        /// The log prefix, taken from the chapter's place on the road rather than from its file name, so
        /// the un-numbered first chapter still reads as Ch01 and a renamed scene still logs its gate.
        /// </summary>
        private static string Tag(string sceneName)
        {
            int index = ChapterRoute.IndexOf(sceneName);
            return index >= 0 ? $"Ch{index + 1:D2}" : sceneName;
        }

        private static int CountPlacementsBetween(GameplaySceneDefaults layout, float fromX, float toX)
        {
            return CountIn(layout.MeleeGruntSpawns, fromX, toX)
                   + CountIn(layout.LeapingAttackerSpawns, fromX, toX)
                   + CountIn(layout.RangedCasterSpawns, fromX, toX);
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
    }
}
