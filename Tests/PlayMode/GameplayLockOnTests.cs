using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// Lock-on exists to separate where the player walks from what the player faces, and facing is what
    /// aims every swing - the hitbox anchor mirrors off it. That makes a broken lock-on a combat bug
    /// wearing a camera bug's clothes, so these pin the two things that make it worth having: facing
    /// follows the target while walking away, and the lock lets go rather than aiming at a corpse.
    ///
    /// UNITS: every distance below was authored in Unity metres and is wrapped in <c>World.U</c> - the
    /// three-unit offset becomes 300 px and the five-unit overshoot 500 px. All of them are horizontal,
    /// so nothing here flips sign; <see cref="PlayerLockOn.LockOnRange"/> is already in pixels because
    /// <c>WorldTuningData</c> scales it when the JSON loads, and is not scaled again.
    /// Facing (-1 / +1) and health are unscaled.
    /// </summary>
    public sealed class GameplayLockOnTests
    {
        private const string GameplaySceneName = "GameplayScene";

        // GameplayEnter is a 1.8s shot; the rest is headroom for a slow headless frame.
        private const float EntryCutsceneWait = 6f;

        private Node2D _dummy;

        [SetUp]
        public void SetUp()
        {
            // PlayerPrefs is a file under user:// here and survives between headless runs, so a slot left
            // by another fixture would have the bootstrap resume a run instead of starting one.
            PlayerPrefs.DeleteAll();
            GameSave.LoadOnNextGameplayStart = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (GodotObject.IsInstanceValid(_dummy))
                _dummy.QueueFree();

            _dummy = null;
        }

        [Test]
        public async Task LockOn_HoldsFacingOnTheTargetWhileThePlayerWalksAway()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();

            // PORT NOTE: in Unity every one of these was a component on the player GameObject. Here they
            // are sibling child nodes of the player body, which GetComponentInParent is the lookup for.
            var lockOn = player.GetComponentInParent<PlayerLockOn>();
            Assert.NotNull(lockOn, "The spawner should give the player a lock-on component.");

            // The receiver writes the real keyboard into the controller every frame, so a test that
            // sets move input alongside it is overwritten with zero before the physics step - and a
            // zero input never turns the player, which makes "facing did not change" pass for the
            // wrong reason. Driven directly instead.
            DisableInputReceiver(player);

            Node2D body = PlayerBody(player);

            // World.U(3f) = 300 px. Horizontal, so no sign flip.
            PlaceDummyEnemy(body.GlobalPosition + new Vector2(World.U(3f), 0f));

            // Proves the input under test can turn the player at all before the lock is on. Without
            // this the rest of the test cannot tell a working override from a dead input.
            await WalkFor(player, Vector2.Left);
            Assert.AreEqual(-1f, player.FacingDir, 0.001f,
                "Walking left should turn the player left while nothing is locked.");

            lockOn.Acquire();
            Assert.IsTrue(lockOn.HasTarget, "An enemy three units away is well inside the grab range.");

            await TestContext.Runner.NextFrame();
            Assert.AreEqual(1f, player.FacingDir, 0.001f, "Facing should turn to the target on acquisition.");

            // The same input that just turned the player, now against the lock.
            await WalkFor(player, Vector2.Left);
            Assert.AreEqual(1f, player.FacingDir, 0.001f,
                "Walking away from a locked target must not turn the player away from it - that is the whole feature.");

            // PORT NOTE: still an explicit Clear, and now it is the only route. PlayerLockOn drops its
            // target in _ExitTree because Godot has no OnDisable, so a caller that parks the node rather
            // than removing it has to clear it itself.
            lockOn.Clear();

            await WalkFor(player, Vector2.Left);
            Assert.AreEqual(-1f, player.FacingDir, 0.001f,
                "Letting go has to hand facing back to the move input, or the player is stuck aiming at nothing.");
        }

        [Test]
        public async Task LockOn_LetsGoWhenTheTargetDies()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            var lockOn = player.GetComponentInParent<PlayerLockOn>();

            // World.U(3f) = 300 px.
            PlaceDummyEnemy(PlayerBody(player).GlobalPosition + new Vector2(World.U(3f), 0f));

            lockOn.Acquire();
            Assert.IsTrue(lockOn.HasTarget, "The dummy should be grabbable before it dies.");

            // Damage is unscaled; the direction is horizontal, so the Y flip does not reach it.
            _dummy.GetComponent<Health>().ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();
            await TestContext.Runner.NextFrame();

            Assert.IsFalse(lockOn.HasTarget,
                "A lock held on a dead enemy pins the player's facing to a corpse for the rest of the fight.");
        }

        [Test]
        public async Task LockOn_RefusesATargetBeyondTheAuthoredRange()
        {
            await LoadGameplayScene();

            PlayerController2D player = FindPlayer();
            var lockOn = player.GetComponentInParent<PlayerLockOn>();

            // LockOnRange is already pixels - WorldTuningData scales it at load - so only the five-unit
            // overshoot is converted: World.U(5f) = 500 px.
            PlaceDummyEnemy(PlayerBody(player).GlobalPosition
                            + new Vector2(lockOn.LockOnRange + World.U(5f), 0f));

            lockOn.Acquire();

            Assert.IsFalse(lockOn.HasTarget,
                "Lock-on reach is authored in WorldTuning.json; grabbing past it makes the number meaningless.");
        }

        /// <summary>
        /// Holds a move input across two physics steps. Facing is written in HandleMovement, which runs
        /// inside <c>PlayerMotor2D.FixedTick</c> off _PhysicsProcess, so a single frame is not enough to
        /// see a turn.
        /// </summary>
        private static async Task WalkFor(PlayerController2D player, Vector2 input)
        {
            for (var i = 0; i < 2; i++)
            {
                player.SetMoveInput(input);
                await TestContext.Runner.NextPhysicsFrame();
            }

            await TestContext.Runner.NextFrame();
        }

        private static void DisableInputReceiver(PlayerController2D player)
        {
            var receiver = player.GetComponentInParent<PlayerInputReceiver>();
            Assert.NotNull(receiver, "Gameplay scene should spawn a player input receiver.");

            // Unity's Behaviour.enabled. Godot has no such flag, so the port maps it onto whether the
            // node processes - which is the whole of what this component does.
            receiver.Enabled = false;
        }

        /// <summary>
        /// A bare body on the Enemy layer with a health component - which is all the acquisition filter
        /// looks for. Deliberately not a real archetype: those bring an AI that walks out of range
        /// mid-test.
        /// </summary>
        /// <remarks>
        /// PORT CHANGE: Unity's dummy was a GameObject with a *trigger* CircleCollider2D.
        /// <see cref="PlayerLockOn.Acquire"/> walks from the overlap hit up to a
        /// <see cref="CharacterBody2D"/> and refuses anything that has none, so the dummy has to be a
        /// solid body here. It is positioned before it enters the tree, or it sweeps across the arena
        /// from the origin on its first physics step.
        /// </remarks>
        private void PlaceDummyEnemy(Vector2 position)
        {
            var body = new CharacterBody2D
            {
                Name = "LockOnDummy",
                Position = position,
                CollisionLayer = World.Layer.Enemy,
                CollisionMask = 0,
            };

            body.AddChild(new CollisionShape2D
            {
                Name = "Collider",
                // World.U(0.5f) = 50 px, the authored Unity radius.
                Shape = new CircleShape2D { Radius = World.U(0.5f) },
            });

            var health = new Health { Name = nameof(Health) };
            body.AddChild(health);

            TestContext.CurrentScene.AddChild(body);

            // After the tree, so Health._Ready cannot overwrite them.
            health.SetMaxHealth(50f);
            health.SetHealth(50f);

            _dummy = body;
        }

        private static async Task LoadGameplayScene()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            CutsceneDirector director = CutsceneDirector.Instance;
            if (director == null)
                return;

            // The entry shot takes the controls and runs unscaled; polled on real time for that reason.
            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => !director.IsPlaying, EntryCutsceneWait),
                "The entry cutscene has to end on its own after a scene load.");
        }

        private static PlayerController2D FindPlayer()
        {
            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "Gameplay scene should spawn the player.");
            return player;
        }

        /// <summary>
        /// Unity's <c>player.transform</c>. The port made <see cref="PlayerMotor2D"/> the player body and
        /// hung every other component under it, so the actor's position is the body's, not the
        /// controller's.
        /// </summary>
        private static Node2D PlayerBody(PlayerController2D player)
        {
            var body = player.GetComponentInParent<CharacterBody2D>();
            Assert.NotNull(body, "The player actor root should be the PlayerMotor2D body.");
            return body;
        }
    }
}
