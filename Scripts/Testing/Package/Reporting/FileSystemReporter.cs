using System.IO;
using System.Text;
using UnityTestAgent.Replay;
using UnityTestAgent.Scenario;

namespace UnityTestAgent.Reporting
{
    public sealed class FileSystemReporter : IUnityTestReporter
    {
        private readonly string directory;

        public FileSystemReporter(string directory)
        {
            this.directory = directory;
        }

        public void Report(ScenarioResult result, ReplayRecording replay)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "summary.json"), ReportFormatter.BuildSummaryJson(result, replay, includeHash: true), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "report.md"), ReportFormatter.BuildMarkdown(result, replay, includeHash: true), Encoding.UTF8);
            ReplaySerializer.Save(Path.Combine(directory, "replay.json"), replay);
        }
    }
}
