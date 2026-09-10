using System;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public sealed class SeededRandomAgent : IUnityTestAgent
    {
        private readonly Random random;

        public SeededRandomAgent(int seed)
        {
            random = new Random(seed);
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            switch (random.Next(0, 5))
            {
                case 0:
                    return new AgentDecision(AgentAction.Move(-1f, 0.1f), "seeded random move left");
                case 1:
                    return new AgentDecision(AgentAction.Move(1f, 0.1f), "seeded random move right");
                case 2:
                    return new AgentDecision(AgentAction.Jump(), "seeded random jump");
                case 3:
                    return new AgentDecision(AgentAction.Dodge(random.Next(0, 2) == 0 ? -1f : 1f), "seeded random dodge");
                default:
                    return new AgentDecision(AgentAction.Attack(), "seeded random attack");
            }
        }
    }
}
