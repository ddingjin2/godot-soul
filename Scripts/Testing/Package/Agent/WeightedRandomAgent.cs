using System.Collections.Generic;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public sealed class WeightedRandomAgent : IUnityTestAgent
    {
        private readonly SeededRandomSource random;
        private readonly List<Entry> entries = new List<Entry>();

        public WeightedRandomAgent(int seed)
        {
            random = new SeededRandomSource(seed);
        }

        public WeightedRandomAgent Add(AgentAction action, int weight)
        {
            entries.Add(new Entry(action, weight));
            return this;
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            var total = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Weight > 0)
                {
                    total += entries[i].Weight;
                }
            }

            if (total <= 0)
            {
                return new AgentDecision(AgentAction.Idle(), "no weighted actions");
            }

            var roll = random.Range(0, total);
            var cursor = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Weight <= 0)
                {
                    continue;
                }

                cursor += entries[i].Weight;
                if (roll < cursor)
                {
                    return new AgentDecision(entries[i].Action, "weighted random");
                }
            }

            return new AgentDecision(AgentAction.Idle(), "weighted random fallback");
        }

        private struct Entry
        {
            public readonly AgentAction Action;
            public readonly int Weight;

            public Entry(AgentAction action, int weight)
            {
                Action = action;
                Weight = weight;
            }
        }
    }
}
