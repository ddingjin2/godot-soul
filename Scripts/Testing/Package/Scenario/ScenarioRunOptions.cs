using UnityTestAgent.Reporting;
using UnityTestAgent.Replay;

namespace UnityTestAgent.Scenario
{
    public sealed class ScenarioRunOptions
    {
        public static readonly ScenarioRunOptions None = new ScenarioRunOptions();

        public IScenarioSetup Setup;
        public ReplayRecorder ReplayRecorder;
        public IUnityTestReporter Reporter;
    }
}
