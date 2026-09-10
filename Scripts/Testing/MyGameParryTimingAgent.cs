using System.Linq;
using MyGame.Core;
using UnityTestAgent.Agent;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace MyGame.Testing
{
    /// <summary>
    /// Parries anything winding up inside reach and otherwise hands the frame to
    /// <see cref="SimpleCombatAgent"/>.
    /// </summary>
    /// <remarks>
    /// UNITS: ranges are Godot pixels, because that is what <see cref="MyGameStateProbe"/> reports. The
    /// defaults are Unity's authored metres written out (a C# default value cannot call
    /// <see cref="World.U"/>), and the fallback agent is constructed with a converted range for the
    /// same reason - the package's own 1.5f default is metres and would put its attack range one and a
    /// half pixels from the player's nose.
    /// </remarks>
    public sealed class MyGameParryTimingAgent : IUnityTestAgent
    {
        /// <summary>Unity's <c>SimpleCombatAgent</c> defaults with the one spatial field converted.</summary>
        private static readonly SimpleCombatAgent Fallback = new(World.U(1.5f));

        private readonly float parryRange;
        private readonly float parryStaminaCost;

        /// <param name="parryRange">Pixels. 170 is Unity's authored 1.7 m.</param>
        public MyGameParryTimingAgent(float parryRange = 170f, float parryStaminaCost = 15f)
        {
            this.parryRange = parryRange;
            this.parryStaminaCost = parryStaminaCost;
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (!observation.Player.IsAlive)
                return new AgentDecision(AgentAction.Idle(), "player dead");

            var threat = observation.Enemies
                .Where(enemy => enemy.IsAlive && enemy.IsAttacking)
                .OrderBy(enemy => DistanceX(observation, enemy.Position))
                .FirstOrDefault();

            if (threat != null && DistanceX(observation, threat.Position) <= parryRange && observation.Player.Stamina >= parryStaminaCost)
                return new AgentDecision(new AgentAction { Type = AgentActionType.Parry, Strength = 1f }, "parry nearby attack");

            return Fallback.Decide(observation);
        }

        private static float DistanceX(AgentObservation observation, Vector2Observation target)
        {
            return System.Math.Abs(target.X - observation.Player.Position.X);
        }
    }
}
