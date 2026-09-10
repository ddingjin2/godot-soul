using Godot;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// The player's gauges, authored in <c>Resources/Design/PlayerResources.json</c>.
    /// </summary>
    /// <remarks>
    /// <b>Unit scaling: none.</b> Nothing in this file is a distance, a speed or an acceleration -
    /// health, humanity, stamina and poise are gauge points, the regen rates are points per second, the
    /// delays and the wind-up are seconds, and the charge count is a count. So <see cref="Load"/> reads
    /// the JSON straight through with no <see cref="World.Ppu"/> pass, unlike its movement and combat
    /// siblings.
    /// </remarks>
    public partial class PlayerResourceData : Resource
    {
        /// <summary>Base name of the design file, shared with the Gameplay tuning catalog.</summary>
        public const string FileName = "PlayerResources";

        // Health
        [Export] public float maxHealth = 100f;
        [Export] public float startingHealth = 100f;

        // Humanity
        [Export] public float startingHumanity = 100f;

        // Stamina
        [Export] public float maxStamina = 100f;
        [Export] public float staminaRegenRate = 30f;
        [Export] public float staminaRegenDelay = 1.5f;

        /// <summary>Stagger resistance. Zero means the player can never be staggered.</summary>
        [Export] public float maxPoise = 60f;

        /// <summary>How much harder a heavy attack hits poise than a light one.</summary>
        [Export] public float poiseHeavyMultiplier = 2f;
        [Export] public float poiseRegenDelay = 2f;
        [Export] public float poiseRegenRate = 30f;

        /// <summary>Health restored when the drink finishes. Nothing is restored if it is interrupted.</summary>
        [Export] public float healAmount = 40f;

        /// <summary>Drinks per rest. A checkpoint rest and a respawn both refill them.</summary>
        [Export] public int maxHealCharges = 3;

        /// <summary>Helpless wind-up before the heal lands. Movement and dodge are locked; a hit cancels it and the charge is still spent.</summary>
        [Export] public float healWindup = 0.9f;

        /// <summary>Grace before a dropped soul stain can be picked up, so the death that dropped it cannot instantly reclaim it.</summary>
        [Export] public float soulStainPickupDelay = 0.2f;

        // Costs
        [Export] public float attackCost = 20f;
        [Export] public float dodgeCost = 25f;
        [Export] public float parryCost = 15f;
        [Export] public float blockCostPerSecond = 10f;
        [Export] public float blockStaminaThreshold = 20f;

        /// <summary>The authored file, or null when it is missing.</summary>
        public static PlayerResourceData Load()
        {
            return Res.LoadJson<PlayerResourceData>("Design/" + FileName);
        }
    }
}
