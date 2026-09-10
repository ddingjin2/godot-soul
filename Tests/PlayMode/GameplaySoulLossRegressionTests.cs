using System.Reflection;
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
    /// Guards two ways the soul currency can silently vanish: a fatal hit landing during spirit form
    /// feeding the player's wallet to the killer through the kill-reward path, and a corrupt save slot
    /// throwing out of <see cref="GameSave.Read"/> in the middle of scene bootstrap.
    /// </summary>
    /// <remarks>
    /// UNITS: nothing in this file is a distance. Souls, health and the pickup delay are unscaled by the
    /// port, so every number is the Unity one. The only converted value is the hit direction, which is
    /// <see cref="Vector2.Right"/> in both engines.
    /// </remarks>
    public sealed class GameplaySoulLossRegressionTests
    {
        private const string GameplaySceneName = "GameplayScene";

        // Mirrors GameSave's private PlayerPrefs key; there is no API for planting a broken payload.
        private const string SaveKey = "MyGame.Save";

        // GameplayEnter is a 1.8s shot; the rest is headroom for a slow headless frame.
        private const float EntryCutsceneWait = 6f;

        private float _startTimeScale;
        private GameSaveData _existingSave;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;
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
        public async Task FatalHitInSpiritForm_DoesNotHandTheWalletToTheKiller()
        {
            await LoadGameplayScene();

            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "Gameplay scene should spawn the player.");
            var purse = player.GetComponentInParent<SoulsWallet>();
            var death = player.GetComponentInParent<DeathStateController>();
            var drop = player.GetComponentInParent<GameplaySoulDrop>();

            var grunt = SceneQuery.FindFirst<MeleeGrunt>();
            Assert.NotNull(grunt, "Gameplay scene should spawn a melee grunt.");
            var gruntWallet = grunt.GetComponent<SoulsWallet>();
            int gruntReward = gruntWallet.Souls;

            // First death puts the player into spirit form. Spirit form restores a sliver of health,
            // so a second fatal hit can land before the respawn.
            player.GetComponentInParent<Health>().ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();
            Assert.IsTrue(death.IsInSpiritState, "A death should enter spirit form.");

            // The souls the player picked back up on the walk back, still in spirit form.
            purse.SetSouls(50);

            // The killing blow lands through the resolver, the way a real enemy hit does.
            // DamageRequest takes Godot Nodes now; the damage number itself is unscaled by the port.
            CombatResolver.Resolve(new DamageRequest(
                grunt, player, player.GlobalPosition, Vector2.Right, 9999f, 0f, DamageType.Standard));
            await TestContext.Runner.NextFrame();

            Assert.AreEqual(gruntReward, gruntWallet.Souls,
                "A fatal hit during spirit form must not move the player's souls into the enemy's wallet.");

            int onGround = drop != null && drop.ActiveStain != null ? drop.ActiveStain.Souls : 0;
            Assert.AreEqual(50, purse.Souls + onGround,
                "Souls held when spirit form ends fatally must stay banked or on the ground, not vanish.");
        }

        /// <summary>
        /// The case the test above does not reach: the player already had a stain on the ground when the
        /// second fatal hit landed. Raising OnDeath for a spirit-form death routes it into
        /// GameplaySoulDrop.DropSouls, which frees the current stain before checking whether it has
        /// anything to replace it with - so an empty wallet takes the untouched stain down with it.
        /// GameplayFallDeath makes this automatic rather than incidental: it re-arms after its one second
        /// lockout while the player is still falling past the death plane, so every pit death produces a
        /// second death inside the same three seconds of spirit form.
        /// </summary>
        [Test]
        public async Task SecondDeathDuringSpiritForm_KeepsTheStainTheFirstDeathLeft()
        {
            await LoadGameplayScene();

            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "Gameplay scene should spawn the player.");
            var purse = player.GetComponentInParent<SoulsWallet>();
            var death = player.GetComponentInParent<DeathStateController>();
            var drop = player.GetComponentInParent<GameplaySoulDrop>();
            var health = player.GetComponentInParent<Health>();

            purse.SetSouls(80);
            await TestContext.Runner.NextFrame();

            health.ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();

            Assert.IsTrue(death.IsInSpiritState, "A death should enter spirit form.");
            SoulPickup stain = drop.ActiveStain;
            Assert.NotNull(stain, "The first death should leave a stain.");
            Assert.AreEqual(80, stain.Souls, "The stain should hold what the wallet held.");

            // Spirit form restores a sliver of health and grants no invulnerability, so a second fatal
            // hit lands seconds before the player is given control back.
            health.ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();

            // Assert.NotNull reads a freed Godot object as null, which is what Unity's overloaded == did.
            Assert.NotNull(stain,
                "A death the player was never given a chance to avoid must not destroy the stain they have not reached.");

            int onGround = drop.ActiveStain != null ? drop.ActiveStain.Souls : 0;
            Assert.AreEqual(80, purse.Souls + onGround, "The souls should still be banked or on the ground.");
        }

        /// <summary>
        /// The pickup grace moved out of <see cref="GameplayTuningDefaults"/> and into
        /// PlayerResources.json, and <c>GameplayPlayerSpawner</c> is the only thing carrying it to the
        /// stain. Drop that argument and the stain quietly arms on the fallback constant instead: the
        /// game still runs, the designer's number just stops existing. Nothing else would notice.
        /// </summary>
        [Test]
        public async Task DroppedStain_ArmsOnTheAuthoredPickupDelay()
        {
            await LoadGameplayScene();

            PlayerResourceData resources = GameplayTuningCatalog.Load()?.PlayerResources;
            Assert.NotNull(resources, "PlayerResources.json should load; the delay under test is authored there.");

            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "Gameplay scene should spawn the player.");
            var drop = player.GetComponentInParent<GameplaySoulDrop>();

            player.GetComponentInParent<SoulsWallet>().SetSouls(30);
            await TestContext.Runner.NextFrame();

            player.GetComponentInParent<Health>().ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();

            SoulPickup stain = drop.ActiveStain;
            Assert.NotNull(stain, "Dying with souls banked should leave a stain.");

            // No accessor for the delay, so it is read by field name the way the P0 stability runner reads
            // private members - a rename breaks this test rather than the build.
            FieldInfo delayField = typeof(SoulPickup).GetField("_pickupDelay", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(delayField, "SoulPickup should keep its _pickupDelay field.");

            // A time, not a distance - unscaled by the port.
            Assert.AreEqual(resources.soulStainPickupDelay, (float)delayField.GetValue(stain), 0.0001f,
                "The stain should arm on the authored delay, not on the GameplayTuningDefaults fallback.");
        }

        [Test]
        public void CorruptSaveSlot_ReadsAsNoSaveInsteadOfThrowing()
        {
            PlayerPrefs.SetString(SaveKey, "{this is not json");

            GameSaveData data = null;
            Assert.DoesNotThrow(() => data = GameSave.Read(),
                "A corrupt slot must not throw out of GameSave.Read - Continue runs it during scene bootstrap.");

            // The JSON reader is free to either throw on a malformed payload (caught, read as no slot) or
            // leave the object at its defaults. Both are acceptable; what the bootstrap cannot survive is
            // a throw, and what it must not act on is restorable state. GameplaySaveBridge.Apply skips
            // health <= 0 and humanity < 0, so a defaults-only record restores nothing.
            Assert.IsTrue(
                data == null || (data.health <= 0f && data.humanity < 0f && data.souls <= 0),
                "A corrupt slot must not hand the bootstrap any state to restore.");
        }

        private static async Task LoadGameplayScene()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            // Bootstrap plays the entry cutscene on ready, and while it runs the director refuses every
            // other shot - so the deaths below landed with the death cutscene never staged, which is not
            // the path the souls have to survive. Same wait as GameplayCutsceneTests.LoadGameplayScene.
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
