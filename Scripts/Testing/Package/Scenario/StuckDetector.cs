using UnityTestAgent.Observation;

namespace UnityTestAgent.Scenario
{
    public sealed class StuckDetector
    {
        private readonly int requiredStillTicks;
        private readonly float positionEpsilon;
        private bool hasLastPosition;
        private Vector2Observation lastPosition;
        private int stillTicks;

        public StuckDetector(int requiredStillTicks, float positionEpsilon)
        {
            this.requiredStillTicks = requiredStillTicks;
            this.positionEpsilon = positionEpsilon;
        }

        public bool Update(AgentObservation observation)
        {
            var current = observation.Player.Position;
            if (!hasLastPosition)
            {
                lastPosition = current;
                hasLastPosition = true;
                stillTicks = 1;
                return false;
            }

            if (DistanceSquared(lastPosition, current) <= positionEpsilon * positionEpsilon)
            {
                stillTicks++;
            }
            else
            {
                stillTicks = 1;
                lastPosition = current;
            }

            return stillTicks >= requiredStillTicks;
        }

        public void Reset()
        {
            hasLastPosition = false;
            lastPosition = new Vector2Observation();
            stillTicks = 0;
        }

        private static float DistanceSquared(Vector2Observation a, Vector2Observation b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
