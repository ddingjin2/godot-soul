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
    /// The paths a player takes that nothing drove: the two Interact zones through a real
    /// <see cref="PlayerInputReceiver.OnInteract"/>, a resume at a middle bonfire, and New Game+ carrying
    /// its souls into the first chapter.
    ///
    /// Every one of these is a place where the arena builds an object, the save writes a field, and the
    /// two are joined by a subscription nobody exercises. They were on the QA backlog from three separate
    /// reviews for that reason - the components are all covered alone, and the joins are not.
    ///
    /// UNITS: the two teleport offsets are authored Unity metres and go through <c>World.U</c> (1.2 -> 120
    /// px), as does the resume tolerance (2 -> 200 px). Both are horizontal, so nothing flips sign.
    /// Checkpoint positions read off <see cref="GameplaySceneDefaults"/> are already in Godot pixels and
    /// are not converted again. Souls, lap numbers and indices are unscaled.
    /// </summary>
    public sealed class GameplayInteractAndResumeTests
    {
        private const string ChapterOne = "GameplayScene";
        private const string ChapterTwo = "Chapter02_Orange";

        private bool _startSkipAll;
        private Difficulty _startDifficulty;
        private int _startNewGamePlus;

        [SetUp]
        public void SetUp()
        {
            _startSkipAll = CutsceneDirector.SkipAll;
            _startDifficulty = DifficultySettings.Current;
            _startNewGamePlus = DifficultySettings.NewGamePlus;

            CutsceneDirector.SkipAll = true;
            DifficultySettings.Set(Difficulty.Normal, 0);

            // PORT CHANGE: MyGame.Core.PlayerPrefs is a file under user:// and survives between headless
            // runs, so GameSave.Clear alone is not enough - a slot written by an earlier *run* would
            // still be sitting there. Unity's PlayerPrefs died with the editor session.
            //
            // The Unity fixture also snapshotted the existing slot and put it back in teardown. There is
            // nothing to preserve once the file is wiped, so that pair is gone with it.
            PlayerPrefs.DeleteAll();
            GameSave.Clear();
        }

        [TearDown]
        public async Task TearDown()
        {
            CutsceneDirector.SkipAll = _startSkipAll;
            GameSave.LoadOnNextGameplayStart = false;
            DifficultySettings.Set(_startDifficulty, _startNewGamePlus);
            GameSave.Clear();

            // Godot's input actions are global and latched: an action left pressed here is still pressed
            // in the next fixture. Unity's synthetic input was an event invocation with no such state.
            Input.ActionRelease(InteractAction);

            await TestContext.Runner.NextFrame();
        }

        /// <summary>
        /// The gate portal, opened the way a player opens it. Every other portal test calls the component;
        /// this one presses Interact, which is the join that would break if the zone stopped subscribing to
        /// the receiver - and the receiver is what the pause menu and the victory panel switch off.
        /// </summary>
        [Test]
        public async Task TheGatePortal_OpensTheTravelPanel_ThroughARealInteract()
        {
            await LoadFresh(ChapterOne);

            var portal = SceneQuery.FindFirst<GateTravelZone>();
            Assert.NotNull(portal, "Every arena builds a gate portal.");

            PlayerController2D player = RequirePlayer();
            var hud = SceneQuery.FindFirst<GameplayHud>();
            Assert.NotNull(hud, "The arena builds a HUD, which is what the portal opens.");

            // Standing in it, not near it: the zone answers Interact only while the player body is inside.
            Teleport(PlayerBody(player), portal.GlobalPosition);
            await TestContext.Runner.Seconds(0.4f);

            var receiver = player.GetComponentInParent<PlayerInputReceiver>();
            Assert.NotNull(receiver, "The shipped player carries the input receiver the zones listen to.");

            await PressInteract();

            Assert.IsTrue(hud.IsGateTravelVisible,
                "Interact inside the gate portal did not open the travel panel. The zone is built by the " +
                "environment builder and wired by GameplayBootstrap; if either stops, the portal becomes scenery " +
                "and nothing else in the suite notices.");
        }

        /// <summary>
        /// The shortcut door, opened the way a player opens it. <see cref="ShortcutGate"/> subscribes off
        /// the body that enters its trigger rather than through any Initialize call, so this is the only
        /// thing that proves that self-wiring works - and it is the only wiring the shipped door has.
        /// </summary>
        [Test]
        public async Task TheShortcutDoor_Opens_ThroughARealInteract()
        {
            await LoadFresh(ChapterTwo);

            var gate = SceneQuery.FindFirst<ShortcutGate>();
            Assert.NotNull(gate, $"{ChapterTwo} authors a shortcut door.");
            Assert.IsFalse(gate.IsOpen, "A fresh run starts with the door shut; that is what makes it a shortcut.");

            PlayerController2D player = RequirePlayer();

            // The far side is the one the level makes the player walk round to, and the gate judges which
            // side they are on when Interact is pressed rather than when they entered.
            // World.U(1.2f) = 120 px, horizontal, so no sign flip.
            Teleport(PlayerBody(player), gate.GlobalPosition + new Vector2(World.U(1.2f), 0f));
            await TestContext.Runner.Seconds(0.4f);

            var receiver = player.GetComponentInParent<PlayerInputReceiver>();
            Assert.NotNull(receiver, "The shipped player carries the input receiver.");

            await PressInteract();

            Assert.IsTrue(gate.IsOpen,
                "Interact from the far side did not open the shortcut. The door subscribes in its Area2D's " +
                "BodyEntered off the arriving body - nothing calls Initialize on it - so this is the whole of " +
                "its wiring.");
        }

        /// <summary>
        /// A slot recording the middle bonfire resumes at the middle bonfire. The last one is covered by
        /// the run-back runs and the first by the walkable runs; index 1 is the one no test has ever asked
        /// for, and it is the only index where "falls back to the first" and "uses the recorded one" differ
        /// from each other and from the end.
        /// </summary>
        [Test]
        public async Task ASlotRecordingTheMiddleBonfire_ResumesThere()
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterOne, null);
            Assert.GreaterOrEqual(layout.CheckpointPositions.Length, 3,
                $"{ChapterOne} has to author at least three bonfires for a middle one to exist.");

            // Already Godot pixels - GameplaySceneDefaults converts the authored layout once, at build.
            float expectedX = layout.CheckpointPositions[1].X;

            GameSave.Write(new GameSaveData
            {
                chapterScene = ChapterOne,
                checkpointIndex = 1,
                health = -1f,
                humanity = -1f,
                souls = -1
            });
            GameSave.LoadOnNextGameplayStart = true;

            await LoadChapter(ChapterOne);

            PlayerController2D player = RequirePlayer();
            float startX = PlayerBody(player).GlobalPosition.X;

            GD.Print($"[QA/Resume] A slot recording bonfire 1 of {ChapterOne} woke the player at x={startX:F2}, " +
                     $"against the authored {expectedX:F2}.");

            // Unity's 2-unit tolerance is World.U(2f) = 200 px.
            Assert.AreEqual(expectedX, startX, World.U(2f),
                $"A slot recording bonfire 1 put the player at {startX:F2} instead of {expectedX:F2}. Either the " +
                "index was thrown away and it fell back to the first bonfire, or it clamped to the last - both of " +
                "which cost the player the walk they had already done.");
        }

        /// <summary>
        /// New Game+ keeps the purse. The lap is what carries a finished player into a harder run, and the
        /// souls are the only thing it carries - a lap that dropped them would silently reset the whole
        /// progression the lap exists to continue.
        /// </summary>
        [Test]
        public async Task NewGamePlus_CarriesItsSoulsIntoTheChapter()
        {
            const int carried = 4321;

            GameSave.Write(new GameSaveData
            {
                chapterScene = ChapterOne,
                checkpointIndex = 0,
                souls = carried,
                newGamePlus = 1,
                difficulty = (int)Difficulty.Normal,
                health = -1f,
                humanity = -1f
            });
            GameSave.LoadOnNextGameplayStart = true;

            await LoadChapter(ChapterOne);

            PlayerController2D player = RequirePlayer();
            var purse = player.GetComponentInParent<SoulsWallet>();
            Assert.NotNull(purse, "The shipped player carries a wallet.");

            GD.Print($"[QA/Resume] A New Game+ lap 1 slot holding {carried} souls resumed with {purse.Souls}, " +
                     $"difficulty {DifficultySettings.Current}, lap {DifficultySettings.NewGamePlus}.");

            Assert.AreEqual(carried, purse.Souls,
                $"A New Game+ slot holding {carried} souls resumed with {purse.Souls}. The lap keeps the purse - " +
                "that is the only thing it carries forward, so dropping it makes the lap a fresh game with harder " +
                "enemies and nothing to spend.");
            Assert.AreEqual(1, DifficultySettings.NewGamePlus,
                "The lap number itself has to survive the load, or the enemy-health stack the lap exists for is " +
                "never applied.");
        }

        // ------------------------------------------------------------------------------------------

        private const string InteractAction = "interact";

        /// <summary>
        /// PORT CHANGE: Unity raised the receiver's <c>UnityEvent</c> directly
        /// (<c>receiver.OnInteract.Invoke()</c>). <see cref="PlayerInputReceiver.OnInteract"/> is a C#
        /// <c>event</c> here and no outsider can raise one, so the key itself is pressed instead - which
        /// is what both of these tests say on the tin.
        /// </summary>
        /// <remarks>
        /// The action is re-pressed on every frame of the window rather than pressed once and held, and
        /// that is the whole of what makes it land. <c>GameplayInput.InteractPressed</c> is
        /// <c>Input.IsActionJustPressed</c>, which is true only during the process frame the press was
        /// stamped in - and a test never resumes at the top of one. <c>SceneTree.ProcessFrame</c> fires
        /// *before* the tree propagates _Process, while a <c>SceneTreeTimer</c> (the 0.4s settle above)
        /// fires after it, so a single press can be stamped on the far side of the receiver's tick and
        /// then never be seen again. Re-stamping the edge every frame makes it land whichever way that
        /// falls; the release first is what lets it re-stamp, because Input.ActionPress only records the
        /// frame on a rising edge. Same pattern, same reason, as GameplayChapterTwoTraversalTests.
        /// </remarks>
        private static async Task PressInteract()
        {
            for (var frame = 0; frame < 3; frame++)
            {
                Input.ActionRelease(InteractAction);
                Input.ActionPress(InteractAction);
                await TestContext.Runner.NextFrame();
            }

            // Released here as well as in teardown: Godot's action state is global and latched, so a
            // press left standing would still be down for whatever this test does next.
            Input.ActionRelease(InteractAction);
            await TestContext.Runner.NextFrame();
        }

        private static async Task LoadFresh(string sceneName)
        {
            GameSave.Clear();
            GameSave.LoadOnNextGameplayStart = false;
            await LoadChapter(sceneName);
        }

        private static async Task LoadChapter(string sceneName)
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(sceneName));

            // The arena is built in the bootstrap's _Ready and the spawners run inside it. Unity waited a
            // flat second for Start plus the spawners; the same wait is kept, because the HUD and the
            // zones are wired over the frames after that and 0.4s of settling follows in the callers.
            await TestContext.Runner.Seconds(1f);
        }

        private static PlayerController2D RequirePlayer()
        {
            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "The chapter has to spawn a player.");
            return player;
        }

        /// <summary>
        /// Unity's <c>player.gameObject</c>. The port made <see cref="PlayerMotor2D"/> the player body and
        /// hung the other components under it, so the node that moves is the body.
        /// </summary>
        private static CharacterBody2D PlayerBody(PlayerController2D player)
        {
            var body = player.GetComponentInParent<CharacterBody2D>();
            Assert.NotNull(body, "The player actor root should be the PlayerMotor2D body.");
            return body;
        }

        /// <summary>
        /// PORT CHANGE: Unity had to switch <c>Rigidbody2D.simulated</c> off across the move, because
        /// those bodies were Continuous-collision and a live teleport swept through 300 units of arena.
        /// A <see cref="CharacterBody2D"/> only sweeps inside <c>MoveAndSlide</c>, so writing
        /// <c>GlobalPosition</c> is already a teleport and the simulated dance has nothing to buy. The
        /// velocity is still cleared, or the body carries its old momentum out of the zone.
        /// </summary>
        private static void Teleport(CharacterBody2D body, Vector2 to)
        {
            body.GlobalPosition = to;
            body.Velocity = Vector2.Zero;
        }
    }
}
