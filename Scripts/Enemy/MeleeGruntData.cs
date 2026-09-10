using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// UNITS: scaled to pixels on top of the base class's fields - <c>patrolDistance</c> and
    /// <c>attackRadius</c>. <c>patrolIdleTime</c> is seconds and <c>telegraphPulseSpeed</c> is radians
    /// per second, so neither moves.
    /// </summary>
    public sealed partial class MeleeGruntData : EnemyTuningData
    {
        // Melee Specific
        [Export] public float patrolDistance = 3f;
        [Export] public float patrolIdleTime = 1f;
        [Export] public float attackRadius = 0.8f;
        [Export] public float telegraphPulseSpeed = 8f;

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
