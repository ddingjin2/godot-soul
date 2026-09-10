namespace UnityTestAgent.Core
{
    public interface ILoggerSink
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }
}
