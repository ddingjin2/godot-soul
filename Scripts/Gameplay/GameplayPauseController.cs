using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.UI;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Escape stops the game and puts the pause menu up. Freezing is <see cref="GameClock.TimeScale"/>
    /// plus disabling the input receiver, the same pair <see cref="GameplayVictoryController"/> uses: a
    /// zero timescale stops every timer but not _Process, so a button held during the pause would still
    /// buffer an action for the moment play resumes.
    /// </summary>
    public sealed partial class GameplayPauseController : Node
    {
        private GameplayHud _hud;
        private PlayerInputReceiver _input;
        private GameplayVictoryController _victory;
        private GameplayPlayerContext _player;
        private bool _isPaused;

        public bool IsPaused => _isPaused;

        public void Initialize(GameplayHud hud, PlayerInputReceiver input, GameplayVictoryController victory, GameplayPlayerContext player)
        {
            _hud = hud;
            _input = input;
            _victory = victory;
            _player = player;
        }

        public override void _Ready()
        {
            // The one node that must keep reading Escape while everything else is frozen - otherwise the
            // pause menu could be opened but never closed. The HUD it drives already runs on Always too.
            ProcessMode = ProcessModeEnum.Always;
        }

        public override void _Process(double delta)
        {
            // The victory panel already owns the timescale and its own buttons; pausing on top of it
            // would leave two menus fighting over who restores play.
            if (_victory != null && _victory.HasWon)
                return;

            // The gate travel panel for the same reason, one panel later. It is full-screen and opaque,
            // and Escape is read here through GameplayInput rather than through the input receiver the
            // portal disables - so without this the pause menu opens invisibly underneath it.
            if (GateTravelZone.AnyPanelOpen)
                return;

            if (GameplayInput.PausePressed)
                SetPaused(!_isPaused);
        }

        public override void _ExitTree()
        {
            // Leaving the scene paused would carry a zero timescale into whatever loads next.
            if (!_isPaused)
                return;

            GameClock.TimeScale = 1f;
            if (HitStopManager.Instance != null)
                HitStopManager.Instance.Suppress = false;
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;

            // Hit stop drives the timescale too. Without handing it the freeze first, its _Process would
            // write 1 back on the very next frame and the pause would never take.
            if (HitStopManager.Instance != null)
                HitStopManager.Instance.Suppress = paused;

            GameClock.TimeScale = paused ? 0f : 1f;

            // Pausing always takes the controls. Resuming only hands them back if nothing else is holding
            // them: a cutscene that locked input restores it in its own Restore, and switching it on here
            // would put the player in control under a letterboxed screen for the rest of the shot.
            if (_input != null && (paused || !HoldsCutsceneLock()))
                _input.Enabled = !paused;

            _hud?.SetPauseVisible(paused, Resume, SaveGame, ReturnToTitle);
        }

        /// <summary>
        /// Whether a cutscene is currently holding the input receiver and will hand it back itself.
        /// Absent director, absent lock: a scene with no cutscene layer resumes exactly as it always did.
        /// </summary>
        private static bool HoldsCutsceneLock()
        {
            CutsceneDirector director = CutsceneDirector.Instance;
            return director != null && director.HoldsInputLock;
        }

        public void Resume()
        {
            SetPaused(false);
        }

        public void SaveGame()
        {
            GameSave.Write(GameplaySaveBridge.Capture(_player));
            _hud?.ShowPauseSaved();
        }

        public void ReturnToTitle()
        {
            GameClock.TimeScale = 1f;
            GetTree().ChangeSceneToFile(ChapterRoute.ScenePath(GameplayVictoryController.TitleSceneName));
        }
    }
}
