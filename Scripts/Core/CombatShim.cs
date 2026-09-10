using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// Unity's <c>GetComponent</c> family, expressed against the Godot scene tree.
    ///
    /// A Unity actor was one GameObject carrying many components; the same actor here is one root node
    /// carrying child nodes (Health, Poise, SoulsWallet, ...). So "a component on this object" becomes
    /// "this node, or one of its direct children", and "a component on this object or an ancestor"
    /// becomes the same test walked up the parent chain.
    ///
    /// One deliberate difference from Unity: because a component is a *child* here,
    /// <see cref="GetComponentInParent{T}"/> can also find a sibling of the node it started from - the
    /// two are children of the same actor root. That is the answer callers want ("the Health of the
    /// actor this hurtbox belongs to"), so it is left in rather than guarded against.
    /// </summary>
    public static class NodeExt
    {
        /// <summary>Unity <c>GetComponent&lt;T&gt;</c>: this node, or a direct child of it.</summary>
        public static T GetComponent<T>(this Node node) where T : class
        {
            if (node == null)
            {
                return null;
            }

            if (node is T self)
            {
                return self;
            }

            foreach (Node child in node.GetChildren())
            {
                if (child is T found)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Unity <c>GetComponentInParent&lt;T&gt;</c>: this node, then each ancestor, children included.</summary>
        public static T GetComponentInParent<T>(this Node node) where T : class
        {
            for (Node current = node; current != null; current = current.GetParent())
            {
                T found = current.GetComponent<T>();
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Unity <c>GetComponentInChildren&lt;T&gt;</c>: this node, then the whole subtree below it.</summary>
        public static T GetComponentInChildren<T>(this Node node) where T : class
        {
            if (node == null)
            {
                return null;
            }

            if (node is T self)
            {
                return self;
            }

            foreach (Node child in node.GetChildren())
            {
                T found = child.GetComponentInChildren<T>();
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
