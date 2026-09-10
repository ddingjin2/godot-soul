using System.Collections.Generic;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public sealed class RouteFollowingAgent : IUnityTestAgent
    {
        private readonly List<Vector2Observation> waypoints;
        private readonly float arrivalRadius;
        private int index;

        public RouteFollowingAgent(IEnumerable<Vector2Observation> waypoints, float arrivalRadius)
        {
            this.waypoints = new List<Vector2Observation>(waypoints);
            this.arrivalRadius = arrivalRadius;
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (index >= waypoints.Count)
            {
                return new AgentDecision(AgentAction.Idle(), "route complete");
            }

            while (index < waypoints.Count
                && DistanceSquared(observation.Player.Position, waypoints[index]) <= arrivalRadius * arrivalRadius)
            {
                index++;
                if (index >= waypoints.Count)
                {
                    return new AgentDecision(AgentAction.Interact(), "route destination reached");
                }
            }

            var target = waypoints[index];
            var direction = target.X >= observation.Player.Position.X ? 1f : -1f;
            return new AgentDecision(AgentAction.Move(direction, 0.1f), "follow route");
        }

        private static float DistanceSquared(Vector2Observation a, Vector2Observation b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
