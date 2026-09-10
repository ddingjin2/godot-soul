using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public sealed class ExplorationAgent : IUnityTestAgent
    {
        private readonly WeightedRandomAgent fallback;

        public ExplorationAgent(int seed)
        {
            fallback = new WeightedRandomAgent(seed)
                .Add(AgentAction.Move(-1f, 0.1f), 3)
                .Add(AgentAction.Move(1f, 0.1f), 3)
                .Add(AgentAction.Jump(), 1)
                .Add(AgentAction.Attack(), 1)
                .Add(AgentAction.Interact(), 1);
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (SoulslikeDecisionUtility.DangerScore(observation) >= 1f)
            {
                return new AgentDecision(SoulslikeDecisionUtility.FilterByStamina(observation, AgentAction.Dodge(-observation.Player.FacingDirection)), "avoid danger");
            }

            for (var i = 0; i < observation.Interactables.Count; i++)
            {
                if (observation.Interactables[i].CanInteract)
                {
                    return new AgentDecision(AgentAction.Interact(), "interact with available object");
                }
            }

            return fallback.Decide(observation);
        }
    }
}
