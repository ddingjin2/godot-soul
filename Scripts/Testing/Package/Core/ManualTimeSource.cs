namespace UnityTestAgent.Core
{
    public sealed class ManualTimeSource : ITimeSource
    {
        public float ElapsedSeconds { get; private set; }
        public int FrameIndex { get; private set; }

        public void Advance(float deltaSeconds)
        {
            ElapsedSeconds += deltaSeconds;
            FrameIndex++;
        }
    }
}
