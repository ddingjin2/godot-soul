using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public interface IUnityTestAgent
    {
        AgentDecision Decide(AgentObservation observation);
    }
}
