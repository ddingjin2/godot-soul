using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// UNITS: scaled to pixels on top of the base class's fields - <c>projectileSpeed</c>,
    /// <c>minDistance</c>, <c>repositionDistance</c>, <c>strafeSpeed</c>, <c>patrolDistance</c> and
    /// <c>projectileArcHeight</c>.
    /// <c>projectileDamage</c> is a health number, every reposition and telegraph field is seconds or a
    /// fraction, and <c>strafeFlipChance</c> is a probability, so none of those moves.
    ///
    /// The shot's contact radius and sprite size are <b>not</b> here: <c>Scenes/Effects/EnemyProjectile.tscn</c>
    /// authors both, and a second copy in this file would fight the authored shape.
    /// </summary>
    public sealed partial class RangedCasterData : EnemyTuningData
    {
        // Ranged Specific
        [Export] public float projectileSpeed = 5f;
        [Export] public float projectileDamage = 8f;
        [Export] public float minDistance = 5f;
        [Export] public float repositionDistance = 3f;
        [Export] public float castTelegraphTime = 1.1f;
        [Export] public float strafeSpeed = 1.5f;

        /// <summary>Half the width of the patrol beat, in metres, and the hard leash on how far the caster may drift from where it spawned.</summary>
        [Export] public float patrolDistance = 3f;

        /// <summary>Seconds the caster stands still at each end of its patrol beat.</summary>
        [Export] public float patrolIdleTime = 0.5f;

        /// <summary>Shortest wait, in seconds, before the caster may back off again.</summary>
        [Export] public float repositionCooldownMin = 1.2f;

        /// <summary>Longest wait, in seconds, before the caster may back off again. Each retreat rolls between the two.</summary>
        [Export] public float repositionCooldownMax = 2f;

        /// <summary>Seconds a retreat runs for before the caster settles back into strafing.</summary>
        [Export] public float repositionDuration = 0.5f;

        /// <summary>
        /// Chance <b>per physics frame</b> that a strafing caster reverses direction. Frame-rate
        /// dependent, and deliberately carried across as-authored rather than converted to a per-second
        /// rate, which would change how the fight moves.
        /// </summary>
        [Export] public float strafeFlipChance = 0.01f;

        /// <summary>Seconds a shot flies before it gives up, which is what decides how far one actually reaches.</summary>
        [Export] public float projectileLifetime = 5f;

        /// <summary>How high a shot bows over its flight path, in metres.</summary>
        [Export] public float projectileArcHeight = 0.5f;

        // --- Telegraph readability. The blend target (yellow) is the shared danger colour and stays in
        // code with the rest of the palette; what is authored here is this archetype's own base. None
        // of the four is a distance, so ScaleToPixels leaves them alone. ---

        /// <summary>Radians per second the wind-up pulse runs at.</summary>
        [Export] public float telegraphPulseSpeed = 6f;

        /// <summary>How far the wind-up pulse swings, as a fraction of rest scale.</summary>
        [Export] public float telegraphPulseAmplitude = 0.1f;

        /// <summary>How far the body tints toward the danger colour while winding up. 1 is the danger colour outright.</summary>
        [Export] public float telegraphBlend = 0.8f;

        /// <summary>
        /// The colour the telegraph blends <i>from</i>. Deliberately not <c>enemyColor</c>: muting the
        /// body must not drag the danger read dark. See Docs/MoodDirection.md "The lerp trap".
        /// </summary>
        [Export] public Color telegraphColor = new Color(0.5f, 0.3f, 1f);

        public override void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            base.ScaleToPixels();

            projectileSpeed = World.U(projectileSpeed);
            minDistance = World.U(minDistance);
            repositionDistance = World.U(repositionDistance);
            strafeSpeed = World.U(strafeSpeed);
            patrolDistance = World.U(patrolDistance);
            projectileArcHeight = World.U(projectileArcHeight);
        }

        public static RangedCasterData Load(string designPath = "Design/RangedCaster") =>
            LoadFrom<RangedCasterData>(designPath);
    }
}
