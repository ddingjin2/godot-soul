using System;
using System.Collections.Generic;

namespace UnityTestAgent.Scenario
{
    public sealed class UnityTestScenario
    {
        private readonly List<IScenarioCondition> successConditions = new List<IScenarioCondition>();
        private readonly List<IScenarioCondition> failureConditions = new List<IScenarioCondition>();

        public string Id { get; private set; }
        public int MaxTicks { get; private set; }
        public IReadOnlyList<IScenarioCondition> SuccessConditions
        {
            get { return successConditions; }
        }

        public IReadOnlyList<IScenarioCondition> FailureConditions
        {
            get { return failureConditions; }
        }

        public UnityTestScenario(string id, int maxTicks)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Scenario id is required.", "id");
            }

            if (maxTicks <= 0)
            {
                throw new ArgumentOutOfRangeException("maxTicks", "Max ticks must be positive.");
            }

            Id = id;
            MaxTicks = maxTicks;
        }

        public UnityTestScenario WithSuccessCondition(IScenarioCondition condition)
        {
            successConditions.Add(condition);
            return this;
        }

        public UnityTestScenario WithFailureCondition(IScenarioCondition condition)
        {
            failureConditions.Add(condition);
            return this;
        }
    }
}
