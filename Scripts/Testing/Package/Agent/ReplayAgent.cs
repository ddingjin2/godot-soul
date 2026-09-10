using System.Collections.Generic;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;
using UnityTestAgent.Replay;

namespace UnityTestAgent.Agent
{
    public sealed class ReplayAgent : IUnityTestAgent
    {
        private readonly Queue<ReplayFrame> frames;

        public ReplayAgent(ReplayRecording replay)
        {
            frames = new Queue<ReplayFrame>(replay.Frames);
        }

        public AgentDecision Decide(AgentObservation observation)
        {
            if (frames.Count == 0)
            {
                return new AgentDecision(AgentAction.Idle(), "replay exhausted");
            }

            return new AgentDecision(frames.Dequeue().Action, "replay");
        }
    }
}
