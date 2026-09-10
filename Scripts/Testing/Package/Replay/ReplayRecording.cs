using System;
using System.Collections.Generic;

namespace UnityTestAgent.Replay
{
    [Serializable]
    public sealed class ReplayRecording
    {
        public string ScenarioId;
        public int Seed;
        public string PackageVersion = "0.1.0";
        public List<ReplayFrame> Frames = new List<ReplayFrame>();

        public ReplayRecording(string scenarioId, int seed)
        {
            ScenarioId = scenarioId;
            Seed = seed;
        }
    }
}
