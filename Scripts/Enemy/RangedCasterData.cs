using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// UNITS: scaled to pixels on top of the base class's fields - <c>projectileSpeed</c>,
    /// <c>minDistance</c>, <c>repositionDistance</c> and <c>strafeSpeed</c>.
    /// <c>projectileDamage</c> is a health number and <c>castTelegraphTime</c> is seconds, so neither
    /// moves.
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
        }

        public static RangedCasterData Load(string designPath = "Design/RangedCaster") =>
            LoadFrom<RangedCasterData>(designPath);
    }
}
