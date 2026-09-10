using System.Collections.Generic;
using MyGame.Core;
using UnityTestAgent.Agent;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace MyGame.Testing
{
    /// <summary>
    /// Walks a list of route points in order, jumping at anything above it.
    /// </summary>
    /// <remarks>
    /// UNITS AND AXIS: the route points are in the same space
    /// <see cref="MyGameStateProbe"/> reports - Godot pixels, +Y **down**. A caller holding Unity-space
    /// route points converts them with <see cref="World.V"/> before handing them over.
    ///
    /// The one behavioural line that flipped is the jump test. Unity asked <c>dy &gt; 0.8f</c>, meaning
    /// "the next point is 0.8 m higher than me"; higher is a *smaller* Y here, so the same question is
    /// <c>dy &lt; -World.U(0.8f)</c>.
    /// </remarks>
    public sealed class MyGameCheckpointRouteAgent : IUnityTestAgent
    {
        /// <summary>Unity's 0.8 m "the route point is above me" threshold, in pixels.</summary>
        private static readonly float JumpHeightThreshold = World.U(0.8f);

        private readonly IReadOnlyList<Vector2Observation> route;
        private readonly float reachRadius;
        private int routeIndex;

        /// <param name="reachRadius">Pixels. 35 is Unity's authored 0.35 m.</param>
        public MyGameCheckpointRouteAgent(IReadOnlyList<Vector2Observation> route, float reachRadius = 35f)
        {
            this.route = route;
            this.reachRadius = reachRadius;
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (!observation.Player.IsAlive)
                return new AgentDecision(AgentAction.Idle(), "player dead");

            if (route == null || route.Count == 0 || routeIndex >= route.Count)
                return new AgentDecision(AgentAction.Idle(), "route complete");

            Vector2Observation target = route[routeIndex];
            float dx = target.X - observation.Player.Position.X;
            float dy = target.Y - observation.Player.Position.Y;
            if (dx * dx + dy * dy <= reachRadius * reachRadius)
            {
                routeIndex++;
                return new AgentDecision(AgentAction.Idle(), "route point reached");
            }

            if (dy < -JumpHeightThreshold && observation.Player.IsGrounded)
                return new AgentDecision(AgentAction.Jump(), "jump toward elevated route point");

            return new AgentDecision(AgentAction.Move(dx >= 0f ? 1f : -1f, 0.1f), "move toward route point");
        }
    }
}
