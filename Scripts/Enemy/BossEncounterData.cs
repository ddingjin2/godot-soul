using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// What a boss fight is, as opposed to what the boss is. Stats, damage and telegraph timings stay in
    /// the archetype data ([[WrathMiniBossData]], [[RainbowChapterBossData]]); this holds the numbers that
    /// describe the encounter around them - how far the arena reaches, how long the intro may hold the
    /// fight, how the phase-two pattern is weighted.
    ///
    /// They were constants on <see cref="WrathMiniBoss"/>, which meant a second boss with a wider arena
    /// was a code change. The constants stay as the fallback so a boss built with no catalog - every one
    /// the test runners make - fights exactly as it did.
    ///
    /// The three phase-two weights are relative, not percentages: they are summed and normalised, so
    /// 4/3/3 and 40/30/30 mean the same thing and no edit can leave them adding up to something other
    /// than one.
    ///
    /// UNITS: <see cref="ScaleToPixels"/> scales <c>arenaLeftOffset</c>, <c>arenaRightOffset</c> and
    /// <c>detectionRange</c>. Everything else here is seconds, a weight or a multiplier.
    /// </summary>
    public sealed partial class BossEncounterData : Resource
    {
        // Arena
        /// <summary>
        /// How far left of its spawn the boss may walk. The fight is anchored on the spawn point, not on
        /// a centre object, so a boss dropped anywhere carries its arena with it.
        /// </summary>
        [Export] public float arenaLeftOffset = 5f;

        /// <summary>
        /// How far right of its spawn the boss may walk. Short on purpose: the wall is behind the boss,
        /// so the player is always the one with room to retreat.
        /// </summary>
        [Export] public float arenaRightOffset = 1.1f;

        // Engagement
        /// <summary>
        /// How far the boss notices the player from. Wider than a normal enemy's, so the fight starts
        /// when the player enters the arena rather than when they walk into reach.
        /// </summary>
        [Export] public float detectionRange = 8f;

        // Intro
        /// <summary>
        /// Longest the intro sequence will wait for a cutscene listener before starting the fight anyway.
        /// A hold that never lifts would leave the boss standing in a fight that never begins.
        /// </summary>
        [Export] public float introHoldTimeout = 2.7f;

        // Recovery
        /// <summary>
        /// The opening after an attack ends. This is the player's whole punish window, so it is the
        /// single most feel-critical number here.
        /// </summary>
        [Export] public float postAttackRecoveryTime = 0.5f;

        /// <summary>How much of that window phase two keeps. Below 1 means the enraged boss gives less room.</summary>
        [Export] public float phaseTwoRecoveryMultiplier = 0.8f;

        // Phase Two Pattern
        /// <summary>Relative weight of the slash in phase two. Summed with the other two and normalised.</summary>
        [Export] public float phaseTwoSlashWeight = 0.4f;

        [Export] public float phaseTwoSlamWeight = 0.3f;
        [Export] public float phaseTwoRushWeight = 0.3f;

        /// <summary>
        /// Extra movement speed while rage is up, on top of the phase-two multiplier in the boss's own
        /// tuning.
        /// </summary>
        [Export] public float rageSpeedMultiplier = 1.2f;

        // Presentation
        /// <summary>
        /// Beat between the death blow and the victory callback, so the kill reads before the panel
        /// arrives.
        /// </summary>
        [Export] public float victoryPresentationDelay = 1.5f;

        private bool _scaledToPixels;

        /// <summary>
        /// Picks a pattern index (0 slash, 1 slam, 2 rush) from the weights. <paramref name="roll"/> is
        /// 0..1 and comes from the caller so a test can drive the selection instead of the RNG.
        /// </summary>
        /// <remarks>
        /// A table of all-zero weights would divide by nothing; it falls back to the slash rather than
        /// throwing, because a boss that stops attacking is a worse failure than a predictable one.
        /// </remarks>
        public int SelectPhaseTwoPattern(float roll)
        {
            float total = phaseTwoSlashWeight + phaseTwoSlamWeight + phaseTwoRushWeight;
            if (total <= 0f)
            {
                return 0;
            }

            float point = Mathf.Clamp(roll, 0f, 1f) * total;

            if (point < phaseTwoSlashWeight)
            {
                return 0;
            }

            if (point < phaseTwoSlashWeight + phaseTwoSlamWeight)
            {
                return 1;
            }

            return 2;
        }

        /// <summary>Unity's <c>[Min]</c> attributes, which the editor enforced and JSON does not.</summary>
        public void OnValidate()
        {
            arenaLeftOffset = Mathf.Max(0f, arenaLeftOffset);
            arenaRightOffset = Mathf.Max(0f, arenaRightOffset);
            detectionRange = Mathf.Max(0f, detectionRange);
            introHoldTimeout = Mathf.Max(0f, introHoldTimeout);
            postAttackRecoveryTime = Mathf.Max(0f, postAttackRecoveryTime);
            phaseTwoRecoveryMultiplier = Mathf.Clamp(phaseTwoRecoveryMultiplier, 0.1f, 1f);
            phaseTwoSlashWeight = Mathf.Max(0f, phaseTwoSlashWeight);
            phaseTwoSlamWeight = Mathf.Max(0f, phaseTwoSlamWeight);
            phaseTwoRushWeight = Mathf.Max(0f, phaseTwoRushWeight);
            rageSpeedMultiplier = Mathf.Max(0.1f, rageSpeedMultiplier);
            victoryPresentationDelay = Mathf.Max(0f, victoryPresentationDelay);
        }

        public void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            _scaledToPixels = true;

            arenaLeftOffset = World.U(arenaLeftOffset);
            arenaRightOffset = World.U(arenaRightOffset);
            detectionRange = World.U(detectionRange);
        }

        /// <summary>
        /// <c>Res.LoadJson</c> plus the clamp and the unit conversion, so no caller can pick up an
        /// encounter still measured in metres. Chapter one is <c>"Design/WrathEncounter"</c>; the
        /// authored chapters are <c>"Design/Chapter0N_&lt;Colour&gt;_Encounter"</c>.
        /// </summary>
        public static BossEncounterData Load(string designPath)
        {
            BossEncounterData data = Res.LoadJson<BossEncounterData>(designPath);
            if (data == null)
            {
                return null;
            }

            data.OnValidate();
            data.ScaleToPixels();
            return data;
        }
    }
}
