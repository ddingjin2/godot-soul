using System.Linq;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public sealed class SimpleCombatAgent : IUnityTestAgent
    {
        private readonly float attackRange;
        private readonly float dodgeStaminaCost;
        private readonly float attackStaminaCost;

        public SimpleCombatAgent(float attackRange = 1.5f, float dodgeStaminaCost = 20f, float attackStaminaCost = 10f)
        {
            this.attackRange = attackRange;
            this.dodgeStaminaCost = dodgeStaminaCost;
            this.attackStaminaCost = attackStaminaCost;
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (!observation.Player.IsAlive)
            {
                return new AgentDecision(AgentAction.Idle(), "player dead");
            }

            var target = observation.Enemies
                .Where(enemy => enemy.IsAlive)
                .OrderBy(enemy => DistanceX(observation, enemy))
                .FirstOrDefault();

            if (target == null)
            {
                return new AgentDecision(AgentAction.Idle(), "no target");
            }

            var directionToTarget = target.Position.X >= observation.Player.Position.X ? 1f : -1f;
            var distance = DistanceX(observation, target);

            if (target.IsAttacking && observation.Player.Stamina >= dodgeStaminaCost)
            {
                return new AgentDecision(AgentAction.Dodge(-directionToTarget), "dodge telegraphed attack");
            }

            if (distance <= attackRange && observation.Player.Stamina >= attackStaminaCost)
            {
                return new AgentDecision(AgentAction.Attack(), "attack target in range");
            }

            return new AgentDecision(AgentAction.Move(directionToTarget, 0.1f), "approach target");
        }

        private static float DistanceX(AgentObservation observation, EnemyObservation enemy)
        {
            return System.Math.Abs(enemy.Position.X - observation.Player.Position.X);
        }
    }
}
