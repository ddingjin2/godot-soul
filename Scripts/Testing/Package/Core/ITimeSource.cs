namespace UnityTestAgent.Core
{
    public interface ITimeSource
    {
        float ElapsedSeconds { get; }
        int FrameIndex { get; }
    }
}
