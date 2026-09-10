using System.Linq;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Scenario
{
    public sealed class EnemyDefeatedCondition : IScenarioCondition
    {
        private readonly string enemyId;

        public EnemyDefeatedCondition(string enemyId)
        {
            this.enemyId = enemyId;
        }

        public bool IsMet(AgentObservation observation)
        {
            return observation.Enemies.Any(enemy => enemy.Id == enemyId && !enemy.IsAlive);
        }

        public string Describe()
        {
            return "Enemy defeated: " + enemyId;
        }
    }

    public sealed class BossDefeatedCondition : IScenarioCondition
    {
        private readonly string bossId;

        public BossDefeatedCondition(string bossId)
        {
            this.bossId = bossId;
        }

        public bool IsMet(AgentObservation observation)
        {
            return observation.Bosses.Any(boss => boss.Id == bossId && !boss.IsAlive);
        }

        public string Describe()
        {
            return "Boss defeated: " + bossId;
        }
    }

    public sealed class MaxHitPointCondition : IScenarioCondition
    {
        private readonly int maxHitPoints;

        public MaxHitPointCondition(int maxHitPoints)
        {
            this.maxHitPoints = maxHitPoints;
        }

        public bool IsMet(AgentObservation observation)
        {
            return observation.Player.HitPoints <= maxHitPoints;
        }

        public string Describe()
        {
            return "Player hit points at or below " + maxHitPoints;
        }
    }
}
