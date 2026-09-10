using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// The three Unity idioms the runtime world-builders lean on that Godot has no direct member for:
    /// <c>new GameObject(name)</c>, <c>AddComponent&lt;T&gt;()</c> and <c>SetActive(bool)</c>, plus the
    /// active scene's name.
    ///
    /// Unity's <c>new GameObject</c> dropped the object into the active scene's root with no parent
    /// named; the Godot equivalent is <see cref="SceneTree.CurrentScene"/>, so that is what
    /// <see cref="SceneRoot"/> resolves to. <see cref="Root"/> overrides it, which is how a headless
    /// test builds an arena into a node it owns instead of into whatever scene happens to be open.
    ///
    /// Added by the Gameplay/arena port. The other Gameplay files build the same way, so use this
    /// rather than growing a second copy.
    /// </summary>
    public static class GameplayBuildShim
    {
        /// <summary>Where <see cref="NewObject{T}(string,Vector2)"/> parents new objects. Null means the open scene.</summary>
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
        /// project used, because both came from <c>GameplayScene</c>/<c>Chapter0N_Colour</c>.
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
        /// Unity <c>new GameObject(name)</c> at a world position, parented into the open scene.
        ///
        /// The position is written <i>before</i> the node enters the tree on purpose: a body that is
        /// added at the origin and moved afterwards sweeps from the origin to its destination on the
        /// first physics step, hitting whatever stands in between. Same trap Unity's
        /// <c>Instantiate(prefab, position, rotation)</c> existed to avoid.
        /// </summary>
        public static T NewObject<T>(string name, Vector2 position) where T : Node2D, new()
        {
            var node = new T { Name = name, Position = position };
            SceneRoot?.AddChild(node);
            return node;
        }

        /// <summary>Unity <c>new GameObject(name)</c> with no position of its own.</summary>
        public static T NewObject<T>(string name) where T : Node, new()
        {
            var node = new T { Name = name };
            SceneRoot?.AddChild(node);
            return node;
        }

        /// <summary>
        /// Unity <c>AddComponent&lt;T&gt;()</c>. A Unity component on a GameObject is a child node of the
        /// actor root here, which is the shape <see cref="NodeExt.GetComponent{T}"/> reads back.
        /// </summary>
        public static T AddComponent<T>(this Node parent, string name = null) where T : Node, new()
        {
            var node = new T { Name = name ?? typeof(T).Name };
            parent.AddChild(node);
            return node;
        }

        /// <summary>Unity <c>GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()</c>, which is most of the spawners.</summary>
        public static T EnsureComponent<T>(this Node parent, string name = null) where T : Node, new()
        {
            return parent.GetComponent<T>() ?? parent.AddComponent<T>(name);
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
