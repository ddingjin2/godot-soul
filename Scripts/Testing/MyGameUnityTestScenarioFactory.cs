using MyGame.Core;
using UnityTestAgent.Observation;
using UnityTestAgent.Scenario;

namespace MyGame.Testing
{
    /// <summary>
    /// The scenarios this game hands the scenario runner.
    /// </summary>
    /// <remarks>
    /// THE NAME IS HISTORICAL. There is no Unity here any more, and none of the eleven
    /// <c>MyGame*</c> types in this folder still touch it - but the report trail, the scenario ids they
    /// write (<c>mygame-reach-checkpoint-*</c>) and the two fixtures that drive them all key off these
    /// names, so renaming eleven types plus two test files would cost the ability to compare a run
    /// today against a run from before the port and buy nothing. They keep the names.
    ///
    /// UNITS: the bounds below are Godot pixels. Unity authored them in metres, and the vertical pair
    /// also flips sign and meaning - see <see cref="OutOfBoundsCondition"/>.
    /// </remarks>
    public static class MyGameUnityTestScenarioFactory
    {
        /// <summary>Unity's <c>-20f</c> floor: 20 m below the origin is 2000 px below it, and below is +Y.</summary>
        private static readonly float FallCeilingPx = World.U(20f);

        /// <summary>Unity's <c>100f</c> horizontal limit in pixels.</summary>
        private static readonly float MaxAbsXPx = World.U(100f);

        public static UnityTestScenario CreatePlayerAliveSmoke(int maxTicks = 5)
        {
            return new UnityTestScenario("mygame-player-alive-smoke", maxTicks)
                .WithSuccessCondition(new PlayerAliveCondition())
                .WithFailureCondition(new PlayerDeadCondition());
        }

        /// <param name="target">Godot pixels, +Y down.</param>
        /// <param name="radius">Godot pixels. 50 is Unity's authored 0.5 m.</param>
        public static UnityTestScenario CreateMoveToPosition(Vector2Observation target, float radius = 50f, int maxTicks = 120)
        {
            return new UnityTestScenario("mygame-reach-position", maxTicks)
                .WithSuccessCondition(new ReachedPositionCondition(target, radius))
                .WithFailureCondition(new PlayerDeadCondition())
                .WithFailureCondition(new OutOfBoundsCondition(FallCeilingPx, MaxAbsXPx));
        }

        public static UnityTestScenario CreateReachCheckpoint(string checkpointId, int maxTicks = 180)
        {
            return new UnityTestScenario("mygame-reach-checkpoint-" + checkpointId, maxTicks)
                .WithSuccessCondition(new ReachedCheckpointCondition(checkpointId))
                .WithFailureCondition(new PlayerDeadCondition())
                .WithFailureCondition(new OutOfBoundsCondition(FallCeilingPx, MaxAbsXPx));
        }

        public static UnityTestScenario CreateDefeatEnemy(string enemyId, int maxTicks = 240)
        {
            return new UnityTestScenario("mygame-defeat-enemy-" + enemyId, maxTicks)
                .WithSuccessCondition(new MyGameTrackedEnemyDefeatedCondition(enemyId))
                .WithFailureCondition(new PlayerDeadCondition())
                .WithFailureCondition(new OutOfBoundsCondition(FallCeilingPx, MaxAbsXPx));
        }

        public static UnityTestScenario CreateBossPatternSurvival(string bossId, int maxTicks = 180)
        {
            return new UnityTestScenario("mygame-boss-pattern-survival-" + bossId, maxTicks)
                .WithSuccessCondition(new SurviveForTicksCondition(maxTicks))
                .WithFailureCondition(new PlayerDeadCondition())
                .WithFailureCondition(new BossDefeatedCondition(bossId));
        }
    }
}
