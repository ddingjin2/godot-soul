using System.Text;
using UnityTestAgent.Replay;
using UnityTestAgent.Scenario;

namespace UnityTestAgent.Reporting
{
    internal static class ReportFormatter
    {
        public static string BuildMarkdown(ScenarioResult result, ReplayRecording replay, bool includeHash)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# UnityTestAgent Scenario Report");
            builder.AppendLine();
            builder.AppendLine("- Scenario: " + result.ScenarioId);
            builder.AppendLine("- Status: " + result.Status);
            builder.AppendLine("- Ticks: " + result.Ticks);
            builder.AppendLine("- Reason: " + result.Reason);
            builder.AppendLine("- Seed: " + replay.Seed);
            builder.AppendLine("- Replay frames: " + replay.Frames.Count);
            if (includeHash)
            {
                builder.AppendLine("- Replay Hash: " + ReplaySerializer.StableHash(replay));
            }

            AppendRecentFrames(builder, replay);
            return builder.ToString();
        }

        public static string BuildSummaryJson(ScenarioResult result, ReplayRecording replay, bool includeHash)
        {
            var builder = new StringBuilder();
            builder.Append("{");
            AppendJsonProperty(builder, "scenarioId", result.ScenarioId);
            builder.Append(",");
            AppendJsonProperty(builder, "status", result.Status.ToString());
            builder.Append(",");
            builder.Append("\"ticks\":").Append(result.Ticks);
            builder.Append(",");
            AppendJsonProperty(builder, "reason", result.Reason);
            builder.Append(",");
            builder.Append("\"seed\":").Append(replay.Seed);
            builder.Append(",");
            builder.Append("\"replayFrames\":").Append(replay.Frames.Count);
            if (includeHash)
            {
                builder.Append(",");
                AppendJsonProperty(builder, "replayHash", ReplaySerializer.StableHash(replay));
            }

            builder.Append("}");
            return builder.ToString();
        }

        private static void AppendRecentFrames(StringBuilder builder, ReplayRecording replay)
        {
            builder.AppendLine();
            builder.AppendLine("## Recent Frames");
            var start = replay.Frames.Count > 5 ? replay.Frames.Count - 5 : 0;
            for (var i = start; i < replay.Frames.Count; i++)
            {
                var frame = replay.Frames[i];
                builder.AppendLine("- Tick " + frame.Tick + ": " + frame.Action.Type);
            }
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, string value)
        {
            builder.Append("\"").Append(name).Append("\":\"").Append(JsonText.Escape(value)).Append("\"");
        }
    }
}
