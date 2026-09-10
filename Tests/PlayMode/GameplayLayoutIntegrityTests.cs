using System.Text;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// Two things the rest of the suite would not notice going wrong.
    ///
    /// 1. <b>A chapter quietly falling back to the shipped 36-unit defaults.</b> Every chapter's arena
    ///    comes out of <see cref="GameplaySceneDefaults.CreateForScene"/>, which falls back when a
    ///    layout file cannot be read or a field is renamed - and the fallback is a complete, valid
    ///    arena, so nothing throws and nothing goes red. The 2026-08-17 density pass rewrote all eight
    ///    layouts with a script; a bad key would have moved seven chapters back to five enemies and one
    ///    bonfire with every existing test still passing.
    /// 2. <b>The bonfire and the gate portal growing into each other.</b> They sit 3.0 apart with radii
    ///    1.5 and 1.4, so the margin is one tenth of a unit. A `WorldTuning.checkpointZoneRadius` edit -
    ///    a designer-owned number in a design file - can make one Interact both rest and open the travel
    ///    panel, which is a bug nobody would look for in a tuning change.
    /// </summary>
    /// <remarks>
    /// PORT - UNITS. Every number below is still the Unity one, in metres, because that is what the
    /// prose and the design docs are written in. <see cref="GameplaySceneDefaults"/> hands back Godot
    /// pixels, so each measurement is divided by <see cref="World.Ppu"/> once at the point it is read
    /// and everything after that is metres again. That is cheaper and far easier to read than scaling
    /// six thresholds and re-checking every message that quotes one.
    /// </remarks>
    public sealed class GameplayLayoutIntegrityTests
    {
        private static readonly string[] Chapters =
        {
            "GameplayScene", "Chapter02_Orange", "Chapter03_Yellow", "Chapter04_Green",
            "Chapter05_Blue", "Chapter06_Indigo", "Chapter07_Violet", "Chapter08_White"
        };

        /// <summary>
        /// The shipped 36-unit arena, which is what every one of these is measured against not being.
        /// Three grunts, a leaper and a caster on one checkpoint - a real arena, which is exactly why a
        /// chapter collapsing into it is silent.
        /// </summary>
        private const int DefaultPlacements = 5;
        private const float DefaultGroundX = 36f;

        /// <summary>What a chapter is, as authored: a level rather than a corridor.</summary>
        private const int MinPlacements = 20;
        private const int MinCheckpoints = 3;
        private const float MinGroundX = 200f;

        private bool _startSkipAll;

        [SetUp]
        public void SetUp()
        {
            // PORT: PlayerPrefs is a ConfigFile at user://playerprefs.cfg and survives between headless
            // runs, so a slot left by a previous run would resume the overlap test's scene load at
            // someone else's bonfire. Unity's PlayerPrefs was per-machine but the suite never outlived
            // the editor session, so the Unity fixture did not need this.
            PlayerPrefs.DeleteAll();

            _startSkipAll = CutsceneDirector.SkipAll;
        }

        [TearDown]
        public void TearDown()
        {
            CutsceneDirector.SkipAll = _startSkipAll;
            GameSave.Clear();
        }

        [Test]
        public void EveryChapterLayout_IsALevelRatherThanTheShippedDefaults()
        {
            var report = new StringBuilder("[QA/Layout] Every chapter as CreateForScene builds it:");
            var collapsed = 0;

            foreach (string scene in Chapters)
            {
                GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(scene, null);
                Assert.NotNull(layout, $"{scene} has to produce a layout.");

                int placements = layout.MeleeGruntSpawns.Length
                                 + layout.LeapingAttackerSpawns.Length
                                 + layout.RangedCasterSpawns.Length;

                // px -> Unity metres, so every threshold and message below stays the authored number.
                float groundX = World.ToUnits(layout.GroundSize.X);

                report.Append($"\n  {scene}: {placements} placements, " +
                              $"{layout.CheckpointPositions.Length} bonfires, ground {groundX:F0} units");

                if (placements <= DefaultPlacements || Mathf.IsEqualApprox(groundX, DefaultGroundX))
                {
                    collapsed++;
                    report.Append("  <-- COLLAPSED TO DEFAULTS");
                }
            }

            GD.Print(report.ToString());

            Assert.Zero(collapsed,
                $"{collapsed} chapter(s) came back as the shipped 36-unit arena rather than the level that is " +
                "authored for them. A renamed or unreadable key in SceneLayout_*.json falls back silently, and the " +
                "fallback is a valid arena - so the road keeps working while seven eighths of it quietly stops " +
                "existing. Read the log line above for which.");
        }

        [Test]
        public void EveryChapterLayout_KeepsItsPlacementsAndBonfires()
        {
            foreach (string scene in Chapters)
            {
                GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(scene, null);

                int placements = layout.MeleeGruntSpawns.Length
                                 + layout.LeapingAttackerSpawns.Length
                                 + layout.RangedCasterSpawns.Length;

                // px -> metres; MinGroundX is the authored 200 units, unchanged from the Unity fixture.
                float groundX = World.ToUnits(layout.GroundSize.X);

                Assert.GreaterOrEqual(placements, MinPlacements,
                    $"{scene} carries {placements} placements. Below {MinPlacements} it is not the level the " +
                    "[[PlaytimePlan]] trash budget is written against, and its soul income stops reaching the " +
                    "ProgressionTuning curve.");
                Assert.GreaterOrEqual(layout.CheckpointPositions.Length, MinCheckpoints,
                    $"{scene} authors {layout.CheckpointPositions.Length} bonfire(s); a chapter is built around " +
                    $"{MinCheckpoints}, and the run-back measurements all start from the last one.");
                Assert.GreaterOrEqual(groundX, MinGroundX,
                    $"{scene}'s floor is {groundX:F0} units. The chapters are ~300; anything this short " +
                    "is the corridor they were built out of.");
            }
        }

        /// <summary>
        /// The bonfire and the portal, as the arena actually builds them, are far enough apart that one
        /// Interact cannot reach both. Measured off the shipped colliders rather than the constants,
        /// because the radius that moves is the one in a design file.
        /// </summary>
        [Test]
        public async Task TheBonfireAndTheGatePortal_DoNotOverlap()
        {
            CutsceneDirector.SkipAll = true;
            GameSave.Clear();

            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(Chapters[0]));

            await TestContext.Runner.Seconds(1f);

            // PORT: Unity's FindObjectsByType over the whole tree is SceneQuery.FindAll here.
            System.Collections.Generic.List<CheckpointZone> bonfires = SceneQuery.FindAll<CheckpointZone>();
            System.Collections.Generic.List<GateTravelZone> portals = SceneQuery.FindAll<GateTravelZone>();

            Assert.IsNotEmpty(bonfires, "Every arena builds a bonfire.");
            Assert.IsNotEmpty(portals, "Every arena builds a gate portal beside each bonfire.");

            // Each portal against the bonfire it was built for, which is the nearest one. Taking any
            // bonfire and any portal pairs a portal at 3 with a bonfire at 68 and reports a margin of 62
            // - a pass that measures nothing, which is how this test read on its first run.
            var worst = float.MaxValue;
            var report = new StringBuilder("[QA/Layout] Every bonfire against the portal built beside it:");

            foreach (GateTravelZone portal in portals)
            {
                CheckpointZone nearest = null;
                var nearestGap = float.MaxValue;
                foreach (CheckpointZone bonfire in bonfires)
                {
                    // px; converted to metres only where it is printed or compared to an authored number.
                    float gap = bonfire.GlobalPosition.DistanceTo(portal.GlobalPosition);
                    if (gap >= nearestGap)
                        continue;

                    nearestGap = gap;
                    nearest = bonfire;
                }

                Assert.NotNull(nearest, "A portal with no bonfire anywhere in the arena.");

                float reaches = ReachOf(nearest) + ReachOf(portal);
                float margin = nearestGap - reaches;
                worst = Mathf.Min(worst, margin);

                report.Append($"\n  x={World.ToUnits(portal.GlobalPosition.X):F1}: " +
                              $"{World.ToUnits(nearestGap):F2} apart, reaches total " +
                              $"{World.ToUnits(reaches):F2}, margin {World.ToUnits(margin):F2}");
            }

            GD.Print(report.ToString());

            // Zero is zero in either unit, so the threshold needs no conversion; only the number quoted
            // back in the message is turned into metres.
            Assert.Greater(worst, 0f,
                $"A bonfire and its gate portal overlap by {World.ToUnits(-worst):F2}. One Interact now both rests " +
                "and opens the travel panel. The shipped margin is a tenth of a unit, so this is most likely a " +
                "WorldTuning.checkpointZoneRadius edit - move the portal's offset with it, or put the radius back.");
        }

        /// <summary>
        /// The zone's reach in pixels. Unity read a <c>CircleCollider2D.radius</c> against
        /// <c>lossyScale</c>; both zones are <see cref="Area2D"/>s with a child
        /// <see cref="CollisionShape2D"/> here, so the circle is read off the shape and the scale off
        /// <see cref="Node2D.GlobalScale"/>. The non-circle fallback is Unity's <c>Collider2D.bounds</c>,
        /// which <c>EnemyShim.BodyBounds</c> already stands in for.
        /// </summary>
        private static float ReachOf(Node2D zone)
        {
            var shape = zone.GetComponent<CollisionShape2D>();
            if (shape?.Shape is CircleShape2D circle)
            {
                Vector2 scale = zone.GlobalScale;
                return circle.Radius * Mathf.Max(scale.X, scale.Y);
            }

            Assert.NotNull(shape, $"{zone.Name} has to carry a collider for its reach to be measured.");
            Rect2 bounds = zone.BodyBounds(Vector2.Zero);
            return Mathf.Max(bounds.Size.X, bounds.Size.Y) * 0.5f;
        }
    }
}
