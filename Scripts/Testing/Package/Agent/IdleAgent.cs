using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public sealed class IdleAgent : IUnityTestAgent
    {
        public AgentDecision Decide(AgentObservation observation)
        {
            return new AgentDecision(AgentAction.Idle(), "idle");
        }
    }
}
