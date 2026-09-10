using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Gameplay;
using MyGame.Player;
using MyGame.UI;

namespace MyGame.Tests
{
    /// <summary>
    /// Covers the charge-limited heal item. Only the parts that decide whether drinking is a risk:
    /// the charge is spent up front so an interrupted drink costs something, both refill paths hand the
    /// charges back, and the death loop cannot be healed out of.
    ///
    /// The wind-up cannot finish in a single frame - the wind-up is 0.9s and Godot's
    /// <c>max_physics_steps_per_frame</c> plus the frame cap keep a delta well under that - so a test
    /// that catches the drink in progress is not racing the frame rate. It is racing the arena instead:
    /// nothing here isolates the player from the enemies, the same bet every other scene-loading test in
    /// this folder already makes.
    ///
    /// UNITS: nothing in this file is a distance. Heal charges, health, damage, seconds and frame counts
    /// all cross the port unscaled, so every number below is the authored Unity number unchanged. The one
    /// converted expectation is the checkpoint zone's position, which is taken from the player body
    /// rather than written down.
    /// </summary>
    public sealed class GameplayHealItemTests
    {
        private const string GameplaySceneName = "GameplayScene";

        // GameplayEnter is a 1.8s shot; the rest is headroom for a slow headless frame.
        private const float EntryCutsceneWait = 6f;

        // DeathStateController holds the player in spirit form for three seconds before respawning.
        private const float RespawnWait = 4.5f;

        // Comfortably past a 0.9 wind-up without hanging the suite if the drink never lands.
        private const float DrinkTimeout = 3f;

        // Well inside the wind-up, so "the drink started" cannot be confused with "the drink finished".
        private const float DrinkStartTimeout = 0.3f;

        private float _startTimeScale;
        private CheckpointZone _zone;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;

            // PORT CHANGE: Unity snapshotted the save slot here and put it back afterwards, because
            // PlayerPrefs was editor-session state. MyGame.Core.PlayerPrefs is a file under user:// that
            // outlives the run, so the slot is wiped instead - a leftover one would have the bootstrap
            // resume a run rather than start one.
            PlayerPrefs.DeleteAll();
        }

        [TearDown]
        public void TearDown()
        {
            // Heal_IsFrozenByPause leaves the timescale at zero if it fails mid-pause; without this every
            // later fixture runs against a stopped clock.
            GameClock.TimeScale = _startTimeScale;
            GameSave.LoadOnNextGameplayStart = false;
            GameSave.Clear();

            if (GodotObject.IsInstanceValid(_zone))
                _zone.QueueFree();

            _zone = null;
        }

        /// <summary>
        /// The whole reason the item has a wind-up. If a hit cancelled the drink but refunded the charge,
        /// mistiming it would cost time only, and drinking mid-fight would stop being a decision.
        /// </summary>
        [Test]
        public async Task Heal_InterruptedByAHit_RestoresNothingAndStillSpendsTheCharge()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            var health = player.GetComponentInParent<Health>();
            Assert.NotNull(health, "The player should carry a health component.");

            health.SetHealth(health.MaxHealth * 0.3f);

            int chargesBefore = player.HealCharges;
            Assert.Greater(chargesBefore, 0, "A freshly spawned player should have drinks in hand.");

            await StartDrink(player);

            Assert.AreEqual(chargesBefore - 1, player.HealCharges,
                "The charge is spent when the drink starts, not when it lands.");

            float healthBefore = health.CurrentHealth;

            // No knockback: an impulse would take actions.Tick out of the loop and hide whether the
            // cancel itself worked. Damage is unscaled and the direction is horizontal, so neither is
            // converted.
            health.ApplyDamage(5f, Vector2.Right);

            await TestContext.Runner.NextFrame();
            Assert.IsFalse(player.IsHealing, "A hit has to break the drink.");

            await TestContext.Runner.Seconds(1.5f);

