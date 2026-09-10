using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;
using MyGame.UI;

namespace MyGame.Tests
{
    /// <summary>
    /// Covers the three systems added on top of the combat core: poise breaking an action, souls changing
    /// hands and surviving a death, and the pause menu freezing and saving.
    /// </summary>
    /// <remarks>
    /// UNITS: the only converted numbers in this file are the two "step off the stain" offsets. Unity
    /// nudged the player 4 world units sideways; that is <c>World.U(4f)</c> = 400 px here. Souls, health,
    /// humanity, poise and every duration are unscaled by the port and are copied as they stand.
    /// </remarks>
    public sealed class GameplaySoulsPoisePauseTests
    {
        private const string GameplaySceneName = "GameplayScene";

        // DeathStateController holds the player in spirit form for three seconds before respawning.
        private const float RespawnWait = 4.5f;

        // GameplayEnter is a 1.8s shot; the rest is headroom for a slow headless frame.
        private const float EntryCutsceneWait = 6f;

        // Added on top of the authored pickup delay, never used in place of it: enough for a slow
        // headless frame and the physics step the collection lands on.
        private const float PickupHeadroom = 1f;

        // Unity metres -> pixels. How far off the stain the player is parked so the test measures walking
        // back rather than the respawn standing on top of it.
        private static readonly Vector2 StepOffTheStain = new(World.U(4f), 0f);

        private float _startTimeScale;
        private GameSaveData _existingSave;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;

            // These tests write the real save slot, so put whatever was there back afterwards.
            _existingSave = GameSave.Read();
        }

        [TearDown]
        public void TearDown()
        {
            GameClock.TimeScale = _startTimeScale;
            GameSave.LoadOnNextGameplayStart = false;

            if (_existingSave != null)
            {
                GameSave.Write(_existingSave);
            }
            else
            {
                GameSave.Clear();
            }
        }

        [Test]
        public async Task PoiseBreak_StopsTheEnemyMidAction()
        {
            await LoadGameplayScene();

            var grunt = SceneQuery.FindFirst<MeleeGrunt>();
            Assert.NotNull(grunt, "Gameplay scene should spawn a melee grunt.");

            var poise = grunt.GetComponent<Poise>();
            Assert.NotNull(poise, "Enemies should carry a poise gauge.");
            Assert.IsTrue(poise.CanBreak, "The grunt should be staggerable.");
            Assert.IsFalse(grunt.IsStunned, "The grunt should start on its feet.");

            // Unity's `new GameObject("TestAttacker")`. It has to be in the tree: the resolver reads the
            // attacker's groups and wallet, and a never-parented node is in no group.
            var attacker = new Node2D { Name = "TestAttacker" };
            TestContext.Tree.Root.AddChild(attacker);

            // One hit under the poise pool leaves it standing, and well under its health so nothing dies.
            float chip = poise.MaxPoise * 0.5f;
            DamageResult first = CombatResolver.Resolve(Hit(attacker, grunt, chip));
            Assert.IsTrue(first.Applied, "The chip hit should land.");
            Assert.IsFalse(first.Staggered, "Half the poise pool should not break it.");
            Assert.IsFalse(grunt.IsStunned, "The grunt should still be acting after one chip hit.");

            // The second hit empties the gauge.
            DamageResult second = CombatResolver.Resolve(Hit(attacker, grunt, chip + 1f));
            Assert.IsTrue(second.Staggered, "Emptying the poise gauge should report a stagger.");
            Assert.IsTrue(grunt.IsStunned, "A poise break should drop the grunt into its stun state.");

            // Refilled on break, so the next hit in the same combo cannot chain a second stagger.
            Assert.AreEqual(poise.MaxPoise, poise.CurrentPoise, 0.01f,
                "Poise should refill on a break so one fast weapon cannot hold an actor down.");

            attacker.QueueFree();
        }

        [Test]
        public async Task KillingAnEnemy_MovesItsSoulsToTheKiller()
        {
            await LoadGameplayScene();

            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "Gameplay scene should spawn the player.");

            var purse = player.GetComponentInParent<SoulsWallet>();
            Assert.NotNull(purse, "The player should carry a wallet.");
            Assert.AreEqual(0, purse.Souls, "The player should start with nothing banked.");

            var grunt = SceneQuery.FindFirst<MeleeGrunt>();
            var reward = grunt.GetComponent<SoulsWallet>();
            Assert.NotNull(reward, "Enemies should carry the souls they are worth.");
            Assert.Greater(reward.Souls, 0, "The grunt should be worth something.");

            int expected = reward.Souls;
            CombatResolver.Resolve(Hit(player, grunt, 9999f));
            await TestContext.Runner.NextFrame();

