using UnityTestAgent.Replay;
using UnityTestAgent.Scenario;

namespace UnityTestAgent.Reporting
{
    public sealed class MemoryReporter : IUnityTestReporter
    {
        public string LastMarkdown { get; private set; }
        public string LastJson { get; private set; }

        public MemoryReporter()
        {
            LastMarkdown = "";
            LastJson = "";
        }

        public void Report(ScenarioResult result, ReplayRecording replay)
        {
            LastMarkdown = ReportFormatter.BuildMarkdown(result, replay, includeHash: false);
            LastJson = ReportFormatter.BuildSummaryJson(result, replay, includeHash: false);
        }
    }
}
