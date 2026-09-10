using Godot;

namespace MyGame.Player
{
    /// <summary>
    /// Plays the sword's swing clip whenever the player actually performs an attack. Sits on the
    /// readable sword node under the hitbox anchor, not on the player body.
    ///
    /// Unity drove an <c>Animator</c> and pulled a trigger parameter; a Godot
    /// <see cref="AnimationPlayer"/> plays a named animation, so <see cref="AttackTriggerName"/> is now
    /// the clip's name rather than a trigger's. Stop-then-play is the <c>ResetTrigger</c>/<c>SetTrigger</c>
    /// pair: a second swing during the first has to read as a second swing, not one long one.
    ///
    /// The animation-event hooks stay public with their Unity names - an AnimationPlayer method track
    /// calls them exactly where the old clip's animation events did.
    /// </summary>
    public partial class PlayerAttackAnimator2D : Node2D
    {
        public const string AttackTriggerName = "Attack";

        [Export] private AnimationPlayer animator;
        private PlayerController2D player;

        public void Initialize(PlayerController2D sourcePlayer, AnimationPlayer sourceAnimator = null)
        {
            if (player != null)
            {
                player.OnAttackPerformed -= PlayAttack;
            }

            player = sourcePlayer;
            animator = sourceAnimator ?? animator ?? FindAnimator();

            if (player != null)
            {
                player.OnAttackPerformed += PlayAttack;
            }
        }

        public override void _Ready()
        {
            animator ??= FindAnimator();
        }

        public override void _ExitTree()
        {
            if (player != null)
            {
                player.OnAttackPerformed -= PlayAttack;
            }
        }

        public void PlayAttack()
        {
            if (animator == null || !animator.HasAnimation(AttackTriggerName))
            {
                return;
            }

            animator.Stop();
            animator.Play(AttackTriggerName);
        }

        public void AnimationEvent_EnableHitbox()
        {
            player?.AnimationSignalEnableHitbox();
        }

        public void AnimationEvent_DisableHitbox()
        {
            player?.AnimationSignalDisableHitbox();
        }

        public void AnimationEvent_AttackEnd()
        {
            player?.AnimationSignalAttackEnd();
        }

        private AnimationPlayer FindAnimator()
        {
            foreach (Node child in GetChildren())
            {
                if (child is AnimationPlayer found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
