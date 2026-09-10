using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Player;
using MyGame.UI;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Watches the chapter boss and turns its death into the gate being passed: the run is written, the
    /// game freezes, the victory cutscene plays and the panel that follows is the only route on.
    /// </summary>
    public sealed partial class GameplayVictoryController : Node
    {
        public const string GameplaySceneName = "GameplayScene";
        public const string TitleSceneName = "TitleScene";

        private const string VictoryCutsceneKey = "Victory";

        private IBossEncounter _boss;
        private GameplayHud _hud;
        private PlayerInputReceiver _input;
        private GameplayPlayerContext _playerContext;
        private PlayerController2D _player;
        private bool _hasWon;

        public bool HasWon => _hasWon;

        /// <summary>
        /// Takes the whole player context rather than only the controller, because passing a gate has to
        /// record the run that passed it - health, humanity and souls as they stand at the kill.
        /// </summary>
        public void Initialize(IBossEncounter boss, GameplayHud hud, PlayerInputReceiver input, GameplayPlayerContext player)
        {
            _hud = hud;
            _input = input;
            _playerContext = player;
            _player = player.Controller;
            RebindBoss(boss);
        }

        /// <summary>
        /// Points the victory hook at a different boss instance. Respawning enemies frees the boss this
        /// controller was listening to, so the replacement has to be handed back or the fight can be won
        /// with nothing happening.
        /// </summary>
        public void RebindBoss(IBossEncounter boss)
        {
            UnsubscribeFromBoss();

            // A freed node is not null through an interface reference - Godot has no overloaded == to
            // make one look null the way Unity did - so a boss killed by a respawn would arrive here
            // looking alive. Normalised once, here, through IsInstanceValid.
            _boss = boss is Node node && IsInstanceValid(node) ? boss : null;

            if (_boss == null)
                return;

            _boss.Defeated -= HandleVictory;
            _boss.Defeated += HandleVictory;
        }

        public override void _ExitTree()
        {
            UnsubscribeFromBoss();
            if (_hasWon && Mathf.IsZeroApprox(GameClock.TimeScale))
                GameClock.TimeScale = 1f;
        }

        private void UnsubscribeFromBoss()
        {
            if (_boss is Node node && IsInstanceValid(node))
                _boss.Defeated -= HandleVictory;
        }

        private void HandleVictory()
        {
            if (_hasWon)
                return;

            _hasWon = true;

            if (_input != null)
                _input.Enabled = false;

            if (_player != null)
            {
                _player.SetMoveInput(Vector2.Zero);
                _player.GetComponentInParent<PlayerActionController>()?.ResetActionState();
                _player.GetComponentInParent<PlayerMotor2D>()?.ResetMotion();
            }

            // No hit stop here, and reordering will not bring it back. CutsceneDirection 6 lists a 0.15s
            // freeze and calls it a one-line ordering problem; it is not. HitStopManager.Suppress sets
            // its pause timer to 0 in its own setter, and its process zeroes it again every frame it
            // stays up, so a request made anywhere in this handler is dead before it can tick. The zero
            // timescale below is a harder freeze than hit stop's 0.05 anyway - the beat has nothing left
            // to show.
            //
            // Shake survives: CameraShake runs on unscaled time and does not go through Suppress.
            if (CameraShake.Instance != null)
                CameraShake.Instance.TriggerShake(CameraShakePreset.BossPhase);

            // Defeated fires 1.5s after the death blow, so that hit stop is long gone - but the player
            // is still fighting whatever grunts remain, and a hit landing within one hit-stop window of
            // this frame would thaw the freeze. Take the timescale away from hit stop, don't race it.
            if (HitStopManager.Instance != null)
                HitStopManager.Instance.Suppress = true;

            // Before the freeze and before any panel: the gate is passed the moment the boss is down,
            // whatever the player does with the panel afterwards.
            RecordGatePassed();

            GameClock.TimeScale = 0f;

            // The panel is the only route to Restart and Title, so it hangs off the completion callback
            // rather than a clip: Skip runs Restore, Restore runs the callback, and a skipped cutscene
            // therefore still ends on the panel instead of a frozen scene with no input. Showing it
            // up front instead would stage the whole shot over a dead UI under the letterbox.
            CutsceneDirector director = CutsceneDirector.Instance;
            if (director != null)
                director.Play(VictoryCutsceneKey, ShowVictoryPanel);
            else
                ShowVictoryPanel();
        }

        /// <summary>
        /// Passing a gate opens the next one, so the panel's first button walks the road rather than
        /// restarting the chapter that was just beaten. At the end of the road there is nothing to open
        /// and it is a restart again, which is also what a scene outside the campaign gets.
        /// </summary>
        private void ShowVictoryPanel()
        {
            string next = ChapterRoute.Next(GameplayBuildShim.ActiveSceneName);
            string subtitle = _boss != null ? $"{_boss.BossName} has fallen" : null;

            if (string.IsNullOrEmpty(next))
            {
                _hud?.ShowVictory(RestartGame, ReturnToTitle, null, subtitle);
                return;
            }

            _hud?.ShowVictory(AdvanceToNextChapter, ReturnToTitle, Tr("UI_VICTORY_NEXT_GATE"), subtitle);
        }

        /// <summary>
        /// Records the gate as passed, with the run that passed it.
        /// </summary>
        /// <remarks>
        /// On the kill rather than on a button, because the kill is what opens the gate. Recording it in
        /// <see cref="AdvanceToNextChapter"/> instead meant a player who beat a boss and chose 타이틀로
        /// had not passed it - they came back to the same gate with the boss alive.
        ///
        /// Captured live rather than re-read from the slot. Reading it back and editing one field wrote
        /// whatever the last rest held, and on a first run with no rest that is a fresh record with zero
        /// souls - which <see cref="GameplaySaveBridge.Apply"/> then wrote over the wallet, because
        /// health and humanity have unset sentinels and souls did not.
        /// </remarks>
        private void RecordGatePassed()
        {
            string current = GameplayBuildShim.ActiveSceneName;
            if (!ChapterRoute.IsChapter(current))
                return;

            GameSaveData save = GameplaySaveBridge.Capture(_playerContext);

            // The gate that is now open, the furthest one ever reached, and - at the end of the road -
            // the Hard difficulty that finishing it earns. All three are one rule, so they live in
            // ChapterRoute rather than being spelled out again here.
            ChapterRoute.RecordGatePassed(save, current);

            GameSave.Write(save);
        }

        /// <summary>
        /// Opens the next chapter. The slot was already written when the boss died, so this only decides
        /// where the player goes now.
        /// </summary>
        public void AdvanceToNextChapter()
        {
            string next = ChapterRoute.Next(GameplayBuildShim.ActiveSceneName);
            if (string.IsNullOrEmpty(next))
            {
                RestartGame();
                return;
            }

            // The next chapter starts from the slot, or the player arrives at a new gate with the health
            // and souls of a fresh run rather than the ones they finished the last gate on.
            GameSave.LoadOnNextGameplayStart = true;

            LoadScene(next);
        }

        public void RestartGame()
        {
            LoadScene(GameplaySceneName);
        }

        public void ReturnToTitle()
        {
            LoadScene(TitleSceneName);
        }

        private void LoadScene(string sceneName)
        {
            GameClock.TimeScale = 1f;
            GetTree().ChangeSceneToFile(ChapterRoute.ScenePath(sceneName));
        }
    }
}
