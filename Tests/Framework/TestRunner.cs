using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace MyGame.Tests
{
    /// <summary>
    /// Runs every test in the assembly and exits with a non-zero code if any of them failed.
    ///
    /// This replaces the Unity Test Framework, the two PowerShell suite runners and the Unity editor
    /// lock they fought over. There is no lock here: a headless Godot run owns nothing, so several
    /// runs can happen at once.
    ///
    ///   tools/run-tests.ps1                      # everything
    ///   tools/run-tests.ps1 -Filter Checkpoint   # class or method name substring
    /// </summary>
    public partial class TestRunner : Node
    {
        private const int NoTestsRanExitCode = 2;

        private readonly List<string> _failures = new();
        private int _passed;
        private int _ignored;

        public override async void _Ready()
        {
            TestContext.Runner = this;

            // _Ready is async void, so without this the first test would execute inside the tree's own
            // ready propagation - where AddChild is refused and a fixture that builds nodes loses them
            // silently. One frame puts every test on equal, idle footing.
            await NextFrame();

            string filter = ReadFilterArgument();
            var stopwatch = Stopwatch.StartNew();

            List<Type> fixtures = DiscoverFixtures();
            if (fixtures.Count == 0)
            {
                GD.PrintErr("No test fixtures found.");
                GetTree().Quit(NoTestsRanExitCode);
                return;
            }

            foreach (Type fixture in fixtures)
            {
                await RunFixture(fixture, filter);
            }

            stopwatch.Stop();
            Report(stopwatch.Elapsed);
            GetTree().Quit(_failures.Count == 0 ? 0 : 1);
        }

        /// <summary>Waits one rendered frame - the direct equivalent of Unity's <c>yield return null</c>.</summary>
        public async Task NextFrame()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        /// <summary>Waits one physics step, for tests that need a body to have moved.</summary>
        public async Task NextPhysicsFrame()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        /// <summary>Unity's <c>yield return new WaitForSeconds(s)</c>, scaled by the game clock.</summary>
        public async Task Seconds(float seconds)
        {
            await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        }

        /// <summary>Unity's <c>WaitForSecondsRealtime</c> - unaffected by a hit-stop time scale.</summary>
        public async Task RealtimeSeconds(float seconds)
        {
            await ToSignal(
                GetTree().CreateTimer(seconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true),
                SceneTreeTimer.SignalName.Timeout);
        }

        /// <summary>
        /// Unity's <c>yield return new WaitUntil(...)</c>, with the timeout the Unity helpers grew
        /// after a hung condition took a whole suite down with it.
        /// </summary>
        public async Task<bool> WaitUntil(Func<bool> condition, float timeoutSeconds = 5f)
        {
            double deadline = Godot.Time.GetTicksMsec() + timeoutSeconds * 1000.0;
            while (Godot.Time.GetTicksMsec() < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await NextFrame();
            }

            return condition();
        }

        /// <summary>Loads a scene and waits until it is the running one. Unity's <c>LoadGameplayScene</c>.</summary>
        public async Task<Node> LoadScene(string scenePath)
        {
            Error error = GetTree().ChangeSceneToFile(scenePath);
            if (error != Error.Ok)
            {
                throw new AssertionException($"Could not load {scenePath}: {error}");
            }

            // ChangeSceneToFile is deferred to the end of the frame, and the new scene's _Ready runs
            // the frame after that - so two frames, not one, before the tree is usable.
            await NextFrame();
            await NextFrame();
            return GetTree().CurrentScene;
        }

        private static List<Type> DiscoverFixtures()
        {
            return Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(type => !type.IsAbstract && type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(method => method.GetCustomAttribute<TestAttribute>() != null))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();
        }

        private async Task RunFixture(Type fixture, string filter)
        {
            MethodInfo[] methods = fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo setUp = methods.FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);
            MethodInfo tearDown = methods.FirstOrDefault(m => m.GetCustomAttribute<TearDownAttribute>() != null);

            List<MethodInfo> tests = methods
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .Where(m => Matches(fixture, m, filter))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToList();

            if (tests.Count == 0)
            {
                return;
            }

            GD.Print($"--- {fixture.FullName}");

            foreach (MethodInfo test in tests)
            {
                string name = $"{fixture.Name}.{test.Name}";
                object instance;
                try
                {
                    instance = Activator.CreateInstance(fixture);
                }
                catch (Exception e)
                {
                    Record(name, e);
                    continue;
                }

                try
                {
                    await Invoke(setUp, instance);
                    await Invoke(test, instance);
                    _passed++;
                    GD.Print($"  PASS {test.Name}");
                }
                catch (IgnoreException ignore)
                {
                    _ignored++;
                    GD.Print($"  SKIP {test.Name}: {ignore.Message}");
                }
                catch (Exception e)
                {
                    Record(name, e);
                }
                finally
                {
                    try
                    {
                        await Invoke(tearDown, instance);
                    }
                    catch (Exception e)
                    {
                        // A failing teardown hides the real failure if it is allowed to throw here.
                        GD.PushWarning($"{name} teardown threw: {Unwrap(e).Message}");
                    }

                    // QueueFree is deferred, so without a frame here the next test starts with the
                    // previous one's nodes still in the tree - and any assertion that searches the whole
                    // tree quietly finds them. That is the difference between a fixture passing alone
                    // and failing in a full run.
                    await NextFrame();
                }
            }
        }

        private static bool Matches(Type fixture, MethodInfo method, string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            return fixture.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                   || method.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Invokes a test, set-up or teardown method whether it is <c>void</c> (Unity's <c>[Test]</c>)
        /// or returns a <c>Task</c> (what Unity's <c>[UnityTest]</c> coroutines became).
        /// </summary>
        private static async Task Invoke(MethodInfo method, object instance)
        {
            if (method == null)
            {
                return;
            }

            try
            {
                object result = method.Invoke(instance, Array.Empty<object>());
                if (result is Task task)
                {
                    await task;
                }
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // Reflection wraps whatever the test threw; the wrapper is noise in a failure report.
                throw e.InnerException;
            }
        }

        private void Record(string name, Exception e)
        {
            Exception real = Unwrap(e);
            string detail = real is AssertionException ? real.Message : $"{real.GetType().Name}: {real.Message}\n{real.StackTrace}";
            _failures.Add($"{name}\n  {detail}");
            GD.Print($"  FAIL {name}");
        }

        private static Exception Unwrap(Exception e) =>
            e is TargetInvocationException { InnerException: not null } wrapped ? wrapped.InnerException : e;

        private void Report(TimeSpan elapsed)
        {
            GD.Print("");
            foreach (string failure in _failures)
            {
                GD.PrintErr("FAILED " + failure);
            }

            GD.Print("");
            GD.Print($"{_passed} passed, {_failures.Count} failed, {_ignored} skipped in {elapsed.TotalSeconds:F1}s");
        }

        private static string ReadFilterArgument()
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--test-filter="))
                {
                    return arg["--test-filter=".Length..];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// How a test reaches the running tree. Unity tests could call into the engine from anywhere;
    /// here everything that needs a frame, a timer or a scene goes through the runner node.
    /// </summary>
    public static class TestContext
    {
        public static TestRunner Runner { get; internal set; }

        public static SceneTree Tree => Runner?.GetTree();

        public static Node CurrentScene => Runner?.GetTree()?.CurrentScene;
    }
}
