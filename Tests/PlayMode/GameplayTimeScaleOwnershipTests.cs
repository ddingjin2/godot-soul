using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// Three systems write <see cref="GameClock.TimeScale"/>: hit stop, the pause menu and the victory
    /// freeze. Hit stop used to write it every frame, so a pause or a win was stomped back to 1 the frame
    /// after it was set. These guard the handover in both directions - a freeze taking the timescale away
    /// from a running hit stop, and hit stop getting it back once the freeze releases.
    /// </summary>
    /// <remarks>
    /// PORT NOTE - the physics-step half of every assertion below is inverted on purpose.
    /// Unity's <c>HitStopManager</c> wrote <c>Time.fixedDeltaTime</c> alongside <c>Time.timeScale</c>,
    /// because Unity's fixed step is independent of the timescale. Godot's <c>Engine.TimeScale</c>
    /// already scales physics, so the ported manager writes nothing to the step - writing it too would
    /// apply the slowdown twice. So where the Unity fixture asserted "hit stop shortened the step" and
    /// "the freeze put the step back", this one asserts the step never moves at all: it is
    /// <c>1 / Engine.PhysicsTicksPerSecond</c>, a project setting, and no system here may touch it.
    /// That is the ported ownership, and it is what these tests hold.
    /// </remarks>
    public sealed class GameplayTimeScaleOwnershipTests
    {
        private const string GameplaySceneName = "GameplayScene";

        // project.godot sets physics_ticks_per_second = 50, so the step is 0.02 - the same number Unity
        // used. It is a constant here rather than something hit stop restores; see the class remarks.
        private const float DefaultFixedDeltaTime = 0.02f;
        private const float FixedDeltaTolerance = 0.002f;

        // GameplayEnter is a 1.8s shot; the rest is headroom for a slow headless frame.
        private const float EntryCutsceneWait = 6f;

        private float _startTimeScale;
        private bool _startHitStopOption;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;

            // GraphicsOptions.HitStop lives in PlayerPrefs and TitleGraphicsOptionsTests leaves it off after
            // clicking the Low preset, so every test here has to arm it rather than assume the default.
            _startHitStopOption = GraphicsOptions.HitStop;
            GraphicsOptions.SetHitStop(true);
        }

        [TearDown]
        public void TearDown()
        {
            GameClock.TimeScale = _startTimeScale;
            GraphicsOptions.SetHitStop(_startHitStopOption);

            // Unity also put Time.fixedDeltaTime back here. Godot's physics step is a project setting with
            // no setter on GameClock, and nothing in the port writes it, so there is nothing to restore.
        }

        /// <summary>
        /// The plain episode: hit stop owns the timescale while it runs and puts time back when it ends.
        /// Restoring happens once, on the frame the episode ends, so losing that branch would leave the
        /// game running at 5% speed forever.
        /// </summary>
        [Test]
        public async Task HitStop_SlowsTimeAndPutsItBackWhenTheEpisodeEnds()
        {
            await LoadGameplayScene();

            HitStopManager hitStop = FindHitStop();
            hitStop.TriggerHitStop(0.25f);
            await TestContext.Runner.NextFrame();

            Assert.Less(GameClock.TimeScale, 1f, "A hit stop should slow time down.");

            // PORT CHANGE: Unity asserted the step had *shortened*. Engine.TimeScale scales physics for
            // free here, so the step must NOT move - see the class remarks.
            Assert.AreEqual(DefaultFixedDeltaTime, GameClock.FixedDeltaTime, FixedDeltaTolerance,
                "A hit stop must leave the physics step alone; Engine.TimeScale already scales physics.");

            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => Mathf.IsEqualApprox(GameClock.TimeScale, 1f), 2f),
                "Hit stop should hand time back when its episode ends.");

            Assert.AreEqual(DefaultFixedDeltaTime, GameClock.FixedDeltaTime, FixedDeltaTolerance,
                "Ending a hit stop should not have moved the physics step either.");
        }

        /// <summary>
        /// The bug this fixture exists for: pausing while a hit stop is mid-episode. The freeze has to hold
        /// for more than the frame it was set on.
        /// </summary>
        [Test]
        public async Task Pause_HoldsTimeAtZeroWhileAHitStopIsStillRunning()
        {
            await LoadGameplayScene();

            HitStopManager hitStop = FindHitStop();
            var pause = SceneQuery.FindFirst<GameplayPauseController>();
            Assert.NotNull(pause, "Gameplay scene should own a pause controller.");

            hitStop.TriggerHitStop(0.5f);
            await TestContext.Runner.NextFrame();
            Assert.Less(GameClock.TimeScale, 1f, "Setup: the hit stop should be running before the pause.");

            pause.SetPaused(true);
            Assert.AreEqual(0f, GameClock.TimeScale, "Pausing should stop time.");

            // PORT CHANGE: Unity asserted the freeze *put the step back* from hit stop's shortened one.
            // Nothing shortened it here, so the assertion is that it still has not moved.
            Assert.AreEqual(DefaultFixedDeltaTime, GameClock.FixedDeltaTime, FixedDeltaTolerance,
                "Taking the freeze over must not move the physics step.");

            // The old failure took one frame to appear: hit stop's tick wrote the timescale back.
            for (int frame = 0; frame < 3; frame++)
            {
                await TestContext.Runner.NextFrame();
            }

            Assert.AreEqual(0f, GameClock.TimeScale, "A hit stop running when the pause began must not thaw it.");

            pause.Resume();
            await TestContext.Runner.NextFrame();
            Assert.AreEqual(1f, GameClock.TimeScale, "Resuming should start time again.");

            // Suppression is per-instance and has to be released with the pause, or hit stop is dead for the
            // rest of the run with nothing reporting it.
            hitStop.TriggerHitStop(0.25f);
            await TestContext.Runner.NextFrame();
            Assert.Less(GameClock.TimeScale, 1f, "Hit stop should work again once the pause releases it.");

            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => Mathf.IsEqualApprox(GameClock.TimeScale, 1f), 2f),
                "The hit stop taken after the pause should still end on its own.");
        }

        /// <summary>
        /// The overlap nothing covered: a hit stop and the victory freeze wanting the timescale at the same
        /// time.
        /// </summary>
        /// <remarks>
        /// PORT CHANGE - Unity drove this by calling <c>boss.Defeated.Invoke()</c>. A Unity
        /// <c>UnityEvent</c> is an object anyone can raise; <see cref="IBossEncounter.Defeated"/> is a C#
        /// <c>event</c> here, and the language will not let a caller outside the boss raise one. So the
        /// fight is won the way the game wins it - the boss is killed and the 1.5s beat before
        /// <c>Defeated</c> is waited out on real time - and the overlap is then driven from the other side:
        /// a hit stop asked for while the freeze is up must not thaw it. That is the same ownership rule
        /// (<c>HitStopManager.Suppress</c>) the Unity version was reaching for, asserted on the side of the
        /// handover that a test can actually stage.
        /// </remarks>
        [Test]
        public async Task VictoryFreeze_IsNotThawedByAHitStopRunningAtTheSameTime()
        {
            await LoadGameplayScene();

            HitStopManager hitStop = FindHitStop();

            // Through the interface: chapter one moved onto the shared chapter-boss loop on 2026-08-18,
            // and what this test is about is who owns the timescale when the fight is won.
            IBossEncounter boss = FindBoss();
            Assert.NotNull(boss, "Gameplay scene should spawn the boss.");
            var victory = SceneQuery.FindFirst<GameplayVictoryController>();
            Assert.NotNull(victory, "Gameplay scene should own a victory controller.");

            var bossHealth = boss.BossObject.GetComponent<Health>();
            Assert.NotNull(bossHealth, "The boss has to carry Health for the fight to be winnable.");

            // Damage is not scaled by the port - only distances and speeds are.
            bossHealth.ApplyDamage(9999f, Vector2.Right, 0f, true);

            // Defeated is raised a beat after the killing blow, on scaled time; polled on real time because
            // the freeze this test is about drives the scaled clock to zero.
            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => victory.HasWon, 12f),
                "The victory hook should fire once the boss is down.");

            Assert.IsTrue(victory.HasWon, "The victory hook should fire.");
            Assert.AreEqual(0f, GameClock.TimeScale, "Winning should freeze the game.");

            // PORT CHANGE: Unity asserted the freeze did not inherit hit stop's shortened step. Nothing
            // shortens it here, so the assertion is that the freeze left it alone.
            Assert.AreEqual(DefaultFixedDeltaTime, GameClock.FixedDeltaTime, FixedDeltaTolerance,
                "The victory freeze must not move the physics step.");

            // The hit a player lands inside one hit-stop window of the win. Suppress is what makes this a
            // no-op; without it the timescale is back to 0.05 on the next tick and the freeze is gone.
            hitStop.TriggerHitStop(0.5f);

            for (int frame = 0; frame < 3; frame++)
            {
                await TestContext.Runner.NextFrame();
            }

            Assert.AreEqual(0f, GameClock.TimeScale,
                "A hit stop asked for at the win must not thaw the victory freeze a frame later.");
        }

        /// <summary>
        /// Quitting a paused scene. The zero timescale must not follow the reload, and hit stop must not
        /// come back suppressed - which is what a static suppression flag would do.
        /// </summary>
        [Test]
        public async Task ReloadingWhilePaused_LeavesTheNextRunPlayable()
        {
            await LoadGameplayScene();

            var pause = SceneQuery.FindFirst<GameplayPauseController>();
            Assert.NotNull(pause, "Gameplay scene should own a pause controller.");

            pause.SetPaused(true);
            await TestContext.Runner.NextFrame();
            Assert.AreEqual(0f, GameClock.TimeScale, "Setup: the scene should be paused before the reload.");

            await LoadGameplayScene();

            Assert.AreEqual(1f, GameClock.TimeScale,
                "Leaving a paused scene must not carry a zero timescale into the next one.");

            HitStopManager hitStop = FindHitStop();
            hitStop.TriggerHitStop(0.25f);
            await TestContext.Runner.NextFrame();
            Assert.Less(GameClock.TimeScale, 1f, "Hit stop must not stay suppressed across a scene reload.");

            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => Mathf.IsEqualApprox(GameClock.TimeScale, 1f), 2f),
                "The reloaded run's hit stop should still end on its own.");
        }

        /// <summary>
        /// Whichever boss the arena built. Godot's typed lookups cannot take an interface either, so this
        /// walks the tree - cheap enough once in a fixture, and it is what keeps this test indifferent to
        /// which of the two boss classes chapter one is on.
        /// </summary>
        private static IBossEncounter FindBoss() => SceneQuery.FindFirst<IBossEncounter>();

        private static HitStopManager FindHitStop()
        {
            HitStopManager hitStop = HitStopManager.Instance ?? SceneQuery.FindFirst<HitStopManager>();
            Assert.NotNull(hitStop, "Gameplay bootstrap should build a hit stop manager.");
            return hitStop;
        }

        private static async Task LoadGameplayScene()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            // Bootstrap plays the entry cutscene on ready and it holds the player's controls for its whole
            // length. Nothing here asserts input, but pausing on top of a running shot is the cutscene
            // file's case, not this one's: these tests own the timescale handover between hit stop, the
            // pause menu and the victory freeze, and they should measure it on an idle scene.
            //
            // Polled on real time: a cutscene runs unscaled, so a frozen scene never advances a scaled wait.
            CutsceneDirector director = CutsceneDirector.Instance;
            if (director == null)
            {
                return;
            }

            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => !director.IsPlaying, EntryCutsceneWait),
                "The entry cutscene has to end on its own after a scene load; nothing else hands input back.");
        }
    }
}
