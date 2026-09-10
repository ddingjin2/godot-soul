using System.Threading.Tasks;
using Godot;
using MyGame.Gameplay;
using MyGame.Testing;

namespace MyGame.Tests
{
    /// <summary>
    /// The self-driving runtime node, in the real gameplay scene.
    /// </summary>
    /// <remarks>
    /// The Unity version reached every member through reflection so that per-module assembly
    /// definitions could not break the lookups. Godot builds one assembly, so the driver is named
    /// directly and a rename fails at compile time.
    /// </remarks>
    public sealed class UnityTestAgentRuntimeDriverTests
    {
        private const string GameplayScenePath = "res://Scenes/GameplayScene.tscn";

        private Node _host;

        /// <summary>
        /// The driver raises <see cref="CutsceneDirector.SkipAll"/> while it holds the controls and only
        /// lowers it in <c>StopAgent</c>. Left raised, the static outlives this scene and every later
        /// cutscene resolves without playing - so any fixture that waits for the entry shot goes green
        /// having never seen one. That is the quiet kind of pass.
        ///
        /// The driver's own <c>_ExitTree</c> now calls <c>StopAgent</c>, so freeing the host is enough;
        /// the reset below is the belt to that braces.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (GodotObject.IsInstanceValid(_host))
                _host.QueueFree();

            _host = null;
            CutsceneDirector.SkipAll = false;
        }

        [Test]
        public async Task RuntimeDriver_AutoStartsAndAppliesAgentInput_InRealGameplayScene()
        {
            Node scene = await TestContext.Runner.LoadScene(GameplayScenePath);

            _host = new MyGameRuntimeAgentDriver { Name = "RuntimeAgentDriverTestHost" };
            scene.AddChild(_host);

            await TestContext.Runner.NextPhysicsFrame();
            await TestContext.Runner.NextPhysicsFrame();

            var runner = (MyGameRuntimeAgentDriver)_host;
            Assert.IsTrue(runner.IsRunning, "Runtime agent driver should start in a gameplay scene.");
            Assert.NotNull(runner.ControlledPlayer, "Runtime agent driver should find the spawned player.");
            Assert.NotNull(runner.TargetEnemy, "Runtime agent driver should target a live enemy.");
            Assert.Greater(runner.AppliedActionCount, 0, "Runtime agent driver should apply at least one agent action.");
        }
    }
}
