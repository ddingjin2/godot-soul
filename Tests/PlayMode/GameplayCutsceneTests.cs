using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// The cutscene layer takes three things away from the player - input, the camera and the zoom - and
    /// has to give all three back. Phase 2 wired the triggers, so a shot now runs on scene load, on the
    /// boss intro, on death and on victory; these guard the seams where a shot hands control back, and the
    /// one where it refuses to run at all.
    /// </summary>
    /// <remarks>
    /// PORT - TIMELINE IS GONE. Unity loaded a <c>.playable</c> per shot, bound its track outputs by
    /// <see cref="CutsceneRole"/> and let a <c>PlayableDirector</c> evaluate it.
    /// <see cref="CutsceneDirector"/> is a coded <c>async</c> sequence table under the same four keys, so
    /// every assertion here is about the director's observable behaviour - a shot plays, it can be
    /// skipped, it completes, it binds the right roles, it hands input, camera and zoom back - and none
    /// about tracks, clips or a playable graph. Three Unity behaviours had to be retargeted rather than
    /// kept verbatim; each is marked PORT - RETARGETED at its call site:
    /// <list type="bullet">
    /// <item>the "the .playable has not been generated yet" <c>Assert.Ignore</c> guard that every shot
    /// test opened with, and the <c>Resources.Load</c> that backed it: a coded sequence cannot be
    /// missing, so each one becomes an assertion that Play actually started the shot;</item>
    /// <item><c>boss.IntroStarted.Invoke()</c> - a C# <c>event</c> cannot be raised from outside the
    /// class that declares it, so the respawn test waits for the boss's own arrival to stage the shot,
    /// which is the thing the Unity call was standing in for;</item>
    /// <item><c>Behaviour.enabled</c> on the camera follow, which has no Godot counterpart: for a node
    /// whose whole job is <c>_Process</c>, "enabled" is <c>IsProcessing()</c> - the same reading
    /// <see cref="CutsceneRigMove"/> itself takes.</item>
    /// </list>
    /// <para>
    /// UNITS: every authored distance below is converted. Rig offsets and sentinels go through
    /// <c>World.V</c> / <c>World.U</c> with the vertical sign flipped; <c>orthographicSize</c> has no
    /// Godot property at all, so <see cref="OrthographicSizeOf"/> reads the camera's <c>Zoom</c> back
    /// into Unity units and every zoom assertion stays the number and the tolerance Unity wrote.
    /// Durations, alphas and counts are unscaled.
    /// </para>
    /// </remarks>
    public sealed class GameplayCutsceneTests
    {
        private const string GameplaySceneName = "GameplayScene";

        // Authored shots; they are entries in CutsceneDirector's sequence table rather than .playable
        // assets, but the keys are the ones Unity used.
        private const string BossIntroKey = "BossIntro";

        // The one shot played with lockInput: false. Taken off the product const so a rename cannot leave
        // these tests quietly exercising a key nothing plays.
        private const string PlayerDeathKey = GameplayCutsceneTriggers.PlayerDeathKey;

        // A key that can never resolve.
        private const string MissingKey = "__NoSuchCutscene";

        private float _startTimeScale;
        private bool _startSkipAll;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;

            // The runtime agent driver raises SkipAll for the whole time an agent scenario runs, and this
            // static outlives the scene, so arm it here rather than assume it is down.
            _startSkipAll = CutsceneDirector.SkipAll;
            CutsceneDirector.SkipAll = false;
        }

        [TearDown]
        public void TearDown()
        {
            GameClock.TimeScale = _startTimeScale;
            CutsceneDirector.SkipAll = _startSkipAll;
        }

        /// <summary>
        /// Red when the camera rig split is quietly undone - the follow moved back onto the camera, the
        /// shake onto the rig, or the current-camera role onto the parent. That leaves three writers on
        /// one transform again, where the shake's subtract-then-re-add runs against whoever wrote last.
        /// </summary>
        /// <remarks>
        /// PORT: Unity's MainCamera tag is Godot's "current camera", which is what
        /// <c>GetViewport().GetCamera2D()</c> answers - so the tag assertion becomes "the current camera
        /// is still the child, not the rig". The rig <em>is</em> the <see cref="GameplayCameraFollow2D"/>
        /// node here rather than carrying one as a component, because a Godot child cannot move its
        /// parent; <c>GetComponent</c> answers "this node or a direct child", so the reads below are
        /// unchanged.
        /// </remarks>
        [Test]
        public async Task CameraRig_OwnsTheFollowWhileTheCameraKeepsTheShakeAndTheTag()
        {
            await LoadGameplayScene();

            Camera2D cam = TestContext.Tree.Root.GetViewport().GetCamera2D();
            Assert.NotNull(cam, "The current camera must still resolve: it stays the child, not the rig.");

            var rig = cam.GetParent() as Node2D;
            Assert.NotNull(rig, "The camera should hang under a rig parent after bootstrap.");
            Assert.AreEqual(GameplaySystemBootstrapper.CameraRigObjectName, rig.Name.ToString(),
                "The rig is looked up by name, so renaming it unbinds the CameraRig cutscene role too.");

            Assert.NotNull(rig.GetComponent<GameplayCameraFollow2D>(),
                "Follow writes a world position, so it belongs on the rig.");
            Assert.IsNull(cam.GetComponent<GameplayCameraFollow2D>(),
                "A follow on the camera would fight the shake's offset every frame.");

            Assert.NotNull(cam.GetComponent<CameraShake>(),
                "Shake writes the camera's own offset, so it belongs on the camera under the rig.");
            Assert.IsNull(rig.GetComponent<CameraShake>(),
                "A shake on the rig would be stomped by the follow instead of riding on top of it.");

            // Unity asserted a local magnitude under 0.001 metres; World.U makes that 0.1 px.
            Assert.Less(cam.Position.Length(), World.U(0.001f),
                "The camera carries no offset of its own at rest; the rig holds the framing.");
        }

        /// <summary>
        /// Red when a finished cutscene leaves the player unable to move or the camera stuck - and when a
        /// shot whose key resolves to nothing locks the game instead of handing the caller straight back.
        /// The missing-shot path is a real shipping path: any caller may name a shot that does not exist.
        /// </summary>
        [Test]
        public async Task Cutscene_HandsInputAndFollowBackWhenItEnds_AndNeverTakesThemForAMissingShot()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            var input = SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.NotNull(input, "Gameplay bootstrap should spawn a player with an input receiver.");
            var follow = SceneQuery.FindFirst<GameplayCameraFollow2D>();
            Assert.NotNull(follow, "Gameplay bootstrap should put a follow on the camera rig.");

            bool missingCompleted = false;
            director.Play(MissingKey, () => missingCompleted = true);

            Assert.IsTrue(missingCompleted, "A missing shot should hand the caller its continuation back at once.");
            Assert.IsFalse(director.IsPlaying, "A missing shot should not start anything.");
            Assert.IsTrue(input.Enabled, "A missing shot must never lock input - that is a stuck game over a missing sequence.");
            Assert.IsTrue(follow.IsProcessing(), "A missing shot must never park the camera follow.");

            bool completed = false;
            director.Play(BossIntroKey, () => completed = true);

            // PORT - RETARGETED. Unity opened here with an Assert.Ignore for the case where
            // Resources/Cutscenes/BossIntro.playable had not been generated yet. The shot is a row in
            // CutsceneDirector's sequence table now, so it cannot be absent and the guard becomes an
            // assertion. Same for every sibling test below.
            Assert.IsTrue(director.IsPlaying,
                BossIntroKey + " is a coded sequence in this port, so Play has to start it.");

            Assert.IsFalse(input.Enabled, "Starting a cutscene should take input away.");
            Assert.IsFalse(follow.IsProcessing(), "Starting a cutscene should take the camera away from the follow.");

            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => !director.IsPlaying, 5f),
                "The cutscene should reach its end and stop on its own.");

            Assert.IsTrue(completed, "Finishing a cutscene should call the caller's continuation.");
            Assert.IsTrue(input.Enabled, "A finished cutscene must give input back, or the player cannot move again.");
            Assert.IsTrue(follow.IsProcessing(), "A finished cutscene must give the camera back to the follow.");
        }

        /// <summary>
        /// Red when a cutscene ending during a pause or a victory freeze thaws the game - the director
        /// restoring input it never had rather than only what it took. Also red if the director's curves
        /// drop off unscaled time: the wait below runs at a zero timescale, where a scaled sequence never
        /// advances to its end.
        /// </summary>
        [Test]
        public async Task Cutscene_EndingWhilePaused_LeavesInputOff()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            var input = SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.NotNull(input, "Gameplay bootstrap should spawn a player with an input receiver.");
            var pause = SceneQuery.FindFirst<GameplayPauseController>();
            Assert.NotNull(pause, "Gameplay scene should own a pause controller.");

            pause.SetPaused(true);
            Assert.IsFalse(input.Enabled, "Setup: pausing should already have taken input away.");
            Assert.AreEqual(0f, GameClock.TimeScale, "Setup: pausing should stop time.");

            director.Play(BossIntroKey);

            // PORT - RETARGETED: was an Assert.Ignore on the missing .playable.
            Assert.IsTrue(director.IsPlaying,
                BossIntroKey + " is a coded sequence in this port, so Play has to start it.");

            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => !director.IsPlaying, 5f),
                "A cutscene has to run out on top of a frozen game, which is what GameClock.UnscaledDeltaTime buys.");

            Assert.IsFalse(input.Enabled,
                "A cutscene that ends during a pause must not hand control back - it only restores what it took.");
            Assert.IsTrue(pause.IsPaused, "The pause itself should survive the cutscene.");

            pause.SetPaused(false);
        }

        /// <summary>
        /// Red when the director restores input off its Play-time snapshot alone. The pause here opens
        /// <em>after</em> the shot started, so <c>_inputWasEnabled</c> is true and says nothing about the
        /// menu that has since taken the controls - only a live read of the pause controller does. Without
        /// it the shot ends by handing the player control back with the pause menu still up.
        /// </summary>
        [Test]
        public async Task Cutscene_PausedAfterItStarted_StillLeavesInputOffWhenItEnds()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            var input = SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.NotNull(input, "Gameplay bootstrap should spawn a player with an input receiver.");
            var pause = SceneQuery.FindFirst<GameplayPauseController>();
            Assert.NotNull(pause, "Gameplay scene should own a pause controller.");

            Assert.IsTrue(input.Enabled, "Setup: an idle scene leaves the player their controls.");

            director.Play(BossIntroKey);

            // PORT - RETARGETED: was an Assert.Ignore on the missing .playable.
            Assert.IsTrue(director.IsPlaying,
                BossIntroKey + " is a coded sequence in this port, so Play has to start it.");

            Assert.IsFalse(input.Enabled, "Setup: starting the shot takes input away and records that it did.");

            // No await between Play and here on purpose: the shot has to still be running when the pause
            // opens, or this measures the already-covered pause-before-Play case instead.
            pause.SetPaused(true);

            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => !director.IsPlaying, 5f),
                "A cutscene has to run out on top of a frozen game, which is what GameClock.UnscaledDeltaTime buys.");

            Assert.IsFalse(input.Enabled,
                "A pause opened during a shot owns the controls when it ends. Restoring the Play-time " +
                "snapshot instead revives the player on top of the pause menu.");
            Assert.IsTrue(pause.IsPaused, "The pause itself should survive the cutscene.");

            pause.SetPaused(false);
        }

        /// <summary>
        /// Red when Resume hands the controls back out from under a shot that is still running. This is the
        /// other side of the same two-writer field: the director stopped reviving input under an open menu,
        /// and the menu has to stop reviving input under a running shot. The shot keeps the lock until its
        /// own <c>Restore</c>, which is the second half asserted here - a resume that only postpones the
        /// revival to the shot's end would leave the player stuck instead.
        /// </summary>
        [Test]
        public async Task Cutscene_ResumedDuringALockedShot_KeepsInputOffUntilTheShotEnds()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            var input = SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.NotNull(input, "Gameplay bootstrap should spawn a player with an input receiver.");
            var pause = SceneQuery.FindFirst<GameplayPauseController>();
            Assert.NotNull(pause, "Gameplay scene should own a pause controller.");

            director.Play(BossIntroKey);

            // PORT - RETARGETED: was an Assert.Ignore on the missing .playable.
            Assert.IsTrue(director.IsPlaying,
                BossIntroKey + " is a coded sequence in this port, so Play has to start it.");

            Assert.IsFalse(input.Enabled, "Setup: a locked shot takes the controls.");

            // Both sides of the pause, with no await: the shot has to still be running when Resume lands,
            // which is the whole case. An await here would let the shot end first and assert nothing.
            pause.SetPaused(true);
            pause.SetPaused(false);

            Assert.IsFalse(pause.IsPaused, "Setup: Resume should actually lift the pause.");
            Assert.IsFalse(input.Enabled,
                "Resuming out of a pause opened mid-shot must not take the controls back off the cutscene - " +
                "that puts the player in control under a letterboxed screen for the rest of the shot.");

            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => !director.IsPlaying, 5f),
                "The shot should still reach its end on its own after the pause is lifted.");

            Assert.IsTrue(input.Enabled,
                "The shot owns the hand-back, so its end has to deliver it. A resume that only defers the " +
                "revival leaves the player frozen with no menu up and nothing left to restore them.");
        }

        /// <summary>
        /// Red when the resume arbitration widens from "a shot holds the lock" to "a shot is playing". The
        /// death cutscene runs with <c>lockInput: false</c> on purpose - spirit form is a 3.0s window the
        /// player is meant to act in - so it holds nothing, and a pause opened across it has to resume the
        /// player normally. Refusing there freezes the rest of the window with no menu and no shot left to
        /// hand anything back.
        /// </summary>
        [Test]
        public async Task Cutscene_ResumedDuringTheDeathShot_HandsInputBackAtOnce()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            var input = SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.NotNull(input, "Gameplay bootstrap should spawn a player with an input receiver.");
            var pause = SceneQuery.FindFirst<GameplayPauseController>();
            Assert.NotNull(pause, "Gameplay scene should own a pause controller.");

            director.Play(PlayerDeathKey, lockInput: false);

            // PORT - RETARGETED: was an Assert.Ignore on the missing .playable.
            Assert.IsTrue(director.IsPlaying,
                PlayerDeathKey + " is a coded sequence in this port, so Play has to start it.");

            Assert.IsTrue(input.Enabled,
                "Setup: the death shot stages over the player without taking the controls.");

            pause.SetPaused(true);
            Assert.IsFalse(input.Enabled, "Setup: pausing still takes the controls whatever is playing.");

            pause.SetPaused(false);

            Assert.IsTrue(input.Enabled,
                "A shot that never locked input is not holding it, so Resume has to give the controls back " +
                "at once - the spirit-form window is the player's to act in.");

            director.Skip();
        }

        /// <summary>
        /// Red when a respawn hands the new boss to the victory hook but not to the cutscene layer. Both
        /// halves are checked: the <see cref="CutsceneRole.Boss"/> binding the intro resolves against, and
        /// the intro subscription that stages the shot at all. Either one left on the destroyed boss means
        /// every attempt after the first fights an intro with no shot behind it.
        /// </summary>
        /// <remarks>
        /// PORT - RETARGETED (the event half). Unity raised the hook by hand with
        /// <c>boss.IntroStarted.Invoke()</c>, which a <c>UnityEvent</c> allows and a C# <c>event</c> does
        /// not - only the declaring class can raise one. So this waits for the respawned boss to reach its
        /// own arrival instead, which is what the manual raise was standing in for: the boss fires
        /// <c>OnIntroStart</c> the first frame it knows a player, and a trigger still subscribed to the
        /// corpse leaves the director idle.
        /// <para>
        /// The Unity <c>Resources.Load</c> guard that Ignored this half when the .playable was missing is
        /// gone with Timeline; the sequence table always has BossIntro.
        /// </para>
        /// </remarks>
        [Test]
        public async Task RespawnedBoss_StagesItsIntro_OnBothTheBindingAndTheEvent()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            var respawner = SceneQuery.FindFirst<GameplayEnemyRespawner>();
            Assert.NotNull(respawner, "Gameplay scene should own an enemy respawner.");

            IBossEncounter firstBoss = respawner.Enemies.Boss;
            Assert.NotNull(firstBoss, "Setup: the arena spawns a boss for the intro to stage over.");

            respawner.RespawnEnemies();

            IBossEncounter boss = respawner.Enemies.Boss;
            Assert.NotNull(boss, "A respawn should put a boss back.");
            Assert.AreNotSame(firstBoss, boss,
                "Setup: the respawn rebuilds the boss rather than reusing it, which is why both hooks have to move.");

            Assert.IsTrue(director.Binder.TryGet(CutsceneRole.Boss, out Node bound),
                "A respawn must leave a live node under CutsceneRole.Boss, or the intro binds to nothing.");
            Assert.AreSame(boss.BossObject, bound,
                "The Boss role has to point at the boss that is alive now, not the corpse the respawn destroyed.");

            // PORT - RETARGETED: Unity raised boss.IntroStarted.Invoke() by hand. A C# event cannot be
            // raised from outside its class, so the shot has to be staged the way the game stages it -
            // the boss seeing the player. Nothing in the scene does that on its own: the player spawns at
            // x=0 and the boss at x=290 (SceneLayout.json), 290 units apart against an 8-unit
            // detectionRange, so WrathMiniBoss.DetectPlayer never finds anybody and the intro never
            // fires. Walking the player onto the boss is that missing step, and it exercises more of the
            // path than Unity's Invoke did: the new boss raises its own OnIntroStart, and only a trigger
            // that re-subscribed in RebindBoss turns it into a shot.
            Node2D player = director.Binder.Get2D(CutsceneRole.Player);
            Assert.NotNull(player, "Setup: the Player role has to resolve for the boss to have anyone to see.");
            player.GlobalPosition = boss.BossObject.GlobalPosition;

            // The director going busy is the only evidence the triggers followed the new boss rather than
            // staying subscribed to the destroyed one.
            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => director.IsPlaying, 5f),
                "A respawned boss's intro must stage its shot; a trigger still subscribed to the old boss " +
                "leaves the second attempt running the intro bare.");

            // Releases the intro hold the trigger took, so the boss is not left frozen for the teardown.
            director.Skip();
        }

        /// <summary>
        /// Red when <see cref="CutsceneDirector.Play"/> drops a caller's continuation because another shot
        /// is running. The continuation is not decoration: <c>GameplayVictoryController</c> hangs the
        /// victory panel off it, and the panel is the only route to Restart and Title. A player who dies
        /// inside the 1.5s victory window - which the death shot covers 1.2s of - lands on a refused Play,
        /// a zero timescale and no panel, and the pause menu is closed too once HasWon.
        /// </summary>
        [Test]
        public async Task Cutscene_HandsTheContinuationBack_EvenWhileAnotherShotIsRunning()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            director.Play(BossIntroKey);

            // PORT - RETARGETED: was an Assert.Ignore on the missing .playable.
            Assert.IsTrue(director.IsPlaying,
                BossIntroKey + " is a coded sequence in this port, so Play has to start it.");

            bool completed = false;
            director.Play(MissingKey, () => completed = true);

            Assert.IsTrue(completed,
                "A refused Play must still hand the caller its continuation back, or the victory panel is " +
                "lost to a frozen scene with no input and no pause menu.");

            director.Skip();
        }

        /// <summary>
        /// Red when a cutscene leaves the camera zoomed in - on completion or on skip. Nothing else in the
        /// game owns the framing, so a sequence that forgets its return beat, or a skip that stops short of
        /// the last keyframe, zooms the game in for the rest of the session and no input recovers it.
        /// </summary>
        /// <remarks>
        /// PORT: Godot's Camera2D has no <c>orthographicSize</c>; it has <c>Zoom</c>, and the director
        /// converts one to the other exactly as PORTING_GUIDE specifies.
        /// <see cref="OrthographicSizeOf"/> converts back, so the expected value and the 0.01 tolerance are
        /// the Unity numbers unchanged rather than a zoom nobody authored.
        /// </remarks>
        [Test]
        public async Task Cutscene_PutsTheZoomBackToTheSceneDefault_OnCompletionAndOnSkip()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            Camera2D cam = TestContext.Tree.Root.GetViewport().GetCamera2D();
            Assert.NotNull(cam, "The current camera must resolve for the zoom to have an owner at all.");

            // CameraOrthographicSize is the one GameplaySceneDefaults field left in Unity units, so this
            // number needs no conversion - OrthographicSizeOf brings the camera to meet it instead.
            float expected = GameplaySceneDefaults
                .CreateFromAsset(Res.Load<GameplaySceneDefaultsAsset>("Gameplay/SceneDefaults"))
                .CameraOrthographicSize;

            Assert.AreEqual(expected, OrthographicSizeOf(cam), 0.01f,
                "Setup: bootstrap frames the room at the scene default before any cutscene runs.");

            director.Play(BossIntroKey);

            // PORT - RETARGETED: was an Assert.Ignore on the missing .playable.
            Assert.IsTrue(director.IsPlaying,
                BossIntroKey + " is a coded sequence in this port, so Play has to start it.");

            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => !director.IsPlaying, 6f),
                "The shot should stop on its own.");

            Assert.AreEqual(expected, OrthographicSizeOf(cam), 0.01f,
                "A finished cutscene has to leave the zoom at the scene default; nothing else restores it.");

            director.Play(BossIntroKey);
            Assert.IsTrue(director.IsPlaying, "The second play should start - the sequence resolved once already.");

            // One frame of the shot first, so the skip has something to unwind rather than restoring a
            // camera the sequence never moved off its first keyframe.
            await TestContext.Runner.NextFrame();
            director.Skip();

            Assert.AreEqual(expected, OrthographicSizeOf(cam), 0.01f,
                "A skipped cutscene has to leave the zoom at the scene default too - the skip is the " +
                "evaluation of the last frame, and a sequence whose final value is not the default strands " +
                "the player zoomed in.");
        }

        /// <summary>
        /// Red when a rig ease outlives the cutscene that started it. <see cref="CutsceneRigMove"/> reads
        /// camera follow being switched back on as the signal it was released; if that check is lost, the
        /// rig has two writers again and the symptom - a rig that drifts or fights the follow - shows up
        /// nowhere near the cutscene that caused it.
        /// </summary>
        [Test]
        public async Task CameraRig_HasOnlyTheFollowWritingIt_AfterASkip()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            Camera2D cam = TestContext.Tree.Root.GetViewport().GetCamera2D();
            Assert.NotNull(cam, "The current camera must resolve to find the rig above it.");

            var rig = cam.GetParent() as Node2D;
            Assert.NotNull(rig, "The camera should hang under a rig parent after bootstrap.");
            var follow = rig.GetComponent<GameplayCameraFollow2D>();
            Assert.NotNull(follow, "The rig should carry the follow.");

            director.Play(BossIntroKey);

            // PORT - RETARGETED: was an Assert.Ignore on the missing .playable.
            Assert.IsTrue(director.IsPlaying,
                BossIntroKey + " is a coded sequence in this port, so Play has to start it.");

            // Long enough to outlive the shot several times over: the point is what the skip does to it.
            // Unity's target was +20 units in x and +20 units UP in y; +Y is down here, so the vertical
            // offset is subtracted. 30f is a duration and is not scaled.
            CutsceneRigMove.Play(
                rig,
                new Vector2(rig.GlobalPosition.X + World.U(20f), rig.GlobalPosition.Y - World.U(20f)),
                30f);

            director.Skip();
            Assert.IsTrue(follow.IsProcessing(), "A skip has to hand the rig back to the follow.");

            // The mover reads follow processing again to learn it was released, so give it the frames to
            // see it. Without this the test measures release latency instead of a leaked ease.
            await TestContext.Runner.NextFrame();
            await TestContext.Runner.NextFrame();

            follow.SetProcess(false);

            // Unity's (-99, -99) sentinel is in metres with +Y up; World.V converts and flips it. The z
            // Unity preserved has no counterpart - Godot 2D has no depth on a position.
            Vector2 sentinel = World.V(new Vector2(-99f, -99f));
            rig.GlobalPosition = sentinel;

            await TestContext.Runner.RealtimeSeconds(0.4f);

            // Unity's 0.01-unit tolerance is one pixel here.
            Assert.AreEqual(sentinel.X, rig.GlobalPosition.X, World.U(0.01f),
                "Nothing but the follow may write the rig once a cutscene is over - an ease still running " +
                "here is a second writer the rig split was made to end.");
            Assert.AreEqual(sentinel.Y, rig.GlobalPosition.Y, World.U(0.01f),
                "Nothing but the follow may write the rig once a cutscene is over.");

            follow.SetProcess(true);
        }

        /// <summary>
        /// A shot running when the scene goes away never reaches its own completion, so nothing on the
        /// normal path hands input back - <see cref="CutsceneDirector"/>'s <c>_ExitTree</c> is the only
        /// thing that does, and the static <c>Instance</c> plus a zero timescale both outlive the scene
        /// that set them. Red if a reload mid-shot leaves the next run frozen or unable to move, which is
        /// exactly what Restart from the victory panel does during a cutscene.
        /// </summary>
        [Test]
        public async Task SceneReload_DuringAShot_LeavesTheNextRunPlayable()
        {
            await LoadGameplayScene();

            CutsceneDirector director = FindDirector();
            director.Play(BossIntroKey);

            await TestContext.Runner.NextFrame();
            Assert.IsTrue(director.IsPlaying, "The boss intro should be running before the reload under test.");

            var input = SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.NotNull(input, "Gameplay scene should spawn a player input receiver.");
            Assert.IsFalse(input.Enabled, "A locked shot takes the controls; without that this test proves nothing.");

            await LoadGameplayScene();

            var reloadedInput = SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.NotNull(reloadedInput, "The reloaded scene should spawn its own input receiver.");
            Assert.IsTrue(reloadedInput.Enabled,
                "A reload during a locked shot must not carry the lock into the next run.");
            Assert.AreEqual(1f, GameClock.TimeScale, 0.001f,
                "A cutscene's zero timescale is engine-wide, so a reload that does not clear it freezes the next run.");
        }

        /// <summary>
        /// The camera's framing in the Unity units the assertions are written in. Godot's Camera2D has no
        /// orthographic size, so this inverts the conversion <see cref="CutsceneDirector"/> and
        /// <see cref="GameplaySystemBootstrapper.FrameCombatRoom"/> both apply:
        /// <c>zoom = viewportHeight / 2 / (size * Ppu)</c>.
        /// </summary>
        private static float OrthographicSizeOf(Camera2D cam)
        {
            float zoom = cam.Zoom.Y;
            if (zoom <= 0f)
                return 0f;

            return World.ToUnits(cam.GetViewportRect().Size.Y * 0.5f / zoom);
        }

        /// <summary>
        /// The shots are CutsceneTuning.json's now, and the file is the only copy: a missing shot takes
        /// the director's "no such sequence" path, so a typo in a key is a shot that silently never
        /// plays. The other thing a designer can break without a compile error is the neutral ending -
        /// Skip writes neutral rather than evaluating the last frame, so a channel that ends anywhere
        /// else strands the screen.
        /// UNITS: letterbox rows are UI pixels and cross unscaled; fade and cameraSize are unitless.
        /// </summary>
        [Test]
        public void CutsceneTuningJson_CarriesTheFourShots_AndEveryShotEndsOnNeutral()
        {
            CutsceneTuningData file = Res.LoadJson<CutsceneTuningData>("Design/" + CutsceneTuningData.FileName);
            Assert.NotNull(file, "CutsceneTuning.json should exist and parse.");

            foreach (string key in new[] { "GameplayEnter", BossIntroKey, "PlayerDeath", "Victory" })
            {
                CutsceneShot shot = file.Shot(key);
                Assert.NotNull(shot, key + " must be authored; the director plays nothing it cannot find.");

                AssertEndsOn(shot.fade, 0f, key + ".fade");
                AssertEndsOn(shot.letterbox, 0f, key + ".letterbox");
                AssertEndsOn(shot.cameraSize, 1f, key + ".cameraSize");
            }

            Assert.AreEqual(64f, file.OpeningLetterbox("GameplayEnter"), 0.001f,
                "The bootstrap stages the entry bars at the shot's own opening height (CutsceneDirection.md 4).");
        }

        private static void AssertEndsOn(CutsceneKeyframe[] rows, float neutral, string channel)
        {
            if (rows == null || rows.Length == 0)
                return;

            Assert.AreEqual(neutral, rows[^1].value, 0.001f,
                channel + " must end on neutral; Skip restores neutral, not the last keyframe.");
        }

        private static CutsceneDirector FindDirector()
        {
            var director = SceneQuery.FindFirst<CutsceneDirector>();
            Assert.NotNull(director, "Gameplay bootstrap should stand up the cutscene director.");
            return director;
        }

        private static async Task LoadGameplayScene()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            // Bootstrap plays the entry cutscene in _Ready, so for its whole length the director is busy
            // and input is off. Every test above reads the idle state; without this wait they would read
            // the entry shot's state instead.
            var director = SceneQuery.FindFirst<CutsceneDirector>();
            if (director != null)
            {
                Assert.IsTrue(await TestContext.Runner.WaitUntil(() => !director.IsPlaying, 6f),
                    "The entry cutscene has to end on its own after a scene load; nothing else hands input back.");
            }
        }
    }
}
