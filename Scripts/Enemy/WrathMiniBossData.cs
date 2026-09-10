using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// UNITS: scaled to pixels on top of the base class's fields - <c>moveSpeedBoss</c>,
    /// <c>slashRange</c>, <c>slamRange</c>, <c>slamShockwaveForce</c> (a knockback velocity),
    /// <c>rushSpeed</c> and <c>bossBodySize</c>.
    /// Left alone: <c>maxHealthBoss</c>, the three attack damages, every telegraph/duration/cooldown in
    /// seconds, the phase multipliers and <c>bossColor</c>.
    /// </summary>
    public sealed partial class WrathMiniBossData : EnemyTuningData
    {
        // Boss Stats
        [Export] public float maxHealthBoss = 200f;
        [Export] public float moveSpeedBoss = 2.5f;
        [Export] public float attackDamageSlash = 20f;
        [Export] public float attackDamageSlam = 25f;
        [Export] public float attackDamageRage = 15f;

        // Phase Transition
        [Export] public float phaseThreshold = 0.5f;
        [Export] public float phaseSpeedMultiplier = 1.5f;
        [Export] public float phaseAttackCooldownMultiplier = 0.6f;
        [Export] public float rageModeDuration = 5f;

        // Slash Attack
        [Export] public float slashTelegraphTime = 0.85f;
        [Export] public float slashDuration = 0.35f;
        [Export] public float slashRange = 1.8f;
        [Export] public float slashCooldown = 2f;

        // Ground Slam
        [Export] public float slamTelegraphTime = 1f;
        [Export] public float slamDuration = 0.45f;
        [Export] public float slamRange = 2.5f;
        [Export] public float slamShockwaveForce = 8f;
        [Export] public float slamCooldown = 3f;

        // Rage Rush
        [Export] public float rushTelegraphTime = 0.8f;
        [Export] public float rushSpeed = 12f;
        [Export] public float rushDuration = 0.6f;
        [Export] public float rushCooldown = 4f;

        // Visuals
        [Export] public Color bossColor = new Color(0.9f, 0.2f, 0.2f);
        [Export] public Vector2 bossBodySize = new Vector2(1.2f, 2f);

        public override void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            base.ScaleToPixels();

            moveSpeedBoss = World.U(moveSpeedBoss);
            slashRange = World.U(slashRange);
            slamRange = World.U(slamRange);
            slamShockwaveForce = World.U(slamShockwaveForce);
            rushSpeed = World.U(rushSpeed);
            bossBodySize = new Vector2(World.U(bossBodySize.X), World.U(bossBodySize.Y));
        }

        public static WrathMiniBossData Load(string designPath = "Design/WrathMiniBoss") =>
            LoadFrom<WrathMiniBossData>(designPath);
    }
}