            Assert.AreEqual(expected, purse.Souls, "A kill should hand the target's whole balance to the killer.");
            Assert.AreEqual(0, reward.Souls, "The dead enemy should have nothing left to take twice.");
        }

        [Test]
        public async Task PlayerDeath_DropsSoulsAndLetsThemBeRecovered()
        {
            await LoadGameplayScene();

            var player = SceneQuery.FindFirst<PlayerController2D>();
            var purse = player.GetComponentInParent<SoulsWallet>();
            var drop = player.GetComponentInParent<GameplaySoulDrop>();
            Assert.NotNull(drop, "The player should own the soul drop.");

            purse.SetSouls(120);
            await TestContext.Runner.NextFrame();

            await KillPlayerAndWaitForRespawn();

            Assert.AreEqual(0, purse.Souls, "Dying should empty the wallet.");
            Assert.NotNull(drop.ActiveStain, "Dying should leave the souls on the ground.");
            Assert.AreEqual(120, drop.ActiveStain.Souls, "The stain should hold everything that was lost.");

            // Walking back over the stain is the whole point of losing them there. The checkpoint sits
            // where the player spawns, so respawning already drops them on top of it - step off first or
            // this proves nothing about walking back, and the assertion below cannot tell a recovery
            // from a refund.
            SoulPickup stain = drop.ActiveStain;
            Vector2 stainPosition = stain.GlobalPosition;
            float delay = PickupDelay();

            PlaceOn(player, stainPosition + StepOffTheStain);
            await WaitPastPickupDelay(delay);

            Assert.AreEqual(0, purse.Souls,
                "Standing away from the stain should not collect it, even once its grace has passed.");
            Assert.NotNull(drop.ActiveStain, "The stain should wait where it was dropped.");

            PlaceOn(player, stainPosition);
            await WaitForSouls(purse, 120, delay,
                "Touching the stain should give the souls back inside the authored soulStainPickupDelay.");
        }

        [Test]
        public async Task SecondDeath_LosesTheUnrecoveredStain()
        {
            await LoadGameplayScene();

            var player = SceneQuery.FindFirst<PlayerController2D>();
            var purse = player.GetComponentInParent<SoulsWallet>();
            var drop = player.GetComponentInParent<GameplaySoulDrop>();

            purse.SetSouls(90);
            await KillPlayerAndWaitForRespawn();

            SoulPickup firstStain = drop.ActiveStain;
            Assert.NotNull(firstStain, "The first death should leave a stain.");

            // Respawning puts the player back on top of the stain, and the pickup delay is the only thing
            // keeping it there. Step off so this test measures the second death rather than how many
            // frames the harness fitted between the respawn and the next line.
            PlaceOn(player, firstStain.GlobalPosition + StepOffTheStain);
            await WaitPastPickupDelay(PickupDelay());
            Assert.AreEqual(0, purse.Souls, "The stain should still be on the ground, not back in the wallet.");

            // Nothing banked this time, so the second death drops nothing - and the old stain still goes.
            await KillPlayerAndWaitForRespawn();

            Assert.IsNull(firstStain, "Dying again should destroy the stain you had not reached.");
            Assert.IsNull(drop.ActiveStain, "An empty wallet leaves no replacement stain.");
        }

        [Test]
        public async Task Pause_FreezesTheGameAndReleasesIt()
        {
            await LoadGameplayScene();

            var pause = SceneQuery.FindFirst<GameplayPauseController>();
            Assert.NotNull(pause, "Gameplay scene should own a pause controller.");

            var hud = SceneQuery.FindFirst<GameplayHud>();
            var input = SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.IsFalse(pause.IsPaused, "The scene should not start paused.");

            // The pause has to be the only thing holding the controls, or the two assertions below read
            // the entry cutscene's lock and pass whatever the pause menu does.
            Assert.IsTrue(input.Enabled, "Setup: an idle scene leaves the player their controls.");

            pause.SetPaused(true);
            await TestContext.Runner.NextFrame();

            Assert.AreEqual(0f, GameClock.TimeScale, "Pausing should stop time.");
            Assert.IsTrue(hud.IsPauseVisible, "Pausing should put the menu up.");
            Assert.IsFalse(input.Enabled, "Input has to be off, or a held button buffers an action for the unpause.");

            pause.Resume();
            await TestContext.Runner.NextFrame();

            Assert.AreEqual(1f, GameClock.TimeScale, "Resuming should start time again.");
            Assert.IsFalse(hud.IsPauseVisible, "Resuming should take the menu down.");
            Assert.IsTrue(input.Enabled, "Resuming should give control back.");
        }

        [Test]
        public async Task Save_SurvivesReloadingTheScene()
        {
            await LoadGameplayScene();

            var player = SceneQuery.FindFirst<PlayerController2D>();
            player.GetComponentInParent<Health>().SetHealth(42f);
            player.GetComponentInParent<SoulsWallet>().SetSouls(777);
            player.GetComponentInParent<HumanityController>().SetHumanity(64f);

            SceneQuery.FindFirst<GameplayPauseController>().SaveGame();
            Assert.IsTrue(GameSave.Exists, "Saving should leave a slot behind.");

            // Reloading without asking for the save is a fresh run, the way New Game behaves.
            await LoadGameplayScene();
            var fresh = SceneQuery.FindFirst<PlayerController2D>();
            Assert.AreEqual(0, fresh.GetComponentInParent<SoulsWallet>().Souls, "A fresh start should not inherit the save.");

            GameSave.LoadOnNextGameplayStart = true;
            await LoadGameplayScene();

            var restored = SceneQuery.FindFirst<PlayerController2D>();
            Assert.AreEqual(42f, restored.GetComponentInParent<Health>().CurrentHealth, 0.01f, "Health should come back.");
            Assert.AreEqual(777, restored.GetComponentInParent<SoulsWallet>().Souls, "Souls should come back.");
            Assert.AreEqual(64f, restored.GetComponentInParent<HumanityController>().CurrentHumanity, 0.01f,
                "Humanity should come back.");
            Assert.IsFalse(GameSave.LoadOnNextGameplayStart, "The load flag should be consumed, not left armed.");
        }

        private static async Task LoadGameplayScene()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            // Bootstrap plays the entry cutscene on ready, and for its whole length input is off and the
            // director refuses every other shot. Without this wait these tests read the entry shot's state
            // instead of an idle scene - the pause test resumes into a shot that is still holding the
            // controls, and a death here never gets its own cutscene. Same wait, and the same reason, as
            // GameplayCutsceneTests.LoadGameplayScene.
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

        /// <summary>
        /// The stain's grace is authored in PlayerResources.json now, not fixed in code, so no wait here may
        /// assume a number: a fixed 0.3s beat was a silent bet on 0.2, and raising the JSON value past it
        /// turned this file red with a message that blamed the pickup instead of the tuning.
        /// Falls back to the same constant the spawner does when no tuning asset loads.
        /// </summary>
        private static float PickupDelay()
        {
            PlayerResourceData resources = GameplayTuningCatalog.Load()?.PlayerResources;
            float delay = resources != null ? resources.soulStainPickupDelay : GameplayTuningDefaults.SoulStainPickupDelay;

            // Zero would break these tests from the other side: the respawn stands the player on their own
            // stain, so with no grace at all it is reclaimed before the test can step off, and "standing
            // away should not collect it" would fail for a reason that has nothing to do with distance.
            Assert.Greater(delay, 0f,
                "PlayerResources.json soulStainPickupDelay has to stay above zero, or a respawn instantly reclaims the stain it just dropped.");
            return delay;
        }

        private static async Task WaitPastPickupDelay(float delay)
        {
            float deadline = GameClock.Time + delay + PickupHeadroom;
            while (GameClock.Time < deadline)
            {
                await TestContext.Runner.NextFrame();
            }
        }

        /// <summary>
        /// Polls rather than sleeping a beat and asserting once: the recovery cannot land before the
        /// authored delay has passed, and how long after that it lands is a physics step, not a number this
        /// file should hold.
        /// </summary>
        private static async Task WaitForSouls(SoulsWallet purse, int expected, float delay, string message)
        {
            float deadline = GameClock.Time + delay + PickupHeadroom;
            while (purse.Souls != expected && GameClock.Time < deadline)
            {
                await TestContext.Runner.NextFrame();
            }

            Assert.AreEqual(expected, purse.Souls, message);
        }

        /// <summary>
        /// Unity's <c>DamageRequest</c> took two <c>GameObject</c>s; it takes two <see cref="Node"/>s here.
        /// Damage and knockback are unscaled by the port, so the numbers are the Unity ones.
        /// </summary>
        private static DamageRequest Hit(Node2D attacker, Node2D target, float damage)
        {
            return new DamageRequest(attacker, target, target.GlobalPosition, Vector2.Right, damage, 0f, DamageType.Standard);
        }

        /// <summary>
        /// Unity moved <c>player.transform.position</c>. The player root is the motor body here and the
        /// controller is a component node under it, so the move has to go to the body or the collision
        /// shape the stain's trigger sees never leaves the checkpoint.
        /// </summary>
        private static void PlaceOn(PlayerController2D player, Vector2 position)
        {
            var body = player.GetComponentInParent<PlayerMotor2D>();
            Assert.NotNull(body, "The player root is the motor body; without it there is nothing to move.");

            body.ResetMotion();
            body.GlobalPosition = position;
        }

        private static async Task KillPlayerAndWaitForRespawn()
        {
            var deathController = SceneQuery.FindFirst<DeathStateController>();
            Assert.NotNull(deathController, "Gameplay scene should own a death state controller.");

            deathController.GetComponentInParent<Health>().ApplyDamage(9999f, Vector2.Right);

            float deadline = GameClock.Time + RespawnWait;
            while (GameClock.Time < deadline && deathController.IsInSpiritState)
            {
                await TestContext.Runner.NextFrame();
            }

            await TestContext.Runner.NextFrame();
            Assert.IsFalse(deathController.IsInSpiritState, "The player should have left spirit form by now.");
        }
    }
}
