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
    /// The death loop: dying costs the ground you had taken, and everything that was pointed at the boss
    /// has to be pointed at the replacement afterwards.
    ///
    /// UNITS: nothing here is a distance. Enemy counts, death counts and damage all cross the port
    /// unscaled, so every number below is the authored Unity number unchanged. The one converted value
    /// is the knockback direction handed to <c>ApplyDamage</c>, and it is horizontal, so the Y flip does
    /// not reach it.
    /// </summary>
    public sealed class GameplayDeathLoopTests
    {
        private const string GameplaySceneName = "GameplayScene";

        // DeathStateController holds the player in spirit form for three seconds before respawning.
        private const float RespawnWait = 4.5f;

        // GameplayEnter is a 1.8s shot; the rest is headroom for a slow headless frame.
        private const float EntryCutsceneWait = 6f;

        private float _startTimeScale;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;

            // PORT CHANGE: MyGame.Core.PlayerPrefs is a real file under user:// and it survives between
            // headless runs, so a save slot left by an earlier fixture would make the bootstrap resume a
            // run instead of starting one. Unity's PlayerPrefs died with the editor session.
            PlayerPrefs.DeleteAll();
            GameSave.LoadOnNextGameplayStart = false;
        }

        [TearDown]
        public void TearDown()
        {
            // Winning the fight freezes the game; TimeScale has to come back or every later fixture runs
            // against a stopped clock.
            GameClock.TimeScale = _startTimeScale;
        }

        [Test]
        public async Task PlayerDeath_PutsKilledEnemiesBack()
        {
            await LoadGameplayScene();

            int startingEnemies = LiveEnemyCount();
            Assert.Greater(startingEnemies, 1, "Gameplay scene should spawn several enemies.");

            var grunt = SceneQuery.FindFirst<MeleeGrunt>();
            Assert.NotNull(grunt, "Gameplay scene should spawn a melee grunt.");

            // Damage is unscaled by the port; the direction is horizontal, so no Y flip either.
            grunt.GetComponent<Health>().ApplyDamage(9999f, Vector2.Right);

            // PORT CHANGE: Unity's `Destroy(go)` landed at the end of the frame, so two `yield return
            // null` were enough. EnemyDeathCleanup awaits a zero-length SceneTreeTimer before it calls
            // QueueFree, which costs one extra frame - polled rather than counted.
            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => LiveEnemyCount() < startingEnemies, 3f),
                "Killing a grunt should take it out of the fight.");

            await KillPlayerAndWaitForRespawn();

            Assert.AreEqual(startingEnemies, LiveEnemyCount(),
                "Respawning should put every enemy back, so the ground has to be retaken.");
        }

        /// <remarks>
        /// PORT CHANGE - Unity drove the win with <c>currentBoss.Defeated.Invoke()</c>. A Unity
        /// <c>UnityEvent</c> is an object any caller can raise; <see cref="IBossEncounter.Defeated"/> is a
        /// C# <c>event</c> here and the language will not let an outsider raise one. So the fight is won
        /// the way the game wins it - the boss is killed and the victory beat is waited out - which pins
        /// the same thing: a hook still bound to the boss the respawn destroyed would never fire.
        /// The same substitution is made in <c>GameplayTimeScaleOwnershipTests</c>.
        /// </remarks>
        [Test]
        public async Task PlayerDeath_KeepsTheVictoryHookOnTheReplacementBoss()
        {
            await LoadGameplayScene();

            var victory = SceneQuery.FindFirst<GameplayVictoryController>();
            Assert.NotNull(victory, "Gameplay scene should own a victory controller.");

            // Through the interface rather than a concrete boss: chapter one moved onto the shared
            // chapter-boss loop on 2026-08-18, and this test is about the respawn rebinding rather than
            // about which of the two classes the arena happens to build.
            IBossEncounter originalBoss = FindBoss();
            Assert.NotNull(originalBoss, "Gameplay scene should spawn the boss.");

            await KillPlayerAndWaitForRespawn();

            // The old boss node is QueueFree'd by the respawn, and a queued free only lands at the end of
            // the frame - so the tree walk below needs a frame to stop finding the corpse first.
            await TestContext.Runner.NextFrame();
            await TestContext.Runner.NextFrame();

            IBossEncounter currentBoss = FindBoss();
            Assert.NotNull(currentBoss, "Respawning should put the boss back.");
            Assert.AreNotSame(originalBoss, currentBoss, "The boss should be rebuilt, not reused.");

            // The controller was listening to a boss that no longer exists. If it was not handed the
            // replacement, beating the fight after one death would do nothing at all.
            Health bossHealth = currentBoss.BossObject.GetComponent<Health>();
            Assert.NotNull(bossHealth, "The boss has to carry Health for the fight to be winnable.");
            bossHealth.ApplyDamage(9999f, Vector2.Right, 0f, true);

            // Defeated fires a beat after the killing blow so the death reads on screen; polled rather
            // than slept because the length of that beat is the boss's business.
            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => victory.HasWon, 12f),
                "The victory hook should follow the respawned boss.");
        }

        /// <summary>
        /// Whichever boss the arena built. Godot's typed lookups cannot take an interface any more than
        /// Unity's could, so this walks the tree - cheap enough once in a fixture, and it is what keeps
        /// this test indifferent to chapter one's boss having moved onto the shared loop.
        /// </summary>
        private static IBossEncounter FindBoss() => SceneQuery.FindFirst<IBossEncounter>();

        private static async Task LoadGameplayScene()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            // Bootstrap plays the entry cutscene on ready, and while it runs the director refuses every
            // other shot - so killing the player here produced a death the death cutscene never staged,
            // which is not the loop this file is meant to pin.
            //
            // Polled on real time: a cutscene runs unscaled, so a frozen scene never advances a scaled wait.
            CutsceneDirector director = CutsceneDirector.Instance;
            if (director == null)
                return;

            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => !director.IsPlaying, EntryCutsceneWait),
                "The entry cutscene has to end on its own after a scene load; nothing else hands input back.");
        }

        private static async Task KillPlayerAndWaitForRespawn()
        {
            var deathController = SceneQuery.FindFirst<DeathStateController>();
            Assert.NotNull(deathController, "Gameplay scene should own a death state controller.");

            int deathsBefore = deathController.DeathCount;

            // PORT CHANGE: in Unity the controller and Health were two components on the player
            // GameObject, so this was GetComponent. They are sibling child nodes of the player body here,
            // which GetComponentInParent is the lookup for.
            deathController.GetComponentInParent<Health>().ApplyDamage(9999f, Vector2.Right);

            await TestContext.Runner.WaitUntil(() => !deathController.IsInSpiritState, RespawnWait);
            await TestContext.Runner.NextFrame();

            Assert.AreEqual(deathsBefore + 1, deathController.DeathCount, "The player should have died once.");
            Assert.IsFalse(deathController.IsInSpiritState, "The player should have left spirit form by now.");
        }

        private static int LiveEnemyCount() => SceneQuery.FindAll<EnemyStateMachine>().Count;
    }
}
