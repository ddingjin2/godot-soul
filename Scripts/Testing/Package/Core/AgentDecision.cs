using System;

namespace UnityTestAgent.Core
{
    [Serializable]
    public struct AgentDecision
    {
        public AgentAction Action;
        public string Reason;

        public AgentDecision(AgentAction action, string reason = "")
        {
            Action = action;
            Reason = reason;
        }
    }
}
