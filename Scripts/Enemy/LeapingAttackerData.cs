using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// UNITS: scaled to pixels on top of the base class's fields - <c>leapSpeed</c>, <c>leapHeight</c>,
    /// <c>maintainDistance</c>, <c>patrolDistance</c> and <c>maintainDistanceDeadband</c>. The times stay
    /// in seconds and the telegraph fields, the colour and <c>landingPunishMultiplier</c> are unitless.
    ///
    /// <c>leapHeight</c> is a launch velocity, not a height, and it is authored as a positive "upward"
    /// number the way Unity's +Y-up world wanted it. The sign flip belongs at the point of use, not
    /// here - see <see cref="LeapingAttacker.ExecuteLeap"/>.
    /// </summary>
    public sealed partial class LeapingAttackerData : EnemyTuningData
    {
        // Leap Specific
        [Export] public float leapSpeed = 10f;
        [Export] public float leapHeight = 3f;
        [Export] public float maintainDistance = 4f;
        [Export] public float landingVulnerabilityTime = 0.5f;
        [Export] public float leapTelegraphTime = 1.1f;

        /// <summary>
        /// Half the width of the patrol beat, in metres, measured from where the leaper spawned. It is
        /// also the hard leash on combat movement - <c>ClampHomewardDirection</c> refuses to carry the
        /// body past it - so widening this widens the whole fight, not just the walk.
        /// </summary>
        [Export] public float patrolDistance = 3f;

        /// <summary>Seconds the leaper stands still at each end of its patrol beat.</summary>
        [Export] public float patrolIdleTime = 0.5f;

        /// <summary>
        /// Half-width of the band around <c>maintainDistance</c> inside which the leaper simply stands,
        /// in metres. This is the spacing of the whole fight: too narrow and it jitters, too wide and it
        /// never closes.
        /// </summary>
        [Export] public float maintainDistanceDeadband = 1f;

        /// <summary>
        /// What damage is multiplied by while the leaper is in its landing-vulnerability window. The
        /// window's length is <c>landingVulnerabilityTime</c>; this is its payoff, and the two together
        /// are the archetype's whole trade.
        /// </summary>
        [Export] public float landingPunishMultiplier = 2f;

        // --- Telegraph readability. The blend target (yellow) is the shared danger colour and stays in
        // code with the rest of the palette; what is authored here is this archetype's own base. None
        // of the four is a distance, so ScaleToPixels leaves them alone. ---

        /// <summary>Radians per second the wind-up pulse runs at.</summary>
        [Export] public float telegraphPulseSpeed = 8f;

        /// <summary>How far the wind-up pulse swings, as a fraction of rest scale.</summary>
        [Export] public float telegraphPulseAmplitude = 0.15f;

        /// <summary>How far the body tints toward the danger colour while winding up. 1 is the danger colour outright.</summary>
        [Export] public float telegraphBlend = 0.7f;

        /// <summary>
        /// The colour the telegraph blends <i>from</i>. Deliberately not <c>enemyColor</c>: muting the
        /// body must not drag the danger read dark. See Docs/MoodDirection.md "The lerp trap".
        /// </summary>
        [Export] public Color telegraphColor = new Color(1f, 0.5f, 0f);

        public override void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            base.ScaleToPixels();

            leapSpeed = World.U(leapSpeed);
            leapHeight = World.U(leapHeight);
            maintainDistance = World.U(maintainDistance);
            patrolDistance = World.U(patrolDistance);
            maintainDistanceDeadband = World.U(maintainDistanceDeadband);
        }

        public static LeapingAttackerData Load(string designPath = "Design/LeapingAttacker") =>
            LoadFrom<LeapingAttackerData>(designPath);
    }
}
