#if TOOLS
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Runs the ported test suite headlessly, out of process.
    /// </summary>
    /// <remarks>
    /// Ported from <c>UnityTestAgentRuntimeDriverEditor</c>, which most of the way through was a
    /// workaround: Unity could not attach a runtime driver to a play session from the editor, so it left
    /// a request file in <c>Temp/</c> and picked it up on the next play-mode change. None of that
    /// survives. <c>tools/run-tests.ps1</c> builds and runs <c>res://Tests/TestMain.tscn</c> in a
    /// headless Godot, which owns no editor and takes no lock, so the button just starts it.
    ///
    /// Output goes to the launched console, not the Godot editor log - a test run's output is the point
    /// of the run, and the editor's output panel is where everything else is already shouting.
    /// </remarks>
    public static class TestRunLauncher
    {
        public static void Run()
        {
            string script = ProjectSettings.GlobalizePath("res://tools/run-tests.ps1");
            long pid = OS.CreateProcess("powershell", new[] { "-NoExit", "-ExecutionPolicy", "Bypass", "-File", script }, true);

            if (pid <= 0)
            {
                GD.PushError($"TestRunLauncher: could not start powershell. Run it by hand: powershell -File {script}");
                return;
            }

            GD.Print($"TestRunLauncher: started {script} (pid {pid}). Exit code 0 means every test passed.");
        }
    }
}
#endif
