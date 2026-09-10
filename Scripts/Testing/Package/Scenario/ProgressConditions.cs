using UnityTestAgent.Observation;

namespace UnityTestAgent.Scenario
{
    public sealed class ReachedCheckpointCondition : IScenarioCondition
    {
        private readonly string checkpointId;

        public ReachedCheckpointCondition(string checkpointId)
        {
            this.checkpointId = checkpointId;
        }

        public bool IsMet(AgentObservation observation)
        {
            return observation.Player.CheckpointId == checkpointId;
        }

        public string Describe()
        {
            return "Reached checkpoint: " + checkpointId;
        }
    }

    public sealed class ReachedPositionCondition : IScenarioCondition
    {
        private readonly Vector2Observation target;
        private readonly float radius;

        public ReachedPositionCondition(Vector2Observation target, float radius)
        {
            this.target = target;
            this.radius = radius;
        }

        public bool IsMet(AgentObservation observation)
        {
            var dx = observation.Player.Position.X - target.X;
            var dy = observation.Player.Position.Y - target.Y;
            return dx * dx + dy * dy <= radius * radius;
        }

        public string Describe()
        {
            return "Reached position";
        }
    }

    public sealed class SceneLoadedCondition : IScenarioCondition
    {
        private readonly string sceneName;

        public SceneLoadedCondition(string sceneName)
        {
            this.sceneName = sceneName;
        }

        public bool IsMet(AgentObservation observation)
        {
            return observation.Scene.SceneName == sceneName && !observation.Scene.IsLoading;
        }

        public string Describe()
        {
            return "Scene loaded: " + sceneName;
        }
    }
}
