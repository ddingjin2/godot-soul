using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// The Unity idioms the arena code still leans on that Godot has no direct member for: the active
    /// scene's root and name, and <c>SetActive(bool)</c>.
    ///
    /// Unity's <c>new GameObject</c> dropped the object into the active scene's root with no parent
    /// named; the Godot equivalent is <see cref="SceneTree.CurrentScene"/>, so that is what
    /// <see cref="SceneRoot"/> resolves to. <see cref="Root"/> overrides it, which is how a headless
    /// test builds an arena into a node it owns instead of into whatever scene happens to be open.
    ///
    /// <b>This was the enabler for building the world in code, and K7 took that half away.</b>
    /// <c>NewObject</c>, <c>AddComponent</c> and <c>EnsureComponent</c> are gone: every reusable thing
    /// is a saved scene the spawners instance, and the shell and the actor scenes author the components
    /// the spawners used to add. What replaced <c>EnsureComponent</c> is
    /// <see cref="RequireComponent{T}"/> - the same lookup with the <c>AddComponent</c> half turned into
    /// an error, which is the missing-data policy applied to scene structure (PLAN_CLOSEOUT D1/K7).
    /// The <c>AddComponent&lt;T&gt;</c> the suites build fixtures with moved to
    /// <c>Tests/Framework/NodeBuild.cs</c>, where a test-only builder belongs.
    /// </summary>
    public static class GameplayBuildShim
    {
        /// <summary>Where <see cref="SceneRoot"/> points instead of the open scene. Null means the open scene.</summary>
        public static Node Root { get; set; }

        private static SceneTree Tree => Engine.GetMainLoop() as SceneTree;

        /// <summary>Unity's implicit "active scene root" that <c>new GameObject</c> parented to.</summary>
        public static Node SceneRoot
        {
            get
            {
                if (Root != null)
                {
                    return Root;
                }

                SceneTree tree = Tree;
                return (Node)tree?.CurrentScene ?? tree?.Root;
            }
        }

        /// <summary>
        /// Unity's <c>SceneManager.GetActiveScene().name</c>. Godot's scene has a file path rather than a
        /// scene name, so the file's base name is what stands in - which is the same string the Unity
        /// project used, because both came from <c>GameplayScene</c>/<c>Chapter0N_Colour</c>. A chapter
        /// that inherits <c>Scenes/World/GameplayShell.tscn</c> still answers with its own file's name,
        /// because <see cref="Node.SceneFilePath"/> is the file that was loaded, not the base.
        /// </summary>
        public static string ActiveSceneName
        {
            get
            {
                Node current = Tree?.CurrentScene;
                if (current == null)
                {
                    return string.Empty;
                }

                string path = current.SceneFilePath;
                return string.IsNullOrEmpty(path) ? current.Name : path.GetFile().GetBaseName();
            }
        }

        /// <summary>
        /// Unity <c>GetComponent&lt;T&gt;()</c> for a component the actor's scene is supposed to author.
        /// Was <c>GetComponent ?? AddComponent</c>; the add half was the last runtime assembly of an
        /// actor, and since the actor scenes carry every one of these, it only ever ran for a
        /// hand-built fixture. Null with an error naming the actor and the type is the answer now: a
        /// scene that lost a node is a broken build, not a build with a node quietly put back.
        /// </summary>
        /// <param name="owner">Who was asking, for the error line - usually the spawner's type name.</param>
        public static T RequireComponent<T>(this Node actor, string owner) where T : Node
        {
            T found = actor.GetComponent<T>();
            if (found == null)
            {
                GD.PushError($"{owner}: '{actor.Name}' has no {typeof(T).Name}; its scene under Scenes/Actors should author one. The actor is incomplete.");
            }

            return found;
        }

        /// <summary>
        /// Unity <c>gameObject.SetActive(bool)</c>: stop drawing, stop ticking, stop colliding. Godot
        /// splits those three, so all three are written here - a template object that is only hidden
        /// still runs its _Process and still reports overlaps.
        /// </summary>
        public static void SetActive(this Node node, bool active)
        {
            if (node == null)
            {
                return;
            }

            if (node is CanvasItem canvasItem)
            {
                canvasItem.Visible = active;
            }

            // Deferred, like the collision shapes below: Godot refuses to disable a CollisionObject
            // from inside a physics callback ("Disable with call_deferred() instead"), and a soul
            // pickup or a gate deactivating itself from its own BodyEntered is exactly that case.
            // Visibility stays immediate, so the node disappears on the frame the caller asked.
            node.SetDeferred(
                Node.PropertyName.ProcessMode,
                (int)(active ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled));
            SetCollisionEnabled(node, active);
        }

        // Deferred because Godot forbids re-shaping a collider from inside a physics callback, and
        // SetActive is called from death handling, which is one.
        private static void SetCollisionEnabled(Node node, bool enabled)
        {
            if (node is CollisionShape2D shape)
            {
                shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, !enabled);
            }

            foreach (Node child in node.GetChildren())
            {
                SetCollisionEnabled(child, enabled);
            }
        }
    }
}
