namespace UnityTestAgent.Replay
{
    public sealed class ReplaySerializationOptions
    {
        public static readonly ReplaySerializationOptions Default = new ReplaySerializationOptions();

        public bool IncludeObservations = true;
    }
}
