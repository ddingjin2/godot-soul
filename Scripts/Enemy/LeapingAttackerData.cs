using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// UNITS: scaled to pixels on top of the base class's fields - <c>leapSpeed</c>, <c>leapHeight</c>
    /// and <c>maintainDistance</c>. The two times stay in seconds.
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
        }

        public static LeapingAttackerData Load(string designPath = "Design/LeapingAttacker") =>
            LoadFrom<LeapingAttackerData>(designPath);
    }
}
