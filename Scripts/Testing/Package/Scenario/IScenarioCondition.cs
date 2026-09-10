using UnityTestAgent.Observation;

namespace UnityTestAgent.Scenario
{
    public interface IScenarioCondition
    {
        bool IsMet(AgentObservation observation);
        string Describe();
    }
}