            Assert.AreEqual(healthBefore - 5f, health.CurrentHealth, 0.01f,
                "An interrupted drink must restore nothing, not land late once the wind-up would have ended.");
            Assert.AreEqual(chargesBefore - 1, player.HealCharges,
                "An interrupted drink still costs the charge; refunding it removes the risk the wind-up exists to create.");
        }

        /// <summary>
        /// Two different code paths reach the refill - <see cref="CheckpointZone.Activate"/> and
        /// <see cref="PlayerController2D.ResetForRespawn"/> - so both are pinned here. A rest that hands
        /// health back but not drinks leaves the player to walk into the boss dry.
        /// </summary>
        [Test]
        public async Task RestAndRespawn_BothRefillHealCharges()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            int max = player.MaxHealCharges;
            Assert.Greater(max, 0, "The player should spawn with a non-zero charge count from PlayerResources.");

            await PlaceZoneOnPlayer(player);

            await DrinkToCompletion(player);
            Assert.AreEqual(max - 1, player.HealCharges, "Finishing a drink should leave one fewer charge.");

            _zone.TryActivate();
            Assert.AreEqual(max, player.HealCharges, "A checkpoint rest should hand the drinks back.");

            await DrinkToCompletion(player);
            Assert.AreEqual(max - 1, player.HealCharges, "Finishing a drink should leave one fewer charge.");

            await KillPlayerAndWaitForRespawn();

            Assert.AreEqual(max, player.HealCharges, "A respawn should hand the drinks back too.");
        }

        /// <summary>
        /// Spirit form is the window where the run is already lost and the player is waiting it out.
        /// Healing out of it would end the death loop before the respawn it exists to reach.
        /// </summary>
        [Test]
        public async Task Heal_IsRefusedInSpiritForm()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            var health = player.GetComponentInParent<Health>();
            var death = player.GetComponentInParent<DeathStateController>();
            Assert.NotNull(death, "The player should carry a death state controller.");

            health.ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();

            Assert.IsTrue(death.IsInSpiritState, "A lethal hit should put the player in spirit form.");

            int chargesBefore = player.HealCharges;
            float healthBefore = health.CurrentHealth;

            player.RequestHeal();
            await TestContext.Runner.Seconds(1f);

            Assert.IsFalse(player.IsHealing, "A drink must not start in spirit form.");
            Assert.AreEqual(chargesBefore, player.HealCharges, "A refused drink must not cost a charge.");
            Assert.AreEqual(healthBefore, health.CurrentHealth, 0.01f,
                "Healing in spirit form would end the death sequence the respawn is waiting on.");

            // Leave spirit form behind so the scene is not torn down mid-death sequence.
            await WaitForRespawn(death);
        }

        /// <summary>
        /// A press refused for want of a charge must expire rather than sit in the buffer until a rest
        /// refills and it drinks itself - at the one place the player is standing still and already at
        /// full health.
        /// </summary>
        /// <remarks>
        /// PORT NOTE - this was marked EXPECTED RED in the Unity source, against a
        /// <c>DecayInputBuffers</c> that aged four buffers and not <c>_healBuffer</c>. The ported
        /// <see cref="PlayerActionController"/> ages it (Scripts/Player/PlayerActionController.cs:722)
        /// and <c>TryStartHeal</c> also drops the buffer outright at zero charges, so the test lands
        /// green here. Kept unchanged: it is now the regression that stops the fix being undone.
        /// </remarks>
        [Test]
        public async Task Heal_RefusedAtZeroCharges_DoesNotFireWhenTheRestRefills()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            int max = player.MaxHealCharges;

            await PlaceZoneOnPlayer(player);

            for (int i = 0; i < max; i++)
                await DrinkToCompletion(player);

            Assert.AreEqual(0, player.HealCharges, "Drinking every charge should empty the flask.");

            player.RequestHeal();
            await TestContext.Runner.NextFrame();
            await TestContext.Runner.NextFrame();

            Assert.IsFalse(player.IsHealing, "A press with no charge left must not start a wind-up.");

            _zone.TryActivate();
            Assert.AreEqual(max, player.HealCharges, "A checkpoint rest should hand the drinks back.");

            await TestContext.Runner.Seconds(0.5f);

            Assert.IsFalse(player.IsHealing,
                "A press refused for want of a charge must expire, not sit in the buffer and drink itself the moment a rest refills.");
            Assert.AreEqual(max, player.HealCharges,
                "The refused press must not spend one of the charges the rest just handed back.");
        }

        /// <summary>
        /// The flask readout is polled in the HUD's _Process rather than pushed by an event, so a lost
        /// player binding shows up as a line that never moves - which reads as "no charges were spent",
        /// the one thing the player has to be able to trust before walking into the boss.
        /// </summary>
        [Test]
        public async Task FlaskReadout_FollowsTheChargeItSpends()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            Label flask = FindFlaskText();

            Assert.Greater(player.MaxHealCharges, 0, "The readout under test only exists when the player carries charges.");
            Assert.AreEqual($"Flask: {player.HealCharges}/{player.MaxHealCharges}", flask.Text,
                "The readout should show the charges the player spawned with.");

            int before = player.HealCharges;
            await StartDrink(player);

            // Two frames: the HUD's _Process may already have run on the frame the charge was spent.
            await TestContext.Runner.NextFrame();
            await TestContext.Runner.NextFrame();

            Assert.AreEqual(before - 1, player.HealCharges, "Starting a drink spends the charge up front.");
            Assert.AreEqual($"Flask: {player.HealCharges}/{player.MaxHealCharges}", flask.Text,
                "A polled readout has to follow the charge it just spent.");
        }

        /// <summary>
        /// Wrath's ban on healing has to reach a drink that is already in the air, not only refuse the
        /// next press. <see cref="PlayerController2D.ApplySinModifier"/> calls
        /// <see cref="PlayerActionController.CancelHeal"/> for exactly this, and nothing else would
        /// catch it going away: a sin activated one frame into the wind-up would otherwise let the heal
        /// land, which is the one thing Wrath is defined as forbidding.
        /// </summary>
        [Test]
        public async Task Heal_CancelledByWrath_SpendsTheChargeAndRestoresNothing()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            var health = player.GetComponentInParent<Health>();
            var sin = player.GetComponentInParent<SinResonanceController>();
            Assert.NotNull(sin, "The player should carry a sin resonance controller.");

            health.SetHealth(health.MaxHealth * 0.3f);

            int chargesBefore = player.HealCharges;
            await StartDrink(player);

            float healthBefore = health.CurrentHealth;

            // Resonance and humanity are both gates on activation; the test is about the cancel, not
            // about how long it takes to earn the sin.
            sin.AddResonance(9999f);
            sin.RequestActivateSin(SinState.Wrath);
            await TestContext.Runner.NextFrame();

            Assert.AreEqual(SinState.Wrath, sin.CurrentSin, "The sin should have activated with resonance in hand.");
            Assert.IsFalse(player.IsHealing, "Wrath has to break a drink that is already in progress.");
            Assert.IsFalse(player.CanHeal, "Wrath should also refuse the next press while it lasts.");

            await TestContext.Runner.Seconds(1.5f);

            Assert.AreEqual(healthBefore, health.CurrentHealth, 0.01f,
                "A drink Wrath cancelled must not land late once the wind-up would have ended.");
            Assert.AreEqual(chargesBefore - 1, player.HealCharges,
                "The charge is spent at the start, so cancelling by sin costs it the same as cancelling by hit.");
        }

        /// <summary>
        /// The wind-up runs on scaled time, so a pause has to stop it. If it ran unscaled, opening the
        /// menu mid-drink would be a free heal - the one input the player can always reach, turning the
        /// item's only cost into nothing.
        /// </summary>
        [Test]
        public async Task Heal_IsFrozenByPause()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            var health = player.GetComponentInParent<Health>();
            var pause = SceneQuery.FindFirst<GameplayPauseController>();
            Assert.NotNull(pause, "Gameplay scene should own a pause controller.");

            health.SetHealth(health.MaxHealth * 0.3f);
            float healthBefore = health.CurrentHealth;

            await StartDrink(player);

            pause.SetPaused(true);

            // Real time, not scaled: a paused game holds GameClock.TimeScale at zero, so a scaled wait
            // here would hang the suite rather than test anything.
            await TestContext.Runner.RealtimeSeconds(2f);

            Assert.IsTrue(player.IsHealing, "A paused drink should still be in progress, not finished behind the menu.");
            Assert.AreEqual(healthBefore, health.CurrentHealth, 0.01f,
                "A wind-up that keeps counting under a pause makes the menu a free heal.");

            pause.SetPaused(false);

            await DrinkToCompletionAlreadyStarted(player);

            Assert.Greater(health.CurrentHealth, healthBefore,
                "Resuming should let the drink the pause froze finish normally.");
        }

        // ------------------------------------------------------------------------------------------

        private static async Task DrinkToCompletionAlreadyStarted(PlayerController2D player)
        {
            // PORT CHANGE: Unity polled a Time.time deadline. TestRunner.WaitUntil counts real
            // milliseconds, which is the same wall of time at timescale 1 and does not hang if a test
            // leaves the clock stopped.
            await TestContext.Runner.WaitUntil(() => !player.IsHealing, DrinkTimeout);

            Assert.IsFalse(player.IsHealing, "The drink should have finished by now.");
            await TestContext.Runner.NextFrame();
        }

        /// <summary>The HUD's flask line. Unity's uGUI <c>Text</c> is a Godot <see cref="Label"/>.</summary>
        private static Label FindFlaskText()
        {
            var hud = SceneQuery.FindFirst<GameplayHud>();
            Assert.NotNull(hud, "Gameplay scene should spawn the HUD.");

            // owned: false because the search must not care who owns the node. The HUD is an
            // authored scene now, so its children do have a scene owner - but the spawner instances
            // it and renames the root, and a search restricted to owned nodes is one refactor away
            // from silently finding nothing.
            var flask = hud.FindChild("FlaskText", recursive: true, owned: false) as Label;
            if (flask == null)
                Assert.Fail("The HUD should carry a FlaskText line for the flask readout.");

            return flask;
        }

        private static async Task StartDrink(PlayerController2D player)
        {
            player.RequestHeal();

            // The player's _Process may already have run this frame, so the drink is waited for rather
            // than assumed. The deadline is well inside the wind-up: reaching it means nothing started.
            await TestContext.Runner.WaitUntil(() => player.IsHealing, DrinkStartTimeout);

            Assert.IsTrue(player.IsHealing, "A heal request with a charge in hand should start the wind-up.");
        }

        private static async Task DrinkToCompletion(PlayerController2D player)
        {
            await StartDrink(player);
            await DrinkToCompletionAlreadyStarted(player);
        }

        private static async Task LoadGameplayScene()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            // Bootstrap plays the entry cutscene on ready and the director refuses every other shot while
            // it runs, so a death staged here would take a path the game never ships.
            //
            // Polled on real time: a cutscene runs unscaled, so a frozen scene never advances a scaled wait.
            CutsceneDirector director = CutsceneDirector.Instance;
            if (director == null)
                return;

            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => !director.IsPlaying, EntryCutsceneWait),
                "The entry cutscene has to end on its own after a scene load; nothing else hands input back.");
        }

        private static PlayerController2D FindPlayer()
        {
            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "Gameplay scene should spawn the player.");
            return player;
        }

        /// <summary>
        /// Unity's <c>player.gameObject</c>. The port made <see cref="PlayerMotor2D"/> the player body and
        /// hung every other component under it.
        /// </summary>
        private static CharacterBody2D PlayerBody(PlayerController2D player)
        {
            var body = player.GetComponentInParent<CharacterBody2D>();
            Assert.NotNull(body, "The player actor root should be the PlayerMotor2D body.");
            return body;
        }

        /// <remarks>
        /// PORT CHANGE: the zone *is* the node here - <see cref="CheckpointZone"/> is an
        /// <see cref="Area2D"/> - so there is no GameObject plus AddComponent, just one object positioned
        /// before it enters the tree (a shape that appears at the origin and is then moved sweeps across
        /// the arena on the way in). Unity's Rigidbody2D.WakeUp is gone with the Rigidbody2D: a Godot
        /// Area2D re-evaluates its overlaps on the next physics step whether or not anything moved.
        /// </remarks>
        private async Task PlaceZoneOnPlayer(PlayerController2D player)
        {
            var respawner = SceneQuery.FindFirst<GameplayEnemyRespawner>();
            Assert.NotNull(respawner, "Gameplay scene should own an enemy respawner.");

            _zone = new CheckpointZone
            {
                Name = "TestCheckpointZone",
                Position = PlayerBody(player).GlobalPosition,
            };
            TestContext.CurrentScene.AddChild(_zone);

            _zone.Initialize(ContextFor(player), respawner);

            // Trigger callbacks land on the physics step, not the frame the area appeared on.
            await TestContext.Runner.NextPhysicsFrame();
            await TestContext.Runner.NextPhysicsFrame();
            await TestContext.Runner.NextFrame();

            Assert.IsTrue(_zone.PlayerInside, "Standing in a zone should register the player as inside it.");
        }

        private static GameplayPlayerContext ContextFor(PlayerController2D player)
        {
            CharacterBody2D go = PlayerBody(player);
            return new GameplayPlayerContext(
                go,
                player,
                go.GetComponent<Health>(),
                go.GetComponent<StaminaSystem>(),
                go.GetComponent<HumanityController>(),
                go.GetComponent<SinResonanceController>(),
                go.GetComponent<DeathStateController>(),
                go.GetComponent<DebugVisualization>(),
                go.GetComponent<SoulsWallet>(),
                go.GetComponent<Poise>());
        }

        private static async Task KillPlayerAndWaitForRespawn()
        {
            var death = SceneQuery.FindFirst<DeathStateController>();
            Assert.NotNull(death, "Gameplay scene should own a death state controller.");

            death.GetComponentInParent<Health>().ApplyDamage(9999f, Vector2.Right);
            await WaitForRespawn(death);
        }

        private static async Task WaitForRespawn(DeathStateController death)
        {
            await TestContext.Runner.WaitUntil(() => !death.IsInSpiritState, RespawnWait);
            await TestContext.Runner.NextFrame();

            Assert.IsFalse(death.IsInSpiritState, "The player should have left spirit form by now.");
        }
    }
}
