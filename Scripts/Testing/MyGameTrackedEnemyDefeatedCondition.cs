using System.Linq;
using Godot;
using UnityTestAgent.Observation;
using UnityTestAgent.Scenario;

namespace MyGame.Testing
{
    /// <summary>
    /// "The enemy this run was pointed at is down" - answered from the observation when the enemy is
    /// still being reported, and from the fact that it stopped being reported once it is gone.
    /// </summary>
    public sealed class MyGameTrackedEnemyDefeatedCondition : IScenarioCondition
    {
        private readonly string enemyId;
        private readonly GodotObject trackedEnemy;
        private bool hasSeenTarget;

        public MyGameTrackedEnemyDefeatedCondition(string enemyId)
            : this(enemyId, null)
        {
        }

        /// <param name="trackedEnemy">
        /// Unity took a <c>UnityEngine.Object</c>; this takes a <see cref="GodotObject"/>. The type is
        /// the only change - it is still only ever tested for "does this still exist".
        /// </param>
        public MyGameTrackedEnemyDefeatedCondition(string enemyId, GodotObject trackedEnemy)
        {
            this.enemyId = enemyId;
            this.trackedEnemy = trackedEnemy;
        }

        public bool IsMet(AgentObservation observation)
        {
            var target = observation.Enemies.FirstOrDefault(enemy => enemy.Id == enemyId);
            if (target != null)
            {
                hasSeenTarget = true;
                return !target.IsAlive;
            }

            // Unity's overloaded == null reported a destroyed object as null; Godot's does not, so the
            // "is the object still there" test has to be IsInstanceValid. Without it a freed enemy reads
            // as still alive and this condition never fires.
            if (GodotObject.IsInstanceValid(trackedEnemy))
                return false;

            return hasSeenTarget;
        }

        public string Describe()
        {
            return "Tracked enemy defeated: " + enemyId;
        }
    }
}
