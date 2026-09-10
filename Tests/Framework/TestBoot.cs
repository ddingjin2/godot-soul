using Godot;

namespace MyGame.Tests
{
    /// <summary>
    /// The scene Godot boots for a test run. Its only job is to put the <see cref="TestRunner"/>
    /// somewhere a scene change cannot free it.
    ///
    /// This exists because the obvious arrangement does not work: if the runner *is* the current
    /// scene, the first `LoadScene` in a test calls `ChangeSceneToFile`, which frees the current
    /// scene - the runner - out from under the very test that asked for the scene. The run then dies
    /// with "Parent node is busy adding/removing children" or a null tree. Parenting the runner to
    /// the root window instead makes it a sibling of whatever scene is loaded, so it survives every
    /// scene change for the length of the run.
    /// </summary>
    public partial class TestBoot : Node
    {
        public override void _Ready()
        {
            var runner = new TestRunner { Name = "TestRunner" };

            // Deferred: the tree is still busy setting this scene up, and AddChild during that is
            // refused. By the time it lands, the boot scene is idle and the runner starts on its own.
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, runner);
        }
    }
}
