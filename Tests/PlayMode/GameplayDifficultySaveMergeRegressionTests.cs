using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// QA regressions for the 2026-08-10 difficulty / New Game+ / gate-portal pass. Three things that
    /// pass their own tests and are still wrong in the running game: a difficulty that scales the grunts
    /// and not the boss, a save merge that reads the raw progress field the field itself says never to
    /// read raw, and two menus that both own the timescale with nothing arbitrating.
    ///
    /// Each test restores every global it touches - the difficulty statics, the PlayerPrefs slot and the
    /// timescale - because all three are process-wide, the prefs file outlives a headless run, and the
    /// suite stops at the first failure.
    ///
    /// UNITS: only the boss's spawn point is a position, and it is converted at the call. Health, souls,
    /// multipliers and chapter indices are all unscaled.
    /// </summary>
    public sealed class GameplayDifficultySaveMergeRegressionTests
    {
        private Node _boss;
        private Node _player;
        private Node _pause;
        private Node _portal;

        [TearDown]
        public void TearDown()
        {
            // Free rather than QueueFree: SceneQuery walks the live tree, so a queued pause controller
            // would still be found by the next test's portal.
            if (GodotObject.IsInstanceValid(_boss))
                _boss.Free();
            if (GodotObject.IsInstanceValid(_player))
                _player.Free();
            if (GodotObject.IsInstanceValid(_pause))
                _pause.Free();
            if (GodotObject.IsInstanceValid(_portal))
                _portal.Free();

            _boss = null;
            _player = null;
            _pause = null;
            _portal = null;

            // Unconditional: a test that fails halfway through leaves the game frozen and every later
            // test in the run waiting on a clock that never advances.
            GameClock.TimeScale = 1f;
            if (HitStopManager.Instance != null)
                HitStopManager.Instance.Suppress = false;

            // The slot lives in user://playerprefs.cfg and survives the process, so it has to go back the
            // way it was found rather than at the end of the run.
            GameSave.Clear();
            DifficultySettings.Set(Difficulty.Normal, 0);
        }

        /// <summary>
        /// The multiplier used to be applied in <c>GameplayEnemySpawner.ApplyEnemyHealth</c>, which was
        /// called before the behaviour was added - and <c>RainbowChapterBossBehaviour.ApplyDataCore</c>
        /// then called <c>SetMaxHealth(bossData.MaxHealth)</c> with the authored number, putting it
        /// straight back. Seven of the eight bosses are chapter bosses, so Hard and every New Game+ lap
        /// changed the walk to the arena and left the fight the difficulty exists for untouched.
        ///
        /// Silent by construction: the health bar is full either way and the grunts really did get
        /// tougher, so the difficulty looks like it works right up to the boss door.
        /// </summary>
        [Test]
        public async Task ChapterBossHealth_ScalesWithDifficulty_LikeEveryOtherEnemy()
        {
            // Unity ran these synchronously. One frame first here because the first test of a run
            // starts inside the runner's own _Ready, where Godot refuses to parent a fixture node.
            await TestContext.Runner.NextFrame();

            DifficultySettings.Set(Difficulty.Hard, 1);

            float multiplier = DifficultySettings.EnemyHealthMultiplier;
            Assert.Greater(multiplier, 1.01f,
                "The fixture needs a difficulty that actually raises health, or this test proves nothing.");

            RainbowChapterBossData data = BuildBossData();

            // Unity passed the enemy layer as an int; CreateEnemyRoot sets World.Layer.Enemy itself here,
            // so the parameter is gone. The spawn point is the one number that converts: Unity's
            // (60, 1) metres with +Y up is (6000, -100) Godot pixels with +Y down.
            RainbowChapterBossBehaviour behaviour = GameplayEnemySpawner.CreateChapterBoss(
                World.V(new Vector2(60f, 1f)),
                GameplayReadabilityDefaults.Create(),
                data,
                BossEncounterData.Load("Design/WrathEncounter"));

            _boss = behaviour;

            Health health = behaviour.GetComponent<Health>();
            Assert.NotNull(health, "A boss with no health cannot be beaten.");

            Assert.AreEqual(data.MaxHealth * multiplier, health.MaxHealth, 0.01f,
                "The chapter boss was built at the authored health, so the difficulty and the New Game+ lap " +
                "reached the grunts and stopped at the boss. ApplyDataCore is the only writer of this " +
                "number and it is where the multiplier has to be folded in.");
        }

        /// <summary>
        /// <c>GameSaveData.furthestChapter</c> says in its own doc comment: read it through
        /// <see cref="ChapterRoute.FurthestIndex"/>, never directly, because a slot written before the
        /// field existed reads 0. <c>GameplaySaveBridge.Capture</c> read it directly.
        ///
        /// The trip that loses the road: a legacy slot resumes at chapter three, the player walks to the
        /// portal and travels home to chapter one, and <c>GateTravelZone.Travel</c> captures (furthest 0,
        /// copied raw) and then points <c>chapterScene</c> at chapter one. Both pieces of evidence are now
        /// gone, so the portal offers one gate and Continue resumes at the start of the road. No error,
        /// and the slot reads exactly like a fresh save.
        /// </summary>
        [Test]
        public async Task Capture_CarriesTheRoadALegacySlotOnlyProvesWithItsSceneName()
        {
            // Unity ran these synchronously. One frame first here because the first test of a run
            // starts inside the runner's own _Ready, where Godot refuses to parent a fixture node.
            await TestContext.Runner.NextFrame();

            string[] scenes = ChapterRoute.Scenes();
            if (scenes.Length < 3)
                Assert.Ignore("The build carries fewer than three chapters, so there is no road to lose.");

            // A slot written before furthestChapter existed: the field is 0 and the recorded scene is the
            // only evidence of how far the run got.
            GameSave.Write(new GameSaveData { chapterScene = scenes[2], furthestChapter = 0, souls = 5 });

            Assert.AreEqual(2, ChapterRoute.FurthestIndex(GameSave.Read()),
                "Fixture guard: the legacy slot has to read as three gates open before the capture is asked about it.");

            // Node2D rather than a bare Node: the context's actor root is a Node2D here, because half of
            // what reads it wants a position.
            var player = new Node2D { Name = "CaptureRoadTestPlayer" };
            Health health = player.AddComponent<Health>();
            _player = player;
            TestContext.Tree.Root.AddChild(player);

            health.SetMaxHealth(100f);
            health.SetHealth(100f);

            var context = new GameplayPlayerContext(player, null, health, null, null, null, null, null);

            GameSaveData captured = GameplaySaveBridge.Capture(context);

            // Exactly what GateTravelZone.Travel does to the captured record before writing it.
            captured.chapterScene = scenes[0];

            Assert.AreEqual(2, ChapterRoute.FurthestIndex(captured),
                "One trip back through the portal erased the road: Capture copied furthestChapter raw " +
                "instead of through ChapterRoute.FurthestIndex, so the recorded scene was the only thing " +
                "holding the progress up and travelling overwrote it.");
        }

        /// <summary>
        /// Two menus, one timescale, no interlock. <c>GameplayPauseController</c> only stands down for the
        /// victory panel, and Escape is read through <c>GameplayInput</c> rather than through the receiver
        /// the portal disables - so the pause menu opens underneath the travel panel, which is
        /// full-screen, opaque and built after it. Closing either one then wrote a timescale of 1
        /// unconditionally and the game ran behind whichever panel was left up.
        ///
        /// Silent: nothing throws, nothing logs, and the player is taking hits under a menu.
        ///
        /// PORT: <c>Time.timeScale</c> is <see cref="GameClock.TimeScale"/>, and both controllers have to
        /// be in the tree rather than merely constructed - the interlock finds the pause menu with a
        /// whole-tree walk, which is what Unity's <c>FindObjectsByType</c> did.
        /// </summary>
        [Test]
        public async Task ClosingTheGatePanel_DoesNotResumeAGameThePauseMenuFroze()
        {
            // Unity ran these synchronously. One frame first here because the first test of a run
            // starts inside the runner's own _Ready, where Godot refuses to parent a fixture node.
            await TestContext.Runner.NextFrame();

            var pauseHost = new GameplayPauseController { Name = "PauseControllerFixture" };
            _pause = pauseHost;
            TestContext.Tree.Root.AddChild(pauseHost);
            pauseHost.Initialize(null, null, null, default);

            var portal = new GateTravelZone { Name = "GatePortalFixture" };
            _portal = portal;
            TestContext.Tree.Root.AddChild(portal);

            pauseHost.SetPaused(true);
            Assert.AreEqual(0f, GameClock.TimeScale, 0.0001f, "Fixture guard: the pause menu owns the freeze first.");

            // The 닫기 button on the travel panel, which is drawn on top of the pause panel and stays
            // clickable while it is up.
            portal.Close();

            Assert.IsTrue(pauseHost.IsPaused, "Fixture guard: the pause menu is still up.");
            Assert.AreEqual(0f, GameClock.TimeScale, 0.0001f,
                "The gate portal handed play back while the pause menu was still open, so the arena is " +
                "live under a menu. Whoever unfreezes has to check that nothing else is holding the freeze, " +
                "the way GameplayPauseController checks the cutscene input lock.");
        }

        /// <summary>
        /// The same fixture boss <c>GameplayChapterBossTests</c> builds, kept local so retuning a shipped
        /// chapter cannot turn this red.
        ///
        /// UNITS: the JSON is the authored Unity number, so it goes through the same <c>OnValidate</c> +
        /// <c>ScaleToPixels</c> pair <see cref="RainbowChapterBossData.Load"/> runs - moveSpeed 2 becomes
        /// 200 px/s, attackRange 1.5 becomes 150 px. maxHealth, poise, damage and the soul reward are all
        /// counts and cross unchanged, which is why the guard below still reads 200.
        /// </summary>
        private static RainbowChapterBossData BuildBossData()
        {
            RainbowChapterBossData data = JsonData.FromJson<RainbowChapterBossData>(
                "{\"maxHealth\":200,\"moveSpeed\":2,\"detectionRange\":8,\"attackRange\":1.5," +
                "\"phaseTwoHealthThreshold\":0.5,\"phaseTwoSpeedMultiplier\":1.2,\"phaseTwoCooldownMultiplier\":0.75," +
                "\"maxPoise\":90,\"poiseHeavyMultiplier\":2,\"poiseRegenDelay\":3,\"poiseRegenRate\":30,\"soulReward\":275," +
                // Stun, poise, body and pulse were class defaults until K3; a boss with a zero body or a
                // zero stun is not the one these tests were written against, so the fixture says them.
                "\"stunDuration\":1,\"bodySize\":{\"x\":1.6,\"y\":2.3},\"telegraphPulseSpeed\":8,\"telegraphPulseAmplitude\":0.2," +
                "\"bossName\":\"DifficultyFixtureBoss\",\"chapterName\":\"Test Chapter\"," +
                "\"attacks\":[{\"attackId\":\"test_swing\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":12,\"knockback\":0," +
                "\"telegraphTime\":0.25,\"activeTime\":0.25,\"recoveryTime\":0.25,\"range\":1.5,\"forwardOffset\":0.5," +
                "\"phaseTwoWeight\":1}]}");

            Assert.NotNull(data, "The fixture JSON should bind onto RainbowChapterBossData.");

            data.OnValidate();
            data.ScaleToPixels();

            Assert.AreEqual(200f, data.MaxHealth, 0.01f, "The fixture boss should carry its authored health.");
            return data;
        }
    }
}
