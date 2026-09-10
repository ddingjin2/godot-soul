using UnityTestAgent.Observation;

namespace UnityTestAgent.Scenario
{
    public sealed class PlayerAliveCondition : IScenarioCondition
    {
        public bool IsMet(AgentObservation observation)
        {
            return observation.Player.IsAlive;
        }

        public string Describe()
        {
            return "Player is alive";
        }
    }

    public sealed class PlayerDeadCondition : IScenarioCondition
    {
        public bool IsMet(AgentObservation observation)
        {
            return !observation.Player.IsAlive;
        }

        public string Describe()
        {
            return "Player is dead";
        }
    }

    /// <summary>
    /// The one file in this copied package that is not verbatim: Unity's +Y was up, so "fell out of the
    /// level" was <c>Position.Y &lt; minY</c>. Godot's +Y is down, so the same question is
    /// <c>Position.Y &gt; maxY</c> - the sign flip and operator flip the porting guide calls for on
    /// <c>fallDeathY</c>. The constructor still takes two floats in the same order, so no caller
    /// changed shape; a Unity floor of -20 m is a Godot ceiling of +2000 px.
    /// </summary>
    public sealed class OutOfBoundsCondition : IScenarioCondition
    {
        private readonly float maxY;
        private readonly float maxAbsX;

        /// <param name="maxY">Godot pixels, +Y down. Below this and the player has fallen out.</param>
        /// <param name="maxAbsX">Godot pixels.</param>
        public OutOfBoundsCondition(float maxY, float maxAbsX)
        {
            this.maxY = maxY;
            this.maxAbsX = maxAbsX;
        }

        public bool IsMet(AgentObservation observation)
        {
            return observation.Player.Position.Y > maxY
                || System.Math.Abs(observation.Player.Position.X) > maxAbsX;
        }

        public string Describe()
        {
            return "Player out of bounds";
        }
    }

    public sealed class NotStuckCondition : IScenarioCondition, IResettableScenarioCondition
    {
        private readonly StuckDetector detector;

        public NotStuckCondition(int requiredStillTicks, float positionEpsilon)
        {
            detector = new StuckDetector(requiredStillTicks, positionEpsilon);
        }

        public bool IsMet(AgentObservation observation)
        {
            return !detector.Update(observation);
        }

        public string Describe()
        {
            return "Player is not stuck";
        }

        public void Reset()
        {
            detector.Reset();
        }
    }
}
