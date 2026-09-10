using System.Linq;
using MyGame.Core;
using UnityTestAgent.Agent;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace MyGame.Testing
{
    /// <summary>
    /// Dodges what is winding up, and otherwise closes to heavy-attack range.
    /// </summary>
    /// <remarks>
    /// UNITS: <see cref="MyGameStateProbe"/> reports positions in Godot pixels, so every range default
    /// below is the Unity metre figure through <see cref="World.U"/>. Stamina costs are not spatial and
    /// are unchanged.
    /// </remarks>
    public sealed class MyGameDodgeHeavyAttackAgent : IUnityTestAgent
    {
        private readonly float attackRange;
        private readonly float dodgeStaminaCost;
        private readonly float heavyAttackStaminaCost;

        /// <param name="attackRange">
        /// Pixels. The default is Unity's authored 1.3 m written out, because a C# default value cannot
        /// call <see cref="World.U"/>. <see cref="World.Ppu"/> is a compile-time constant of 100, so the
        /// literal and the conversion cannot drift apart on their own.
        /// </param>
        public MyGameDodgeHeavyAttackAgent(float attackRange = 130f, float dodgeStaminaCost = 25f, float heavyAttackStaminaCost = 35f)
        {
            this.attackRange = attackRange;
            this.dodgeStaminaCost = dodgeStaminaCost;
            this.heavyAttackStaminaCost = heavyAttackStaminaCost;
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (!observation.Player.IsAlive)
                return new AgentDecision(AgentAction.Idle(), "player dead");

            var target = observation.Enemies
                .Where(enemy => enemy.IsAlive)
                .OrderBy(enemy => DistanceX(observation, enemy.Position))
                .FirstOrDefault();

            if (target == null)
                return new AgentDecision(AgentAction.Idle(), "no target");

            float direction = target.Position.X >= observation.Player.Position.X ? 1f : -1f;
            float distance = DistanceX(observation, target.Position);

            if (target.IsAttacking && observation.Player.Stamina >= dodgeStaminaCost)
                return new AgentDecision(AgentAction.Dodge(-direction), "dodge active threat");

            if (distance <= attackRange && observation.Player.Stamina >= heavyAttackStaminaCost)
                return new AgentDecision(AgentAction.HeavyAttack(), "heavy attack safe nearby target");

            return new AgentDecision(AgentAction.Move(direction, 0.1f), "approach target");
        }

        private static float DistanceX(AgentObservation observation, Vector2Observation target)
        {
            return System.Math.Abs(target.X - observation.Player.Position.X);
        }
    }
}
