using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Replay
{
    public sealed class ReplayRecorder
    {
        private readonly ReplayRecording recording;

        public ReplayRecorder(string scenarioId, int seed)
        {
            recording = new ReplayRecording(scenarioId, seed);
        }

        public void Record(int tick, AgentObservation observation, AgentAction action)
        {
            recording.Frames.Add(new ReplayFrame
            {
                Tick = tick,
                Observation = observation,
                Action = action
            });
        }

        public ReplayRecording ToReplay()
        {
            return recording;
        }
    }
}
