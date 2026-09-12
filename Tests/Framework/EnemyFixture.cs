using MyGame.Enemy;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// What <c>GameplayEnemySpawner.ConfigureSharedBehaviour</c> does for a shipped enemy, for the
    /// enemies the suites build by hand instead.
    ///
    /// Needed since K5b: <see cref="EnemyStateMachine"/>'s eight shared numbers carry no initialisers
    /// any more, and an enemy that reaches the tree without <c>Configure</c> reports an error and stops
    /// (PLAN_CLOSEOUT D1). The same shape as K1's <c>SetTuningData(XData.Load())</c> - the fixture reads
    /// the shipped file rather than numbers written here.
    /// </summary>
    /// <remarks>
    /// UNITS: every distance is already pixels - <c>WorldTuningData.Load</c> ran <c>ScaleToPixels</c>
    /// over the authored metres on the way in, and nothing re-scales below.
    /// </remarks>
    public static class EnemyFixture
    {
        /// <summary>
        /// Call before the machine enters the tree, which is where the spawner does it and where
        /// <c>_Ready</c> checks it.
        /// </summary>
        public static T ConfigureFromDesign<T>(T machine) where T : EnemyStateMachine
        {
            MyGame.Gameplay.WorldTuningData world = MyGame.Gameplay.WorldTuningData.Load();
            Assert.NotNull(world, "Resources/Design/WorldTuning.json has to load; every enemy's leash and gravity is in it.");

            PlayerCombatData combat = PlayerCombatData.Load();
            Assert.NotNull(combat, "Resources/Design/PlayerCombat.json has to load; the perfect-parry stun multiplier is in it.");

            machine.Configure(
                world.enemyGravity,
                world.enemyDisengageDistance,
                world.enemyIdleToPatrolTime,
                world.enemyInvestigateDuration,
                world.enemyRecoveryDuration,
                world.enemyLedgeProbeForward,
                world.enemyLedgeProbeDepth,
                combat.perfectParryStunMultiplier);

            return machine;
        }
    }
}
