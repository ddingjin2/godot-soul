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
        [Export] public float dodgeSpeed;
        [Export] public float dodgeDuration;
        [Export] public float dodgeCooldown;
        [Export] public float dodgeInvulnerabilityTime;

        // Attack
        [Export] public float attackDuration;
        [Export] public float attackCooldown;
        [Export] public float attackActiveWindow;
        [Export] public float attackDamage;
        [Export] public float attackKnockback;
        [Export] public float attackCancelWindow;

        // Heavy attack
        [Export] public float heavyAttackDuration;
        [Export] public float heavyAttackCooldown;
        [Export] public float heavyAttackActiveWindow;
        [Export] public float heavyAttackDamageMultiplier;
        [Export] public float heavyAttackKnockbackMultiplier;

        // Parry
        [Export] public float parryWindow;
        [Export] public float perfectParryWindow;
        [Export] public float parryCooldown;
        [Export] public float parryStunDuration;
        [Export] public float perfectParryStunMultiplier;

        /// <summary>How long the player is locked out of actions after their poise gauge breaks.</summary>
        [Export] public float staggerDuration;

        [Export] public float inputBufferTime;

        // Combo
        [Export] public int maxComboSteps;
        [Export] public float comboStepDamageMultiplier;

        // Stamina costs
        [Export] public float attackStaminaCost;
        [Export] public float heavyAttackStaminaCost;
        [Export] public float dodgeStaminaCost;
        [Export] public float parryStaminaCost;

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
