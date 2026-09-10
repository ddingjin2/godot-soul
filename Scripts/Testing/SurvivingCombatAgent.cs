using System.Linq;
using MyGame.Core;
using UnityTestAgent.Agent;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace MyGame.Testing
{
    /// <summary>
    /// <see cref="SimpleCombatAgent"/> with the three habits that keep a player alive in a crowd.
    /// Lives here rather than in the package on purpose: the simple agent is the baseline every
    /// measurement in <c>GameplayCombatCostTests</c> was taken with, and moving it would silently
    /// re-date those numbers.
    /// </summary>
    /// <remarks>
    /// Written after a forward-push run through chapter one's front spent 143.9 of 150 seconds in
    /// contact and killed 1 of 61 bodies, dying four times with three flasks untouched. The simple
    /// agent dodges, but only ever watches the nearest enemy, never drinks, and believes a swing costs
    /// 10 stamina against an authored 20 - so in a cluster it is hit from the side while committed to a
    /// swing it cannot afford, and it never spends the healing it is carrying.
    ///
    /// Three changes, and nothing else:
    /// 1. It drinks when the bar is low and nothing is winding up at it.
    /// 2. It reads <em>every</em> enemy in reach for a telegraph, not just the closest one.
    /// 3. Its stamina costs are the authored ones, passed in rather than guessed.
    ///
    /// UNITS: the two ranges are Godot pixels, because <see cref="MyGameStateProbe"/> reports positions
    /// in pixels. The defaults are Unity's authored metres written out - a C# default value cannot call
    /// <see cref="World.U"/>. Stamina costs and the health fraction are not spatial and are unchanged.
    ///
    /// ponytail: still no heavy attack and no parry. Both are worth more than they cost in a real
    /// player's hands, and both need a read on wind-up timing this observation does not carry. Add them
    /// when a measurement actually turns on them.
    /// </remarks>
    public sealed class SurvivingCombatAgent : IUnityTestAgent
    {
        private readonly float _attackRange;
        private readonly float _dodgeStaminaCost;
        private readonly float _attackStaminaCost;
        private readonly float _healBelowFraction;
        private readonly float _threatRange;

        /// <param name="attackRange">Pixels. 150 is Unity's authored 1.5 m.</param>
        /// <param name="threatRange">Pixels. 250 is Unity's authored 2.5 m.</param>
        public SurvivingCombatAgent(
            float attackRange = 150f,
            float dodgeStaminaCost = 20f,
            float attackStaminaCost = 20f,
            float healBelowFraction = 0.45f,
            float threatRange = 250f)
        {
            _attackRange = attackRange;
            _dodgeStaminaCost = dodgeStaminaCost;
            _attackStaminaCost = attackStaminaCost;
            _healBelowFraction = healBelowFraction;
            _threatRange = threatRange;
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (!observation.Player.IsAlive)
                return new AgentDecision(AgentAction.Idle(), "player dead");

            EnemyObservation target = observation.Enemies
                .Where(enemy => enemy.IsAlive)
                .OrderBy(enemy => DistanceX(observation, enemy))
                .FirstOrDefault();

            if (target == null)
                return new AgentDecision(AgentAction.Idle(), "no target");

            float directionToTarget = target.Position.X >= observation.Player.Position.X ? 1f : -1f;
            float distance = DistanceX(observation, target);

            // Anything in reach that is winding up, not just the one that happens to be closest. In a
            // cluster the killing hit usually comes from the enemy the agent had its back to.
            EnemyObservation winding = observation.Enemies
                .Where(enemy => enemy.IsAlive && enemy.IsAttacking && DistanceX(observation, enemy) <= _threatRange)
                .OrderBy(enemy => DistanceX(observation, enemy))
                .FirstOrDefault();

            if (winding != null && observation.Player.Stamina >= _dodgeStaminaCost)
            {
                float away = winding.Position.X >= observation.Player.Position.X ? -1f : 1f;
                return new AgentDecision(AgentAction.Dodge(away), "dodge the nearest telegraph");
            }

            // Only with nothing winding up: the heal has a commit window, and drinking into a swing is
            // how a player loses the flask and the health together.
            if (winding == null && IsHurt(observation))
                return new AgentDecision(AgentAction.UseItem(), "drink while the window is open");

            if (distance <= _attackRange && observation.Player.Stamina >= _attackStaminaCost)
                return new AgentDecision(AgentAction.Attack(), "attack target in range");

            return new AgentDecision(AgentAction.Move(directionToTarget, 0.1f), "approach target");
        }

        private bool IsHurt(AgentObservation observation)
        {
            float max = observation.Player.MaxHitPoints;
            return max > 0f && observation.Player.HitPoints / max <= _healBelowFraction;
        }

        private static float DistanceX(AgentObservation observation, EnemyObservation enemy)
        {
            return System.Math.Abs(enemy.Position.X - observation.Player.Position.X);
        }
    }
}
