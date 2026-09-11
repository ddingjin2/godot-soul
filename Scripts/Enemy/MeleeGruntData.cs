using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// UNITS: scaled to pixels on top of the base class's fields - <c>patrolDistance</c> and
    /// <c>attackRadius</c>. <c>patrolIdleTime</c> is seconds, <c>telegraphPulseSpeed</c> is radians
    /// per second, and the two telegraph fractions and the telegraph colour are unitless, so none of
    /// them moves.
    /// </summary>
    public sealed partial class MeleeGruntData : EnemyTuningData
    {
        // Melee Specific
        [Export] public float patrolDistance;
        [Export] public float patrolIdleTime;
        [Export] public float attackRadius;
        [Export] public float telegraphPulseSpeed;

        // --- Telegraph readability. The blend target (yellow / red) is the shared danger colour and
        // stays in code with the rest of the palette; what is authored here is this archetype's own
        // base. None of the four is a distance, so ScaleToPixels leaves them alone. ---

        /// <summary>How far the wind-up pulse swings, as a fraction of rest scale.</summary>
        [Export] public float telegraphPulseAmplitude;

        /// <summary>How far the body tints toward the danger colour while winding up. 1 is the danger colour outright.</summary>
        [Export] public float telegraphBlend;

        /// <summary>
        /// The colour the telegraph blends <i>from</i>. Deliberately not <c>enemyColor</c>: muting the
        /// body must not drag the danger read dark. See Docs/MoodDirection.md "The lerp trap".
        /// </summary>
        [Export] public Color telegraphColor;

        public override void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            base.ScaleToPixels();

            patrolDistance = World.U(patrolDistance);
            attackRadius = World.U(attackRadius);
        }

        public static MeleeGruntData Load(string designPath = "Design/MeleeGrunt") =>
            LoadFrom<MeleeGruntData>(designPath);
    }
}
