using UnityTestAgent.Observation;

namespace UnityTestAgent.Scenario
{
    public sealed class SurviveForTicksCondition : IScenarioCondition
    {
        private readonly int requiredTicks;

        public SurviveForTicksCondition(int requiredTicks)
        {
            this.requiredTicks = requiredTicks;
        }

        public bool IsMet(AgentObservation observation)
        {
            return observation.Player.IsAlive && observation.FrameIndex >= requiredTicks;
        }

        public string Describe()
        {
            return "Survived for " + requiredTicks + " ticks";
        }
    }

    public sealed class NoDamageForTicksCondition : IScenarioCondition, IResettableScenarioCondition
    {
        private readonly int requiredTicks;
        private bool hasBaseline;
        private int lastHitPoints;
        private int stableTicks;

        public NoDamageForTicksCondition(int requiredTicks)
        {
            this.requiredTicks = requiredTicks;
        }

        public bool IsMet(AgentObservation observation)
        {
            if (!hasBaseline)
            {
                hasBaseline = true;
                lastHitPoints = observation.Player.HitPoints;
                stableTicks = 1;
                return stableTicks >= requiredTicks;
            }

            if (observation.Player.HitPoints == lastHitPoints)
            {
                stableTicks++;
            }
            else
            {
                lastHitPoints = observation.Player.HitPoints;
                stableTicks = 1;
            }

            return stableTicks >= requiredTicks;
        }

        public string Describe()
        {
            return "No damage for " + requiredTicks + " ticks";
        }

        public void Reset()
        {
            hasBaseline = false;
            lastHitPoints = 0;
            stableTicks = 0;
        }
    }
}
