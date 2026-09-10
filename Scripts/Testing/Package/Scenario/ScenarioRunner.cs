using UnityTestAgent.Agent;
using UnityTestAgent.Input;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Scenario
{
    public sealed class ScenarioRunner
    {
        public ScenarioResult Run(
            UnityTestScenario scenario,
            IUnityTestAgent agent,
            IGameStateProbe probe,
            IAgentInputDriver inputDriver)
        {
            return Run(scenario, agent, probe, inputDriver, ScenarioRunOptions.None);
        }

        public ScenarioResult Run(
            UnityTestScenario scenario,
            IUnityTestAgent agent,
            IGameStateProbe probe,
            IAgentInputDriver inputDriver,
            ScenarioRunOptions options)
        {
            if (options == null)
            {
                options = ScenarioRunOptions.None;
            }

            if (options.Setup != null)
            {
                options.Setup.ResetScenario();
            }

            ResetConditions(scenario);

            for (var tick = 1; tick <= scenario.MaxTicks; tick++)
            {
                var observation = probe.Capture();
                observation.FrameIndex = tick;
                var decision = agent.Decide(observation);
                inputDriver.Apply(decision.Action);

                if (options.ReplayRecorder != null)
                {
                    options.ReplayRecorder.Record(tick, observation, decision.Action);
                }

                foreach (var condition in scenario.FailureConditions)
                {
                    if (condition.IsMet(observation))
                    {
                        return Finish(
                            ScenarioResult.Fail(scenario.Id, tick, condition.Describe()),
                            options);
                    }
                }

                foreach (var condition in scenario.SuccessConditions)
                {
                    if (condition.IsMet(observation))
                    {
                        return Finish(
                            ScenarioResult.Pass(scenario.Id, tick, condition.Describe()),
                            options);
                    }
                }
            }

            return Finish(ScenarioResult.Timeout(scenario.Id, scenario.MaxTicks), options);
        }

        private static ScenarioResult Finish(ScenarioResult result, ScenarioRunOptions options)
        {
            if (options.Reporter != null)
            {
                var replay = options.ReplayRecorder != null
                    ? options.ReplayRecorder.ToReplay()
                    : new Replay.ReplayRecording(result.ScenarioId, 0);
                options.Reporter.Report(result, replay);
            }

            return result;
        }

        private static void ResetConditions(UnityTestScenario scenario)
        {
            ResetConditions(scenario.FailureConditions);
            ResetConditions(scenario.SuccessConditions);
        }

        private static void ResetConditions(System.Collections.Generic.IEnumerable<IScenarioCondition> conditions)
        {
            foreach (var condition in conditions)
            {
                var resettable = condition as IResettableScenarioCondition;
                if (resettable != null)
                {
                    resettable.Reset();
                }
            }
        }
    }
}
