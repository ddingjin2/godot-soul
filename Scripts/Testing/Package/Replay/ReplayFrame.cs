using System;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Replay
{
    [Serializable]
    public sealed class ReplayFrame
    {
        public int Tick;
        public AgentObservation Observation;
        public AgentAction Action;
    }
}
