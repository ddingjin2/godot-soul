using UnityTestAgent.Replay;
using UnityTestAgent.Scenario;

namespace UnityTestAgent.Reporting
{
    public interface IUnityTestReporter
    {
        void Report(ScenarioResult result, ReplayRecording replay);
    }
}
