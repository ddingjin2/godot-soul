using Godot;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// The soul sink's numbers: what a level of each stat gives, what the next level costs, and how far
    /// it can go. Authored in <c>Resources/Design/ProgressionTuning.json</c> so the curve is a designer
    /// edit rather than a code change ([[PlaytimePlan]] Phase 1).
    ///
    /// Lives in the Player namespace rather than next to <c>WorldTuningData</c> in Gameplay because
    /// <see cref="PlayerProgression"/> reads it and Player may not reference Gameplay. That is the same
    /// reason <see cref="PlayerResourceData"/> and <see cref="PlayerCombatData"/> live here and are loaded
    /// by the Gameplay catalog rather than defined by it.
    /// </summary>
    /// <remarks>
    /// <b>Unit scaling: none.</b> Levels, gauge points per level and a soul price are all counts - there
    /// is no distance, speed or acceleration in this file, so nothing is multiplied by
    /// <see cref="World.Ppu"/>. The per-level numbers land on maximums (health, stamina, base attack
    /// damage, poise) that are unscaled themselves.
    /// </remarks>
    public partial class ProgressionTuningData : Resource
    {
        /// <summary>Base name of the design file, shared with <c>GameplayTuningCatalog</c> so there is one spelling of it.</summary>
        public const string FileName = "ProgressionTuning";

        private const string DesignResourceFolder = "Design/";

        /// <summary>How many levels one stat can take. A ceiling, not a target - the campaign's soul income buys far fewer than this across all four.</summary>
        [Export] public int maxLevelPerStat = 20;

        /// <summary>Vitality: added to the player's maximum health per level.</summary>
        [Export] public float vitalityPerLevel = 12f;

        /// <summary>Endurance: added to the player's maximum stamina per level.</summary>
        [Export] public float endurancePerLevel = 8f;

        /// <summary>Strength: added to the player's base attack damage per level. Heavy attacks and combo steps scale off that base, so they gain with it.</summary>
        [Export] public float strengthPerLevel = 2f;

        /// <summary>Resolve: added to the player's maximum poise per level.</summary>
        [Export] public float resolvePerLevel = 6f;

        /// <summary>Souls for the very first level bought, of any stat.</summary>
        [Export] public int baseCost = 80;

        /// <summary>Multiplier applied per level already bought, counting all four stats together - so the fifth level costs the same whichever stat it goes into.</summary>
        [Export] public float costGrowth = 1.07f;

        /// <summary>
        /// Souls for the next level, given how many levels have been bought across every stat. Driven by
        /// the total rather than by the one stat, so spreading points is not cheaper than deepening one.
        /// </summary>
        public int CostForNextLevel(int totalLevelsBought)
        {
            float growth = Mathf.Max(1f, costGrowth);
            float cost = Mathf.Max(1, baseCost) * Mathf.Pow(growth, Mathf.Max(0, totalLevelsBought));

            // Souls are whole numbers and TrySpend refuses anything at or below zero.
            return Mathf.Max(1, Mathf.RoundToInt(cost));
        }

        /// <summary>
        /// The authored file, or the defaults above when it is missing. Unlike the Gameplay catalog this
        /// never returns null: progression is a save-backed system, and a missing tuning file must leave
        /// the levels a slot already holds applicable rather than silently inert.
        /// </summary>
        public static ProgressionTuningData Load()
        {
            var loaded = Res.LoadJson<ProgressionTuningData>(DesignResourceFolder + FileName);
            if (loaded == null)
            {
                return new ProgressionTuningData { ResourceName = FileName };
            }

            loaded.ResourceName = FileName;
            return loaded;
        }
    }
}
