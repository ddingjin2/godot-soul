using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// Covers the checkpoint zone: what a rest gives back, what it deliberately does not, and the two
    /// wiring decisions that fail silently when someone undoes them - the trigger filter that ignores the
    /// player's attack hitbox, and taking Interact off <see cref="PlayerInputReceiver"/> instead of
    /// polling it in the zone's own _Process.
    ///
    /// Enemy respawn is not re-tested here: the zone calls the same
    /// <see cref="GameplayEnemyRespawner.RespawnEnemies"/> the death loop already pins in
    /// <c>GameplayDeathLoopTests</c>.
    ///
    /// The shipped scene now places a zone at every checkpoint its layout names, but every test here
    /// still builds its own on top of the player: these assert what a rest does, not where one is. The
    /// zone node is created at its final position before it enters the tree, because a collider that
    /// appears at the origin and is then moved sweeps across the arena on the way in.
    ///
    /// UNITS: distances are authored Unity metres wrapped in <c>World.U</c> - 1.6 -> 160 px, 4 -> 400 px,
    /// 0.6 -> 60 px, 1 -> 100 px. All of them are horizontal, so no vertical expectation flips sign here.
    /// Health, stamina, humanity, souls and frame counts are unscaled.
    /// </summary>
    public sealed class GameplayCheckpointZoneTests
    {
        private const string GameplaySceneName = "GameplayScene";

        // DeathStateController holds the player in spirit form for three seconds before respawning.
        private const float RespawnWait = 4.5f;

        // GameplayEnter is a 1.8s shot; the rest is headroom for a slow headless frame.
        private const float EntryCutsceneWait = 6f;

        private float _startTimeScale;
        private CheckpointZone _zone;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;

            // PORT CHANGE: Unity snapshotted the existing save slot here and put it back in teardown,
            // because PlayerPrefs was editor-session state. MyGame.Core.PlayerPrefs is a real file under
            // user:// that outlives the run, so a slot from a previous run is not something to preserve -
            // it is the thing that makes the next run resume instead of start. Wiped instead.
            PlayerPrefs.DeleteAll();
        }

        [TearDown]
        public void TearDown()
        {
            GameClock.TimeScale = _startTimeScale;
            GameSave.LoadOnNextGameplayStart = false;
            GameSave.Clear();

            // The whole Checkpoint.tscn instance, not just the zone inside it.
            if (GodotObject.IsInstanceValid(_zone))
                (_zone.GetParent() ?? _zone).QueueFree();

            _zone = null;
        }

        /// <summary>
        /// The assertion that stops someone "fixing" the omission. Humanity is the only thing a rest does
        /// not refund, which is the entire price of the rest.
        /// </summary>
        [Test]
        public async Task Rest_RefillsHealthAndStamina_ButLeavesHumanityWhereItWas()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            await PlaceZoneOnPlayer(player);

            var health = player.GetComponentInParent<Health>();
            var stamina = player.GetComponentInParent<StaminaSystem>();
            var humanity = player.GetComponentInParent<HumanityController>();
            Assert.NotNull(stamina, "The player should carry a stamina system.");
            Assert.NotNull(humanity, "The player should carry a humanity controller.");

            // Drained after the zone is live, so stamina regen between frames cannot refill it for us.
            health.SetHealth(health.MaxHealth * 0.25f);
            stamina.SetStamina(stamina.MaxStamina * 0.1f);
            humanity.SetHumanity(40f);

            _zone.TryActivate();

            Assert.AreEqual(health.MaxHealth, health.CurrentHealth, 0.01f, "A rest should hand health back in full.");
            Assert.AreEqual(stamina.MaxStamina, stamina.CurrentStamina, 0.01f, "A rest should hand stamina back in full.");
            Assert.AreEqual(40f, humanity.CurrentHumanity, 0.01f,
                "A rest must not restore humanity: it is spent for good, and refunding it removes the only price the rest charges.");
        }

        /// <summary>
        /// The subtlest thing in the file: only the player's solid body may answer the trigger. The attack
        /// hitbox is a permanently-live trigger area parented under the player, sitting 0.4 to the side of
        /// the body. Letting it answer would drive the inside flag from a collider that is not where the
        /// player is standing.
        ///
        /// Two halves, because the filter breaks in two different ways. The swing half guards the future.
        /// The edge half is the one that failed in Unity: stood at the rim the offset hitbox is already
        /// outside the zone while the body is still inside, so without the filter its exit cleared the
        /// flag on a player who had not moved.
        /// </summary>
        /// <remarks>
        /// PORT NOTE - the port cannot fail this the way Unity could. <see cref="CheckpointZone"/> is an
        /// <see cref="Area2D"/> watching <c>BodyEntered</c>/<c>BodyExited</c>, and Godot only ever reports
        /// solid bodies through those, so the attack hitbox - an <see cref="Area2D"/> itself - is filtered
        /// by the engine before the zone sees it. The test is kept exactly as it was: it now pins that the
        /// zone stays on the body signals rather than switching to <c>AreaEntered</c>, which would put the
        /// Unity bug back.
        /// </remarks>
        [Test]
        public async Task AttackHitbox_NeverDrivesThePlayerInsideFlag()
        {
            // Zone radius 1.5, body half-width 0.29, hitbox centred 0.4 ahead with radius 0.4. At this
            // distance the hitbox reaches no closer than 1.6 (outside) while the body reaches 1.31
            // (inside). World.U(1.6f) = 160 px against the zone's authored 150 px radius.
            float hitboxOutsideBodyInside = World.U(1.6f);

            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            await PlaceZoneOnPlayer(player);

            player.RequestAttack();

            // Long enough to cover the whole swing: windup, active window and recovery.
            for (int frame = 0; frame < 40; frame++)
            {
                Assert.IsTrue(_zone.PlayerInside,
                    $"Swinging inside the zone must not clear the inside flag (frame {frame}).");
                await TestContext.Runner.NextFrame();
            }

            CharacterBody2D body = PlayerBody(player);
            Vector2 zonePosition = _zone.GlobalPosition;
            body.GlobalPosition = new Vector2(zonePosition.X + hitboxOutsideBodyInside, body.GlobalPosition.Y);

            await TestContext.Runner.NextPhysicsFrame();
            await TestContext.Runner.NextPhysicsFrame();
            await TestContext.Runner.NextFrame();

            Assert.IsTrue(_zone.PlayerInside,
                "The hitbox leaving the zone must not evict a player whose body is still standing in it.");

            var health = player.GetComponentInParent<Health>();
            health.SetHealth(15f);
            _zone.TryActivate();

            Assert.AreEqual(health.MaxHealth, health.CurrentHealth, 0.01f,
                "A rest at the rim of the zone should still land.");
        }

        [Test]
        public async Task Rest_WritesTheSaveSlotHoldingTheRestedRun()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            await PlaceZoneOnPlayer(player);

            GameSave.Clear();

            var health = player.GetComponentInParent<Health>();
            var wallet = player.GetComponentInParent<SoulsWallet>();
            health.SetHealth(13f);
            wallet.SetSouls(250);

            _zone.TryActivate();

            Assert.IsTrue(GameSave.Exists, "A rest should write the save slot.");

            GameSaveData saved = GameSave.Read();
            Assert.NotNull(saved, "The slot a rest wrote should read back.");
            Assert.AreEqual(health.MaxHealth, saved.health, 0.01f,
                "The slot should hold the rested run: captured after the refill, not the 13 health walked in with.");
            Assert.AreEqual(250, saved.souls, "A rest should bank the souls the player is carrying.");
        }

        [Test]
        public async Task Rest_MovesTheRespawnPointToTheZone()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            var sceneCheckpoint = SceneQuery.FindFirst<Checkpoint>();
            Assert.NotNull(sceneCheckpoint, "Gameplay scene should build its checkpoint marker.");

            // Away from the scene checkpoint, which sits on the spawn point - resting on top of it would
            // prove nothing about the respawn point moving. World.U(4f) = 400 px, horizontal.
            CharacterBody2D body = PlayerBody(player);
            body.GlobalPosition = sceneCheckpoint.GlobalPosition + new Vector2(World.U(4f), 0f);
            await TestContext.Runner.NextPhysicsFrame();

            await PlaceZoneOnPlayer(player);
            Vector2 zonePosition = _zone.GlobalPosition;

            _zone.TryActivate();
            await KillPlayerAndWaitForRespawn();

            // World.U(0.6f) = 60 px of slack, World.U(1f) = 100 px of separation.
            Assert.AreEqual(zonePosition.X, body.GlobalPosition.X, World.U(0.6f),
                "Dying after a rest should return the player to the zone.");
            Assert.Greater(Mathf.Abs(body.GlobalPosition.X - sceneCheckpoint.GlobalPosition.X), World.U(1f),
                "The respawn should have left the scene checkpoint behind, not stayed on it.");
        }

        [Test]
        public async Task Rest_IsRefusedWhileInSpiritState()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            await PlaceZoneOnPlayer(player);

            var death = player.GetComponentInParent<DeathStateController>();
            var health = player.GetComponentInParent<Health>();
            Assert.NotNull(death, "The player should carry a death state controller.");

            // Damage is unscaled and the direction is horizontal, so neither is converted.
            health.ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();

            Assert.IsTrue(death.IsInSpiritState, "A lethal hit should put the player in spirit form.");

            GameSave.Clear();
            float spiritHealth = health.CurrentHealth;

            _zone.TryActivate();

            Assert.AreEqual(spiritHealth, health.CurrentHealth, 0.01f,
                "Resting mid-death would hand back full health that the respawn immediately overwrites.");
            Assert.IsFalse(GameSave.Exists, "A refused rest should not write the save slot.");

            // Leave spirit form behind so the scene is not torn down mid-death sequence.
            await WaitForRespawn(death);
        }

        [Test]
        public async Task Interact_OutsideTheZone_RestsNothing()
        {
            await LoadGameplayScene();

            // The zero-zones guarantee is gone on purpose ([[PlaytimePlan]] Phase 1): every arena now
            // builds a bonfire at each position its layout names. What replaces it is the count - one
            // zone per authored checkpoint and not one more, because a zone that arrives from anywhere
            // else is a respawn point nobody placed. Read off the layout rather than written down, so a
            // designer adding a bonfire moves this number instead of turning it red.
            //
            // Counted before this fixture builds its own zone, exactly as the Unity version did.
            int authoredCheckpoints = GameplaySceneDefaults.CreateForScene(GameplaySceneName, null).CheckpointPositions.Length;
            Assert.AreEqual(authoredCheckpoints, SceneQuery.FindAll<CheckpointZone>().Count,
                "The shipped scene should place exactly one checkpoint zone per authored checkpoint position.");

            PlayerController2D player = FindPlayer();
            await PlaceZoneOnPlayer(player);

            // World.U(4f) = 400 px, well outside the zone's 150 px reach.
            CharacterBody2D body = PlayerBody(player);
            body.GlobalPosition = _zone.GlobalPosition + new Vector2(World.U(4f), 0f);

            // PORT CHANGE - waited on rather than counted in physics steps. An Area2D reports a
            // teleport out one step later than it reports a teleport in: a GlobalPosition write between
            // steps only reaches the physics server when the frame's transform notifications are
            // flushed, the step after that is the one that separates the shapes, and the callback is
            // delivered from the flush at the top of the step after *that*. Unity's OnTriggerExit2D came
            // off the same FixedUpdate that moved the body, so the Unity fixture could count frames.
            // Three is the number today; waiting on the flag survives the fourth that a slow headless
            // frame would need.
            await TestContext.Runner.WaitUntil(() => !_zone.PlayerInside);

            Assert.IsFalse(_zone.PlayerInside, "Walking out of the zone should clear the inside flag.");

            GameSave.Clear();
            var health = player.GetComponentInParent<Health>();
            health.SetHealth(20f);

            _zone.TryActivate();

            Assert.AreEqual(20f, health.CurrentHealth, 0.01f, "Interact outside the zone should not rest.");
            Assert.IsFalse(GameSave.Exists, "Interact outside the zone should not write the save slot.");
        }

        /// <summary>
        /// Pause and victory stop play by disabling <see cref="PlayerInputReceiver"/>, and a zero
        /// timescale does not stop _Process. A zone that read the key itself would therefore let the
        /// player rest straight through the pause menu, and nothing about that failure is visible in play.
        /// </summary>
        /// <remarks>
        /// PORT CHANGE - Unity's forbidden methods were <c>Update</c> and <c>LateUpdate</c>; the Godot
        /// per-frame callbacks are <c>_Process</c> and <c>_PhysicsProcess</c>, so those are the names
        /// reflected on. And <c>PlayerInputReceiver.OnInteract</c> is a C# <c>event</c> rather than a
        /// public <c>UnityEvent</c> field, so it is looked up with <c>GetEvent</c> - <c>GetField</c> would
        /// find only the compiler's private backing field and pass for the wrong reason.
        /// </remarks>
        [Test]
        public void CheckpointZone_TakesInteractFromTheReceiver_NotItsOwnUpdate()
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            Assert.IsNull(typeof(CheckpointZone).GetMethod("_Process", Flags),
                "CheckpointZone must not poll input in _Process: a zero timescale keeps _Process running, so pause and victory would not stop a rest.");
            Assert.IsNull(typeof(CheckpointZone).GetMethod("_PhysicsProcess", Flags),
                "CheckpointZone must not poll input in _PhysicsProcess either, for the same reason.");
            Assert.IsNotNull(typeof(PlayerInputReceiver).GetEvent("OnInteract"),
                "PlayerInputReceiver has to keep publishing OnInteract - it is the only route the zone has to the key.");
        }

        private static async Task LoadGameplayScene()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            // Bootstrap plays the entry cutscene on ready, and while it runs the director refuses every
            // other shot - so a rest test that kills the player here gets a death with no death cutscene,
            // which is not the path the game ships.
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
        /// Unity's <c>player.gameObject</c> / <c>player.transform</c>. The port made
        /// <see cref="PlayerMotor2D"/> the player body and hung the other components under it, so the node
        /// that carries the position and the solid collider is the body, not the controller.
        /// </summary>
        private static CharacterBody2D PlayerBody(PlayerController2D player)
        {
            var body = player.GetComponentInParent<CharacterBody2D>();
            Assert.NotNull(body, "The player actor root should be the PlayerMotor2D body.");
            return body;
        }

        /// <remarks>
        /// PORT CHANGE: Unity built a GameObject and then <c>AddComponent&lt;CheckpointZone&gt;</c>.
        /// Unity also had to wake the player's sleeping Rigidbody2D, or a collider that appeared under it
        /// generated no contacts; a Godot <see cref="Area2D"/> re-evaluates its overlaps on the next
        /// physics step whether or not anything moved, so that call has nothing left to do and is gone.
        /// <para>
        /// K7: the shipped bonfire - <c>Scenes/World/Checkpoint.tscn</c> - rather than a bare
        /// <see cref="CheckpointZone"/>. The zone used to build its own trigger shape and marker when it
        /// found none, and that branch is gone: the scene authors both, and a zone without them reports
        /// an error instead. Positioned before it enters the tree, so no shape sweeps across the arena.
        /// </para>
        /// </remarks>
        private async Task PlaceZoneOnPlayer(PlayerController2D player)
        {
            var respawner = SceneQuery.FindFirst<GameplayEnemyRespawner>();
            Assert.NotNull(respawner, "Gameplay scene should own an enemy respawner.");

            var bonfire = GD.Load<PackedScene>("res://Scenes/World/Checkpoint.tscn").Instantiate<Node2D>();
            bonfire.Name = "TestCheckpoint";
            bonfire.Position = PlayerBody(player).GlobalPosition;
            TestContext.CurrentScene.AddChild(bonfire);

            // The zone sits at the bonfire root's origin, so its GlobalPosition is the one written above.
            _zone = bonfire.GetNode<CheckpointZone>("CheckpointZone");
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
