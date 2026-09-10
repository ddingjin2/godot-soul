using System;
using UnityTestAgent.Scenario;

namespace MyGame.Testing
{
    public sealed class MyGameScenarioSetup : IScenarioSetup
    {
        private readonly Action _reset;

        public MyGameScenarioSetup(Action reset)
        {
            _reset = reset;
        }

        public void ResetScenario()
        {
            _reset?.Invoke();
        }
    }
}
