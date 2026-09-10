using System;

namespace UnityTestAgent.Scenario
{
    [Serializable]
    public sealed class ScenarioResult
    {
        public string ScenarioId;
        public ScenarioStatus Status;
        public int Ticks;
        public string Reason;

        public static ScenarioResult Pass(string scenarioId, int ticks, string reason)
        {
            return Create(scenarioId, ScenarioStatus.Passed, ticks, reason);
        }

        public static ScenarioResult Fail(string scenarioId, int ticks, string reason)
        {
            return Create(scenarioId, ScenarioStatus.Failed, ticks, reason);
        }

        public static ScenarioResult Timeout(string scenarioId, int ticks)
        {
            return Create(scenarioId, ScenarioStatus.TimedOut, ticks, "Timed out.");
        }

        private static ScenarioResult Create(string scenarioId, ScenarioStatus status, int ticks, string reason)
        {
            return new ScenarioResult
            {
                ScenarioId = scenarioId,
                Status = status,
                Ticks = ticks,
                Reason = reason
            };
        }
    }
}
