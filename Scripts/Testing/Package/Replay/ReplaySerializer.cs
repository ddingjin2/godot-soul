using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityTestAgent.Core;

namespace UnityTestAgent.Replay
{
    public static class ReplaySerializer
    {
        public static string ToJson(ReplayRecording replay)
        {
            return ToJson(replay, ReplaySerializationOptions.Default);
        }

        public static string ToJson(ReplayRecording replay, ReplaySerializationOptions options)
        {
            if (options == null)
            {
                options = ReplaySerializationOptions.Default;
            }

            var builder = new StringBuilder();
            builder.Append("{");
            AppendJsonProperty(builder, "scenarioId", replay.ScenarioId);
            builder.Append(",");
            AppendJsonProperty(builder, "packageVersion", replay.PackageVersion);
            builder.Append(",");
            builder.Append("\"seed\":").Append(replay.Seed.ToString(CultureInfo.InvariantCulture));
            builder.Append(",");
            builder.Append("\"frames\":[");

            for (var i = 0; i < replay.Frames.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                var frame = replay.Frames[i];
                builder.Append("{");
                builder.Append("\"tick\":").Append(frame.Tick.ToString(CultureInfo.InvariantCulture));
                builder.Append(",");
                builder.Append("\"actionType\":\"").Append(frame.Action.Type).Append("\"");
                builder.Append(",");
                builder.Append("\"direction\":").Append(frame.Action.Direction.ToString(CultureInfo.InvariantCulture));
                builder.Append(",");
                builder.Append("\"strength\":").Append(frame.Action.Strength.ToString(CultureInfo.InvariantCulture));
                builder.Append(",");
                builder.Append("\"durationSeconds\":").Append(frame.Action.DurationSeconds.ToString(CultureInfo.InvariantCulture));
                builder.Append(",");
                AppendJsonProperty(builder, "metadata", frame.Action.Metadata);
                if (options.IncludeObservations && frame.Observation != null)
                {
                    builder.Append(",");
                    builder.Append("\"observationFrame\":").Append(frame.Observation.FrameIndex.ToString(CultureInfo.InvariantCulture));
                }
                builder.Append("}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        public static ReplayRecording FromJson(string json)
        {
            var replay = new ReplayRecording(
                MatchString(json, "scenarioId"),
                MatchInt(json, "seed"));
            replay.PackageVersion = MatchString(json, "packageVersion");

            var frameMatches = Regex.Matches(json, "\\{\\\"tick\\\":(\\d+),\\\"actionType\\\":\\\"([^\\\"]+)\\\",\\\"direction\\\":(-?\\d+(?:\\.\\d+)?),\\\"strength\\\":(-?\\d+(?:\\.\\d+)?),\\\"durationSeconds\\\":(-?\\d+(?:\\.\\d+)?),\\\"metadata\\\":\\\"((?:\\\\.|[^\\\"])*)\\\"(?:,\\\"observationFrame\\\":\\d+)?\\}");
            foreach (Match match in frameMatches)
            {
                var action = new AgentAction
                {
                    Type = (AgentActionType)Enum.Parse(typeof(AgentActionType), match.Groups[2].Value),
                    Direction = float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                    Strength = float.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                    DurationSeconds = float.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                    Metadata = JsonText.Unescape(match.Groups[6].Value)
                };

                replay.Frames.Add(new ReplayFrame
                {
                    Tick = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    Action = action
                });
            }

            return replay;
        }

        public static void Save(string path, ReplayRecording replay)
        {
            File.WriteAllText(path, ToJson(replay), Encoding.UTF8);
        }

        public static ReplayRecording Load(string path)
        {
            return FromJson(File.ReadAllText(path, Encoding.UTF8));
        }

        public static string StableHash(ReplayRecording replay)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(ToJson(replay, new ReplaySerializationOptions { IncludeObservations = false }));
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder();
                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, string value)
        {
            builder.Append("\"").Append(name).Append("\":\"").Append(JsonText.Escape(value)).Append("\"");
        }

        private static string MatchString(string json, string name)
        {
            var match = Regex.Match(json, "\\\"" + name + "\\\":\\\"([^\\\"]*)\\\"");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static int MatchInt(string json, string name)
        {
            var match = Regex.Match(json, "\\\"" + name + "\\\":(-?\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }
    }
}
