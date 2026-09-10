using UnityTestAgent.Core;

namespace UnityTestAgent.Input
{
    public interface IAgentInputDriver
    {
        void Apply(AgentAction action);
    }
}
