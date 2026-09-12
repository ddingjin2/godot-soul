using Godot;

namespace MyGame.Tests
{
    /// <summary>
    /// Unity's <c>gameObject.AddComponent&lt;T&gt;()</c>, for the suites that build an actor by hand.
    /// </summary>
    /// <remarks>
    /// This lived on <c>MyGame.Core.GameplayBuildShim</c> until K7, beside the spawners' own
    /// <c>EnsureComponent</c>. The spawners stopped adding components when the actor scenes started
    /// authoring them, and what was left was a builder with production callers of zero and fixture
    /// callers of twenty-nine - so it moved here, where a test-only builder belongs (rule 2: shipping
    /// code assembles nothing; production reads what the scenes author).
    /// <para>
    /// The shape it builds is the one <c>NodeExt.GetComponent&lt;T&gt;</c> reads back: a Unity component
    /// on a GameObject is a direct child node of the actor root here, named for its type unless the
    /// caller says otherwise.
    /// </para>
    /// </remarks>
    public static class NodeBuild
    {
        public static T AddComponent<T>(this Node parent, string name = null) where T : Node, new()
        {
            var node = new T { Name = name ?? typeof(T).Name };
            parent.AddChild(node);
            return node;
        }
    }
}
