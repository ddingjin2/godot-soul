using System;
using System.IO;
using Godot;

namespace MyGame.Testing
{
    /// <summary>
    /// Where a scenario run leaves its report, replay and summary.
    /// </summary>
    /// <remarks>
    /// Unity built this under <c>Application.dataPath/../Logs/UnityTestAgent</c> - the project folder,
    /// which in a Unity editor run is writable and in a player build is not. Godot has an explicit
    /// answer for "somewhere this run may write": <c>user://</c>. So the reports land in
    /// <c>user://Logs/UnityTestAgent/&lt;scenario&gt;-&lt;timestamp&gt;/</c>, which is the real folder
    /// on every platform and exists whether the run is an editor session, a headless test run or an
    /// exported build.
    ///
    /// The path is globalised on the way out because <see cref="FileSystemReporter"/> and everything
    /// else downstream is <c>System.IO</c>, which has never heard of a <c>user://</c> scheme.
    /// </remarks>
    public static class MyGameUnityTestAgentPaths
    {
        /// <summary>The <c>res://</c>-style root, for callers that want to hand it to Godot's own file API.</summary>
        public const string ReportRoot = "user://Logs/UnityTestAgent";

        public static string CreateReportDirectory(string scenarioId)
        {
            string safeScenario = MakeSafePathSegment(scenarioId);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            return Path.Combine(
                ProjectSettings.GlobalizePath(ReportRoot),
                safeScenario + "-" + timestamp);
        }

        private static string MakeSafePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "scenario";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '-');

            return value.Replace(' ', '-');
        }
    }
}
