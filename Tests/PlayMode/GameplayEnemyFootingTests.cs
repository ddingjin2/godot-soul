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
    /// The two enemy behaviours changed on 2026-08-17, pinned. Both were shipped without a test and
    /// both are invisible to the rest of the suite: every one of the eight layouts still places its
    /// sixty enemies on the arena floor, so nothing else in this project would notice a patrol that
    /// started walking off platforms again, and no other test stands close to a caster.
    /// </summary>
    /// <remarks>
    /// PORT NOTES: <see cref="GameplaySceneDefaults"/> hands back platform positions and sizes already
    /// converted - pixels, +Y down - so the only thing this fixture converts is its own literals and the
    /// direction of every vertical comparison. Those flips are called out one by one below, because this
    /// suite is precisely where a wrong sign would look like a passing test: "the grunt never dropped"
    /// and "the grunt never rose" are the same assertion with the operator reversed.
    /// </remarks>
    public sealed class GameplayEnemyFootingTests
    {
        private const string ChapterOne = "GameplayScene";

        /// <summary>
        /// `FirstStep` in `SceneLayout.json` - 4 units wide at y 1.6, the first platform on chapter one's
        /// road. Read from the layout rather than hard-coded so a designer moving it re-derives the test.
        /// </summary>
        private const string TestPlatform = "FirstStep";

        /// <summary>
        /// Long enough for a patrol to cross a 4-unit platform several times over at the grunt's 2 u/s,
        /// so a patrol that walks off has certainly done it by the end. Seconds are unscaled by the port.
        /// </summary>
        private const float WatchSeconds = 6f;

        private bool _startSkipAll;

        [SetUp]
        public void SetUp()
        {
            _startSkipAll = CutsceneDirector.SkipAll;
            CutsceneDirector.SkipAll = true;
            GameSave.Clear();
        }

        /// <summary>Unity's <c>[UnityTearDown]</c> coroutine; the harness awaits a Task teardown too.</summary>
        [TearDown]
        public async Task TearDown()
        {
            CutsceneDirector.SkipAll = _startSkipAll;
            await TestContext.Runner.NextFrame();
        }

        /// <summary>
        /// A grunt stood on a platform is still on it after several patrol legs. Before
        /// <see cref="EnemyStateMachine.ClampToGroundAhead"/> landed, nothing in Scripts/Enemy did ledge
        /// detection and this walked off the end on the first leg - which is the whole reason all eight
        /// chapters put every placement on the floor.
        /// </summary>
        [Test]
        public async Task AGruntPlacedOnAPlatform_IsStillOnItAfterPatrolling()
        {
            await LoadChapterOne();

            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterOne, null);
            Assert.IsTrue(TryFindPlatform(layout, TestPlatform, out GameplaySceneDefaults.PlatformDefinition platform),
                $"{ChapterOne}'s layout has to carry a platform named {TestPlatform} for this fixture to stand " +
                "an enemy on. Renaming it in SceneLayout.json means renaming it here.");

            var grunt = SceneQuery.FindFirst<MeleeGrunt>();
            Assert.NotNull(grunt, $"{ChapterOne} has to spawn a MeleeGrunt.");

            // Everything else off, so nothing chases this grunt off the platform for reasons that have
            // nothing to do with its footing. Unity's SetActive(false) is GameplayBuildShim's, which also
            // disables the collision shapes - hiding a node alone leaves it solid.
            foreach (EnemyStateMachine other in SceneQuery.FindAll<EnemyStateMachine>())
            {
                if (other != grunt)
                    other.SetActive(false);
            }

            var player = SceneQuery.FindFirst<PlayerController2D>();
            if (player != null)
            {
                // The actor root is the motor body, not the controller: switching off the controller node
                // alone would leave a solid, falling player in the arena.
                player.GetComponentInParent<PlayerMotor2D>()?.SetActive(false);
            }

            // Y FLIP: the deck of a platform is its centre *minus* half its height, because up is -Y.
            // Unity read Position.y + Size.y / 2 for the same surface.
            float top = platform.Position.Y - (platform.Size.Y / 2f);

            // And 0.6 units above the deck is 60 px *subtracted*, for the same reason.
            Teleport(grunt, new Vector2(platform.Position.X, top - World.U(0.6f)));

            // Settle onto the platform before the watch starts, or the drop itself is measured.
            await TestContext.Runner.Seconds(0.5f);
            float landedY = grunt.GlobalPosition.Y;

            // Y FLIP: Unity tracked the *lowest* y with Mathf.Min because falling made y smaller. Falling
            // makes y larger here, so the same "how far did it drop" number is the maximum.
            float deepestY = landedY;
            float deadline = GameClock.Time + WatchSeconds;
            while (GameClock.Time < deadline)
            {
                deepestY = Mathf.Max(deepestY, grunt.GlobalPosition.Y);
                await TestContext.Runner.NextPhysicsFrame();
            }

            // X does not flip, so left and right are the Unity expressions unchanged.
            float left = platform.Position.X - (platform.Size.X / 2f);
            float right = platform.Position.X + (platform.Size.X / 2f);
            float x = grunt.GlobalPosition.X;

            GD.Print($"[QA/Footing] A grunt left on {TestPlatform} ({left:F1}..{right:F1} at y {top:F1}) for " +
                     $"{WatchSeconds:F0}s ended at x={x:F2} y={grunt.GlobalPosition.Y:F2}, deepest y={deepestY:F2}.");

            // Y FLIP: Unity asserted lowestY > top - 0.5 ("never sank half a unit below the deck").
            // Deeper is a larger y here, so the same claim is deepestY < top + 50 px.
            Assert.Less(deepestY, top + World.U(0.5f),
                $"The grunt left the platform: it dropped to y {deepestY:F2} against a deck at {top:F1}. " +
                "ClampToGroundAhead is not stopping the patrol at the edge, which puts every enemy back on " +
                "the arena floor as the only place one can be placed.");

            // NUnit's Is.InRange is not in the harness; the same range check, written out.
            Assert.That(x >= left - World.U(0.6f) && x <= right + World.U(0.6f),
                $"The grunt walked off the end at x={x:F2}, outside {left:F1}..{right:F1}.");
        }

        /// <summary>
        /// A caster with the player inside its attack range fires. The old decision needed
        /// <c>dist > minDistance + repositionDistance</c> and capped at <c>detectionRange</c>, so the band
        /// was the one-unit shell 6 &lt; d &lt;= 7 and a player who walked up to a caster was never shot at.
        /// </summary>
        [Test]
        public async Task ACasterFiresAtAPlayerInsideItsAttackRange()
        {
            await LoadChapterOne();

            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            Assert.NotNull(catalog, "The tuning catalog has to load; attackRange is what this test is about.");

            // Already in pixels: the catalog builds this through RangedCasterData.Load, which scales.
            RangedCasterData tuning = catalog.RangedCaster;
            Assert.NotNull(tuning, "Resources/Design/RangedCaster.json has to load.");

            var caster = SceneQuery.FindFirst<RangedCaster>();
            Assert.NotNull(caster, $"{ChapterOne} has to spawn a RangedCaster.");

            foreach (EnemyStateMachine other in SceneQuery.FindAll<EnemyStateMachine>())
            {
                if (other != caster)
                    other.SetActive(false);
            }

            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, $"{ChapterOne} has to spawn a player.");

            PlayerMotor2D playerBody = player.GetComponentInParent<PlayerMotor2D>();
            Assert.NotNull(playerBody, "The player actor root is its motor body.");

            // Unity's MonoBehaviour.enabled is a bool property on the ported receiver.
            var receiver = SceneQuery.FindFirst<PlayerInputReceiver>();
            if (receiver != null)
                receiver.Enabled = false;

            // Well inside attackRange and well outside the retreat zone - the middle of the band the
            // caster is supposed to shoot from, and squarely inside the old silent gap. Both terms are
            // pixels: the tuning is scaled already, the literal 1.5 metres is scaled here.
            float standOff = tuning.attackRange - World.U(1.5f);
            Teleport(playerBody, new Vector2(
                caster.GlobalPosition.X - standOff, playerBody.GlobalPosition.Y));

            var fired = false;
            caster.OnShotFired += () => fired = true;

            float deadline = GameClock.Time + 8f;
            while (GameClock.Time < deadline && !fired)
            {
                // Godot-space axes; zero is zero either way.
                player.SetMoveInput(Vector2.Zero);
                await TestContext.Runner.NextPhysicsFrame();
            }

            float dist = playerBody.GlobalPosition.DistanceTo(caster.GlobalPosition);
            GD.Print($"[QA/Footing] A caster with the player {standOff:F1} px away (attackRange " +
                     $"{tuning.attackRange:F1}, minDistance {tuning.minDistance:F1}, repositionDistance " +
                     $"{tuning.repositionDistance:F1}) fired={fired}; they ended {dist:F2} apart.");

            Assert.IsTrue(fired,
                $"The caster never fired in 8s with the player {standOff:F1} px away, inside its authored " +
                $"attackRange of {tuning.attackRange:F1}. The firing band has collapsed back to the " +
                "minDistance + repositionDistance shell, where a caster is silent at exactly the range a " +
                "player fights it at.");
        }

        // ------------------------------------------------------------------------------------------

        private static async Task LoadChapterOne()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(ChapterOne));

            // The arena is built in _Ready, and the spawners run after it.
            await TestContext.Runner.Seconds(1f);
        }

        private static bool TryFindPlatform(
            GameplaySceneDefaults layout, string name, out GameplaySceneDefaults.PlatformDefinition found)
        {
            foreach (GameplaySceneDefaults.PlatformDefinition candidate in layout.Platforms)
            {
                if (candidate.Name == name)
                {
                    found = candidate;
                    return true;
                }
            }

            found = default;
            return false;
        }

        /// <summary>
        /// PORT CHANGE: Unity had to switch <c>Rigidbody2D.simulated</c> off across the move, because a
        /// Continuous-collision body teleported live sweeps from the old position through whatever stands
        /// between - and this arena is 300 units long. A <see cref="CharacterBody2D"/> only sweeps the
        /// motion <c>MoveAndSlide</c> is given, so writing <see cref="Node2D.GlobalPosition"/> moves it
        /// outright and there is nothing to suspend. Zeroing the velocity is the half that still matters:
        /// otherwise the body arrives carrying whatever fall speed it had built up.
        /// </summary>
        private static void Teleport(CharacterBody2D body, Vector2 to)
        {
            body.GlobalPosition = to;
            body.Velocity = Vector2.Zero;
        }
    }
}
