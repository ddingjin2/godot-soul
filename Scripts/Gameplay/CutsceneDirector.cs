using System;
using System.Threading.Tasks;
using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Plays one of the four authored shots by name, driving the overlay and the camera through a small
    /// coded sequence.
    /// </summary>
    /// <remarks>
    /// <b>Timeline was dropped, and this is what replaced it.</b> Unity built each shot as a
    /// <c>TimelineAsset</c> under <c>Resources/Cutscenes</c>, loaded it with <c>Resources.Load</c>, bound
    /// its track outputs to scene objects by <see cref="CutsceneRole"/> at Play time and let a
    /// <c>PlayableDirector</c> evaluate it. Godot has no Timeline, no <c>PlayableDirector</c> and no
    /// <c>.playable</c> importer, and the four shipped files were never converted. Rebuilding a
    /// track/clip/binding engine on top of <c>AnimationPlayer</c> to run four sequences totalling eight
    /// seconds would be a new system, not a port - so the four are keyframe tables read straight out of
    /// the Unity YAML, authored now in <c>Resources/Design/CutsceneTuning.json</c>
    /// (<see cref="CutsceneTuningData"/>), and <c>BindTracks</c> is now <see cref="CutsceneBinder"/>
    /// answering "which node plays this role".
    /// <para>
    /// What survives unchanged: the keys, the durations, the keyframe times and values, the public
    /// surface (<see cref="Play"/>, <see cref="Skip"/>, <see cref="IsPlaying"/>,
    /// <see cref="HoldsInputLock"/>, <see cref="SkipAll"/>, <see cref="Instance"/>,
    /// <see cref="Binder"/>), and the rule that the completion callback is handed back on every path -
    /// including the ones that refuse to play anything.
    /// </para>
    /// <para>
    /// What is deliberately different: the Timeline clips' ease-in/ease-out blend curves are gone (they
    /// blended a clip against the empty track, which nothing here has), and the keyframe tangents are
    /// read as linear. Neither is visible in a two-second letterbox.
    /// </para>
    /// </remarks>
    public sealed partial class CutsceneDirector : Node
    {
        public const string ObjectName = "CutsceneDirector";

        /// <summary>Kept for the log lines and for the callers that still name shots by resource path.</summary>
        public const string ResourceFolder = "Cutscenes/";

        /// <summary>
        /// PlayMode agents call <c>PlayerController2D</c> directly instead of going through
        /// <see cref="PlayerInputReceiver"/>, so locking input does not stop them - they would keep
        /// playing through the shot and fight the sequence for the camera. The agent driver raises this
        /// so every <see cref="Play"/> resolves immediately instead.
        /// </summary>
        public static bool SkipAll;

        public static CutsceneDirector Instance { get; private set; }

        private readonly CutsceneBinder _binder = new();
        private CutsceneOverlay _overlay;
        private GameplayPauseController _pause;
        private Action _onComplete;
        private string _playingKey;
        private bool _isPlaying;
        private bool _inputWasEnabled;
        private Vector2 _cameraZoomBeforeShot = Vector2.One;

        /// <summary>
        /// Bumped by <see cref="Restore"/> and by <see cref="Skip"/>. A sequence still awaiting a frame
        /// compares it after every await and abandons itself if it no longer matches - which is how a
        /// skip stops a shot mid-way, and how a shot survives outliving the node that started it.
        /// </summary>
        private int _generation;

        public CutsceneBinder Binder => _binder;
        public bool IsPlaying => _isPlaying;

        /// <summary>
        /// True while a shot is holding the player's controls and intends to hand them back itself.
        /// </summary>
        /// <remarks>
        /// The other half of the arbitration in <see cref="Restore"/>. That one stops a shot ending from
        /// reviving input under an open pause menu; this one stops <c>GameplayPauseController</c> resuming
        /// input out from under a shot that is still running. A field two systems write needs both sides to
        /// ask, not one. False for a shot played with <c>lockInput: false</c> - the death cutscene never
        /// took the controls, so resuming is not taking anything from it.
        /// </remarks>
        public bool HoldsInputLock => _isPlaying && _inputWasEnabled;

        // ------------------------------------------------------------------------------------------
        // The four shots live in CutsceneTuning.json, one keyframe table per Timeline track, read out of
        // the matching Assets/_Project/Resources/Cutscenes/*.playable in the Unity source. Tracks that
        // ran at the same time are separate curves awaited together, which is what a Timeline track
        // *was*. The camera track animated "orthographic size" on the Main Camera; here it is a fraction
        // of the size the shot found the camera at, so a chapter that frames its room wider than the
        // shipped 6.8 gets the same push rather than a snap to the shipped number.
        // ------------------------------------------------------------------------------------------
        private static CutsceneTuningData Tuning => CutsceneTuningData.Shared;

        /// <summary>
        /// A null overlay would take the sequence table down with it, so the tables address the overlay
        /// through this: a shot played before the overlay exists writes into a throwaway one instead of
        /// throwing halfway through a step.
        /// </summary>
        private CutsceneOverlay Overlay => _overlay ??= CutsceneOverlay.Create();

        /// <summary>
        /// The director <c>Scenes/World/GameplayShell.tscn</c> authors, with the overlay the bootstrap
        /// just built bound onto it. Was <c>Create</c>, which made the node here; the shell owns the
        /// node and its <c>process_mode</c> since K7, and this is the binding half that is left.
        /// </summary>
        /// <remarks>
        /// Unity added a PlayableDirector and a SignalReceiver here. Both are gone with Timeline: there
        /// is no graph to run, and the Signal Tracks that routed through the receiver to reach
        /// <see cref="CameraShake"/> / <c>HitStopManager</c> were all at t=0.00, which is now the call
        /// site in <see cref="GameplayCutsceneTriggers"/>.
        /// <para>
        /// The director readies before the bootstrap does, so <see cref="Instance"/> is already set when
        /// this is called. Nothing plays a shot in between, which is what keeps <see cref="Overlay"/>'s
        /// lazy fallback from building a throwaway overlay ahead of this line.
        /// </para>
        /// </remarks>
        public static CutsceneDirector Bind(CutsceneOverlay overlay)
        {
            CutsceneDirector director = Instance ?? SceneQuery.FindFirst<CutsceneDirector>();

            if (director == null)
            {
                GD.PushError($"CutsceneDirector: the scene authors no '{ObjectName}'; Scenes/World/GameplayShell.tscn carries one. No cutscene can play.");
                return null;
            }

            director._overlay = overlay;
            return director;
        }

        /// <summary>Plays the shot named <paramref name="key"/>, or hands back at once if it has none.</summary>
        /// <param name="key">One of the keys in the sequence table - the Unity resource names, unchanged.</param>
        /// <param name="onComplete">Runs when the shot ends, is skipped, or turns out not to exist.</param>
        /// <param name="lockInput">
        /// False stages the shot without taking the player's controls. The death cutscene needs it:
        /// spirit form is a 3.0s window the player is meant to act in, and locking 1.2s of it away is
        /// 40% of the reprieve. Camera follow is disabled either way - the sequence drives the rig and
        /// the two fight over it otherwise.
        /// </param>
        public void Play(string key, Action onComplete = null, bool lockInput = true)
        {
            if (_isPlaying)
            {
                // Refusing the shot must never refuse the continuation with it. GameplayVictoryController
                // hangs the victory panel off this callback, and the panel is the only route to Restart and
                // Title - dropping it strands the player in a frozen scene with no input and no pause menu.
                // Synchronous, like the missing-shot path below: the caller is entitled to assume that a
                // Play which did not start anything has already handed the continuation back.
                GD.PushWarning("[Cutscene] Ignoring '" + key + "'; '" + _playingKey + "' is still running.");
                onComplete?.Invoke();
                return;
            }

            CutsceneShot shot = Tuning?.Shot(key);
            if (shot == null)
            {
                // A missing shot is a missing shot, not a stuck game: clear the overlay, hand the caller
                // its continuation back and carry on. The reset is not optional - a caller that staged
                // the fade before calling this (GameplayEnter does) would otherwise leave the screen
                // black for good, with no input left to fix it.
                GD.PushWarning("[Cutscene] No sequence named '" + ResourceFolder + key + "'; skipping it.");
                _overlay?.ResetToNeutral();
                onComplete?.Invoke();
                return;
            }

            if (SkipAll)
            {
                _overlay?.ResetToNeutral();
                _ = InvokeNextFrame(onComplete);
                return;
            }

            _onComplete = onComplete;
            _playingKey = key;
            _isPlaying = true;
            Lock(lockInput);

            _ = Run(shot, ++_generation);
        }

        public void Skip()
        {
            if (!_isPlaying)
                return;

            // Unity jumped the director to its duration and evaluated, which left every channel on its
            // last keyframe. Every one of the four shots ends on neutral - fade 0, bars 0, the camera
            // back at its resting size - so Restore, which writes exactly that, *is* the evaluation at
            // t=duration. CutsceneTuningJson_EveryShotEndsOnNeutral keeps the file to that.
            Restore();
        }

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }

            Instance = this;
        }

        public override void _ExitTree()
        {
            // A scene reload mid-cutscene would otherwise leave the next scene's input receiver disabled -
            // the same class of defence GameplayVictoryController puts around the timescale.
            Restore();

            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------------------------------
        // Steps
        // ------------------------------------------------------------------------------------------

        private async Task Run(CutsceneShot shot, int generation)
        {
            await Task.WhenAll(
                Curve(Overlay.SetFade, shot.fade),
                Curve(Overlay.SetLetterbox, shot.letterbox),
                Curve(SetCameraSize, shot.cameraSize));

            // The sequence may have outlived this node, or a skip may already have restored it.
            if (IsInstanceValid(this) && _generation == generation)
                Restore();
        }

        /// <summary>
        /// Plays one channel of a shot: walks unscaled time from the first keyframe to the last, writing
        /// the interpolated value into <paramref name="apply"/> every frame. One of these is one Timeline
        /// track.
        /// </summary>
        /// <remarks>
        /// Unscaled, because the victory cutscene has to run on top of the zero timescale
        /// <c>GameplayVictoryController</c> sets - the same reason Unity ran its director on
        /// <c>DirectorUpdateMode.UnscaledGameTime</c>. Interpolation is linear; the authored tangents
        /// were flat-ish and the difference does not read at these durations.
        /// </remarks>
        private async Task Curve(Action<float> apply, CutsceneKeyframe[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;

            int generation = _generation;
            apply(keys[0].value);

            float end = keys[^1].time;
            for (float elapsed = 0f; elapsed < end; elapsed += GameClock.UnscaledDeltaTime)
            {
                if (!await NextFrame(generation))
                    return;

                apply(Sample(keys, elapsed));
            }

            apply(keys[^1].value);
        }

        private static float Sample(CutsceneKeyframe[] keys, float at)
        {
            if (at <= keys[0].time)
                return keys[0].value;

            for (int i = 1; i < keys.Length; i++)
            {
                if (at > keys[i].time)
                    continue;

                float span = keys[i].time - keys[i - 1].time;
                float t = span > 0f ? (at - keys[i - 1].time) / span : 1f;
                return Mathf.Lerp(keys[i - 1].value, keys[i].value, t);
            }

            return keys[^1].value;
        }

        /// <summary>
        /// Waits a frame and reports whether the shot that asked is still the shot that is running. False
        /// once a skip, a restore or the node's own destruction has taken it over - which is the
        /// cancellation every step checks after every await.
        /// </summary>
        private async Task<bool> NextFrame(int generation)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return IsInstanceValid(this) && _isPlaying && _generation == generation;
        }

        private static async Task InvokeNextFrame(Action onComplete)
        {
            // Frame-based, so it still ticks under the zero timescale a victory cutscene would run on.
            SceneTree tree = Engine.GetMainLoop() as SceneTree;
            if (tree != null)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            onComplete?.Invoke();
        }

        // ------------------------------------------------------------------------------------------
        // Channels
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The camera channel. Unity animated <c>Camera.orthographicSize</c> - the visible half-height in
        /// world units - on the Main Camera under the rig. Godot's Camera2D has no such property and the
        /// file authors a fraction of the resting size, so this is the zoom <see cref="Lock"/> found,
        /// divided: half the size is twice the zoom. No <c>World.Ppu</c> - a ratio has no unit.
        /// </summary>
        private void SetCameraSize(float fractionOfRest)
        {
            Camera2D camera = FindCamera();
            if (camera == null || fractionOfRest <= 0f)
                return;

            camera.Zoom = _cameraZoomBeforeShot / fractionOfRest;
        }

        /// <summary>The camera under the rig, or whatever camera is current when there is no rig.</summary>
        private Camera2D FindCamera()
        {
            Node2D rig = _binder.Get2D(CutsceneRole.CameraRig);
            return rig?.GetComponent<Camera2D>() ?? GetViewport()?.GetCamera2D();
        }

        // ------------------------------------------------------------------------------------------
        // Locking
        // ------------------------------------------------------------------------------------------

        private void Lock(bool lockInput)
        {
            // Restore reads this to decide whether to hand control back, so leaving it false is what
            // keeps a no-lock shot from switching on a receiver it never switched off.
            _inputWasEnabled = false;

            PlayerInputReceiver input = lockInput ? FindInput() : null;
            if (input != null)
            {
                // Record first. If the game is already paused the receiver is off, and switching it back
                // on when the cutscene ends would hand control back mid-pause.
                _inputWasEnabled = input.Enabled;
                input.Enabled = false;
            }

            Camera2D camera = FindCamera();
            _cameraZoomBeforeShot = camera != null ? camera.Zoom : Vector2.One;

            GameplayCameraFollow2D follow = FindFollow();
            follow?.SetProcess(false);
        }

        private void Restore()
        {
            if (!_isPlaying)
                return;

            _isPlaying = false;

            // Anything still awaiting a frame belongs to the shot that just ended; this is what tells it
            // so. It also covers the case Unity's Stop() covered by raising `stopped` synchronously.
            _generation++;

            // Both halves are needed. The flag says the shot is the one that took the controls; the live
            // read says nothing has taken them since. A pause opened after Play started fails the second.
            if (_inputWasEnabled && !IsPaused())
            {
                PlayerInputReceiver input = FindInput();
                if (input != null)
                    input.Enabled = true;
            }

            GameplayCameraFollow2D follow = FindFollow();
            follow?.SetProcess(true);

            // The zoom the shot borrowed, handed back exactly as it was found. Cheaper and safer than
            // recomputing it from the scene defaults, which a chapter may have overridden.
            Camera2D camera = FindCamera();
            if (camera != null)
                camera.Zoom = _cameraZoomBeforeShot;

            _overlay?.ResetToNeutral();

            Action onComplete = _onComplete;
            _onComplete = null;
            _playingKey = null;
            onComplete?.Invoke();
        }

        /// <summary>
        /// Whether the pause menu owns the input receiver right now. <see cref="Lock"/> snapshots the
        /// receiver at Play time, and that snapshot is stale the moment a pause opens mid-shot: restoring
        /// on it alone hands the controls back with the menu still up.
        /// </summary>
        /// <remarks>
        /// Found lazily rather than wired, because <see cref="Restore"/> also runs from
        /// <see cref="_ExitTree"/> - the controller may already be torn down there, and finding nothing
        /// deliberately reads as "not paused". A live game under a pause menu can still be escaped out of;
        /// a game that hands back no input at all cannot be recovered by the player.
        /// </remarks>
        private bool IsPaused()
        {
            if (!IsInstanceValid(_pause))
                _pause = SceneQuery.FindFirst<GameplayPauseController>();

            return IsInstanceValid(_pause) && _pause.IsPaused;
        }

        private PlayerInputReceiver FindInput()
        {
            return _binder.TryGet(CutsceneRole.Player, out Node player)
                ? player.GetComponentInChildren<PlayerInputReceiver>()
                : null;
        }

        private GameplayCameraFollow2D FindFollow()
        {
            return _binder.TryGet(CutsceneRole.CameraRig, out Node rig)
                ? rig.GetComponent<GameplayCameraFollow2D>()
                : null;
        }
    }
}
