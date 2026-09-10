using System.Collections.Generic;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public sealed class ScriptedAgent : IUnityTestAgent
    {
        private readonly Queue<AgentAction> actions;

        public ScriptedAgent(IEnumerable<AgentAction> actions)
        {
            this.actions = new Queue<AgentAction>(actions);
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (actions.Count == 0)
            {
                return new AgentDecision(AgentAction.Idle(), "script exhausted");
            }

            return new AgentDecision(actions.Dequeue(), "scripted");
        }
    }
}
