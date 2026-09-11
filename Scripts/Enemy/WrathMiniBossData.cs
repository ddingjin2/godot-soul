using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// UNITS: scaled to pixels on top of the base class's fields - <c>moveSpeedBoss</c>,
    /// <c>slashRange</c>, <c>slamRange</c>, <c>slamShockwaveForce</c> (a knockback velocity),
    /// <c>rushSpeed</c> and <c>bossBodySize</c>.
    /// Left alone: <c>maxHealthBoss</c>, the three attack damages, every telegraph/duration/cooldown in
    /// seconds, the phase multipliers, <c>bossColor</c>, the six intro and phase beats, the telegraph
    /// fractions and <c>rushWindupFraction</c>.
    /// Scaled with the rest: <c>attackKnockbackSlash</c>, <c>rushRange</c> and <c>attackPointOffset</c>.
    /// </summary>
    public sealed partial class WrathMiniBossData : EnemyTuningData
    {
        // Boss Stats
        [Export] public float maxHealthBoss;
        [Export] public float moveSpeedBoss;
        [Export] public float attackDamageSlash;
        [Export] public float attackDamageSlam;
        [Export] public float attackDamageRage;

        // Phase Transition
        [Export] public float phaseThreshold;
        [Export] public float phaseSpeedMultiplier;
        [Export] public float phaseAttackCooldownMultiplier;
        [Export] public float rageModeDuration;

        // Slash Attack
        [Export] public float slashTelegraphTime;
        [Export] public float slashDuration;
        [Export] public float slashRange;
        [Export] public float slashCooldown;

        /// <summary>
        /// Knockback the slash puts on the player, in metres per second. Separate from the base class's
        /// <c>attackKnockback</c> because the slash has always hit harder than the rush - this is the
        /// number the fight shipped with, not a re-use of the shared one.
        /// </summary>
        [Export] public float attackKnockbackSlash;

        // Ground Slam
        [Export] public float slamTelegraphTime;
        [Export] public float slamDuration;
        [Export] public float slamRange;
        [Export] public float slamShockwaveForce;
        [Export] public float slamCooldown;

        // Rage Rush
        [Export] public float rushTelegraphTime;
        [Export] public float rushSpeed;
        [Export] public float rushDuration;
        [Export] public float rushCooldown;

        /// <summary>Reach of the rush's contact test, in metres. Slash and slam have their own ranges; this is the rush's.</summary>
        [Export] public float rushRange;

        /// <summary>
        /// How much of a rush duration passes between the telegraph ending and the charge actually
        /// moving, as a fraction of <c>rushDuration</c>. The player's window to step aside.
        /// </summary>
        [Export] public float rushWindupFraction;

        // Visuals
        [Export] public Color bossColor;
        [Export] public Vector2 bossBodySize;

        // --- Telegraph readability. The blend target (yellow for slash, red for slam) is the shared
        // danger colour and stays in code with the rest of the palette. ---

        /// <summary>Radians per second the wind-up pulse runs at.</summary>
        [Export] public float telegraphPulseSpeed;

        /// <summary>How far the wind-up pulse swings, as a fraction of rest scale.</summary>
        [Export] public float telegraphPulseAmplitude;

        /// <summary>How far the body tints toward the danger colour while winding up. 1 is the danger colour outright.</summary>
        [Export] public float telegraphBlend;

        /// <summary>
        /// The colour the telegraph blends <i>from</i>. Deliberately not <c>bossColor</c>: muting the
        /// body must not drag the danger read dark. See Docs/MoodDirection.md "The lerp trap".
        /// </summary>
        [Export] public Color telegraphColor;

        /// <summary>
        /// How far ahead of the body a swing's overlap circle is centred, in metres. Wrath carries no
        /// <c>AttackPoint</c> node, so unlike the grunt's this is what every slash actually uses.
        /// </summary>
        [Export] public float attackPointOffset;

        // --- Intro and phase-transition beats, all seconds. BossEncounterData owns the encounter's
        // outer timings (intro patience, victory delay); these are the beats inside this boss's own
        // sequence, which nothing else could time. ---

        /// <summary>Hit stop when the boss crosses into phase two.</summary>
        [Export] public float phaseTransitionHitStop;

        /// <summary>How long the body holds red on the phase-two flash.</summary>
        [Export] public float phaseFlashRedTime;

        /// <summary>How long the body holds white on the phase-two flash, after the red.</summary>
        [Export] public float phaseFlashWhiteTime;

        /// <summary>How long the boss stands blacked out before the intro impact lands.</summary>
        [Export] public float introBlackoutTime;

        /// <summary>Hit stop on the intro impact.</summary>
        [Export] public float introImpactHitStop;

        /// <summary>How long the intro settles after the impact before the fight is handed over.</summary>
        [Export] public float introSettleTime;

        public override void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            base.ScaleToPixels();

            moveSpeedBoss = World.U(moveSpeedBoss);
            slashRange = World.U(slashRange);
            attackKnockbackSlash = World.U(attackKnockbackSlash);
            rushRange = World.U(rushRange);
            attackPointOffset = World.U(attackPointOffset);
            slamRange = World.U(slamRange);
            slamShockwaveForce = World.U(slamShockwaveForce);
            rushSpeed = World.U(rushSpeed);
            bossBodySize = new Vector2(World.U(bossBodySize.X), World.U(bossBodySize.Y));
        }

        public static WrathMiniBossData Load(string designPath = "Design/WrathMiniBoss") =>
            LoadFrom<WrathMiniBossData>(designPath);
    }
}
