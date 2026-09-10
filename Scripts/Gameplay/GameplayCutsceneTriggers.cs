using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Fires the death and boss-intro cutscenes from the events that already announce them.
    /// </summary>
    /// <remarks>
    /// Both events live below this namespace - <c>Player</c> and <c>Enemy</c> do not reference
    /// <c>Gameplay</c> - so the wiring has to listen upward from here rather than the raisers calling
    /// the director. Shake and hit stop are triggered here too, not from the cutscene sequence: every
    /// one of those beats sits at t=0.00, which is this call site. (In Unity they were Timeline Signal
    /// Tracks at t=0.00, for the same reason and to the same effect.)
    /// </remarks>
    public sealed partial class GameplayCutsceneTriggers : Node
    {
        public const string PlayerDeathKey = "PlayerDeath";
        public const string BossIntroKey = "BossIntro";

        // The beats are CutsceneTuning.json's (CutsceneDirection.md 3 and 5): the rig eases to the boss
        // over bossIntroMoveDuration then holds - the hold needs no code, follow is off for the shot -
        // and on death it creeps deathMoveDistance toward the body between deathMoveDelay and
        // deathMoveDelay + deathMoveDuration. The distance is already pixels; Load() scaled it.
        private static CutsceneTuningData Tuning => CutsceneTuningData.Shared;

        private CutsceneDirector _director;
        private DeathStateController _death;
        private IBossEncounter _boss;

        public void Initialize(CutsceneDirector director, DeathStateController death, IBossEncounter boss)
        {
            Unsubscribe();

            _director = director;
            _death = death;

            if (_death != null)
                _death.OnDeath += HandlePlayerDeath;

            RebindBoss(boss);
        }

        /// <summary>
        /// Points both cutscene hooks at a different boss instance: the intro event this listens to, and
        /// the <see cref="CutsceneRole.Boss"/> binding the director resolves the rig move against.
        /// Respawning enemies frees the boss both were pointed at, so the replacement has to be handed
        /// back or the second attempt runs the intro with no shot behind it.
        /// </summary>
        /// <remarks>
        /// Registering a null target clears the role rather than leaving the dead boss behind it, which is
        /// what a respawn that produces no boss has to leave in the binder.
        /// </remarks>
        public void RebindBoss(IBossEncounter boss)
        {
            UnsubscribeFromBoss();

            // A freed node is not null to the interface: an interface reference is a plain C# reference,
            // so a boss killed by a respawn arrives here looking alive and throws on first use.
            // Normalised through IsInstanceValid once, here, rather than at each of the uses below -
            // the same defence the Unity source made with its Object comparison.
            _boss = IsLive(boss) ? boss : null;

            if (_boss != null)
                _boss.IntroStarted += HandleBossIntro;

            _director?.Binder.Register(CutsceneRole.Boss, _boss?.BossObject);
        }

        public override void _ExitTree()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_death != null)
                _death.OnDeath -= HandlePlayerDeath;

            UnsubscribeFromBoss();
        }

        private void UnsubscribeFromBoss()
        {
            if (IsLive(_boss))
                _boss.IntroStarted -= HandleBossIntro;
        }

        /// <summary>An encounter whose node is still in memory. Unity's <c>boss as Object != null</c>.</summary>
        private static bool IsLive(IBossEncounter boss)
        {
            return boss is Node node && IsInstanceValid(node);
        }

        private void HandlePlayerDeath()
        {
            if (_director == null)
                return;

            if (HitStopManager.Instance != null)
                HitStopManager.Instance.TriggerHitStop(Tuning.deathHitStop);

            // HeavyHit, not BossPhase: a body falling, not an arrival.
            if (CameraShake.Instance != null)
                CameraShake.Instance.TriggerShake(CameraShakePreset.HeavyHit);

            // Spirit form is a 3.0s window to act in and this shot is 1.2s of it, so it stages over the
            // player rather than taking the controls away. Follow still goes off inside Play.
            _director.Play(PlayerDeathKey, lockInput: false);

            // After Play, which is what disables follow: the ease reads the rig's live position as its
            // start, and follow still writing it would fight the move.
            MoveRigTowardPlayer();
        }

        /// <summary>
        /// Nudges the rig the last 0.6 units onto the body. Authored in code rather than on a clip
        /// because the player dies anywhere in the arena - an absolute clip would teleport the camera to a
        /// fixed coordinate on every death.
        /// </summary>
        private void MoveRigTowardPlayer()
        {
            Node2D rig = _director.Binder.Get2D(CutsceneRole.CameraRig);
            Node2D player = _director.Binder.Get2D(CutsceneRole.Player);
            if (rig == null || player == null)
                return;

            Vector2 from = rig.GlobalPosition;

            // Clamped, not scaled: when the rig is already inside the creep distance of the body the move
            // stops on the player instead of overshooting past them.
            Vector2 toPlayer = (player.GlobalPosition - from).LimitLength(Tuning.deathMoveDistance);

            CutsceneRigMove.Play(rig, from + toPlayer, Tuning.deathMoveDuration, Tuning.deathMoveDelay);
        }

        private void HandleBossIntro()
        {
            if (_director == null || !IsLive(_boss))
                return;

            // Synchronous, and before Play: the code that raised this reads the flag further down its own
            // body, and Play can complete inside this call - when the key is unknown, or when another
            // shot is already running and Play refuses this one. Either way the hold is released in the
            // same breath and the fight starts unstaged, which is why this needs no guard of its own.
            _boss.HoldIntro();
            _director.Play(BossIntroKey, _boss.ReleaseIntroHold);

            MoveRigToBoss();
        }

        /// <summary>
        /// Frames the boss as the centre of the room. In code rather than on a clip because the ease has
        /// to start wherever camera follow left the rig: an absolute clip snaps to a fixed coordinate on
        /// frame one and whip-pans across the arena from there.
        /// </summary>
        private void MoveRigToBoss()
        {
            Node2D rig = _director.Binder.Get2D(CutsceneRole.CameraRig);
            if (rig == null)
                return;

            // Camera bounds come from the defaults the scene is built with, never from raw coordinates
            // (CutsceneDirection 2d). Same load GameplayBootstrap does, so the two cannot drift.
            // GameplaySceneDefaults hands these back already in Godot pixels; nothing here converts.
            GameplaySceneDefaults scene = GameplaySceneDefaults.CreateFromAsset(
                Res.Load<GameplaySceneDefaultsAsset>("Gameplay/SceneDefaults", ".tres"));

            // The boss's own position rather than the arena's boss slot, so a chapter boss standing
            // anywhere gets framed too. For the shipped fight these are the same point: the intro fires
            // the frame the player is first seen, and a boss in its intro has not moved off its spawn.
            Vector2 bossPosition = _boss?.BossObject != null && IsInstanceValid(_boss.BossObject)
                ? _boss.BossObject.GlobalPosition
                : scene.WrathMiniBossSpawnPosition;

            var target = new Vector2(
                Mathf.Clamp(bossPosition.X, scene.CameraHorizontalBounds.X, scene.CameraHorizontalBounds.Y),
                bossPosition.Y);

            CutsceneRigMove.Play(rig, target, Tuning.bossIntroMoveDuration);
        }
    }
}
