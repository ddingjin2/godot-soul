using Godot;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// Combat tuning, authored in <c>Resources/Design/PlayerCombat.json</c>.
    /// </summary>
    /// <remarks>
    /// <b>Unit scaling.</b> Only two numbers here are spatial, and <see cref="Load"/> scales them by
    /// <see cref="World.Ppu"/> as the JSON is read:
    /// <list type="bullet">
    /// <item><description>Scaled (metres/second -> pixels/second): <c>dodgeSpeed</c>,
    /// <c>attackKnockback</c> (it is handed to the motor as a velocity, not as a force).</description></item>
    /// <item><description>Left alone: every duration, window and cooldown (seconds), <c>attackDamage</c>
    /// and the stamina costs (gauge points), and every multiplier and step count.</description></item>
    /// </list>
    /// </remarks>
    public partial class PlayerCombatData : Resource
    {
        /// <summary>Base name of the design file, shared with the Gameplay tuning catalog.</summary>
        public const string FileName = "PlayerCombat";

        // Dodge
        [Export] public float dodgeSpeed = World.U(15f);
        [Export] public float dodgeDuration = 0.2f;
        [Export] public float dodgeCooldown = 0.8f;
        [Export] public float dodgeInvulnerabilityTime = 0.15f;

        // Attack
        [Export] public float attackDuration = 0.3f;
        [Export] public float attackCooldown = 0.5f;
        [Export] public float attackActiveWindow = 0.1f;
        [Export] public float attackDamage = 20f;
        [Export] public float attackKnockback = World.U(4f);
        [Export] public float attackCancelWindow = 0.08f;

        // Heavy attack
        [Export] public float heavyAttackDuration = 0.45f;
        [Export] public float heavyAttackCooldown = 0.85f;
        [Export] public float heavyAttackActiveWindow = 0.16f;
        [Export] public float heavyAttackDamageMultiplier = 1.8f;
        [Export] public float heavyAttackKnockbackMultiplier = 1.4f;

        // Parry
        [Export] public float parryWindow = 0.2f;
        [Export] public float perfectParryWindow = 0.08f;
        [Export] public float parryCooldown = 0.5f;
        [Export] public float parryStunDuration = 0.8f;
        [Export] public float perfectParryStunMultiplier = 1.6f;

        /// <summary>How long the player is locked out of actions after their poise gauge breaks.</summary>
        [Export] public float staggerDuration = 0.5f;

        [Export] public float inputBufferTime = 0.15f;

        // Combo
        [Export] public int maxComboSteps = 3;
        [Export] public float comboStepDamageMultiplier = 1.15f;

        // Stamina costs
        [Export] public float attackStaminaCost = 20f;
        [Export] public float heavyAttackStaminaCost = 35f;
        [Export] public float dodgeStaminaCost = 25f;
        [Export] public float parryStaminaCost = 15f;

        /// <summary>The authored file scaled into pixels, or null when it is missing.</summary>
        public static PlayerCombatData Load()
        {
            var raw = Res.LoadJson<PlayerCombatData>("Design/" + FileName);
            if (raw == null)
            {
                return null;
            }

            raw.dodgeSpeed = World.U(raw.dodgeSpeed);
            raw.attackKnockback = World.U(raw.attackKnockback);
            return raw;
        }
    }
}
