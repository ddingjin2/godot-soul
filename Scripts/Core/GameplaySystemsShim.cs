using System.Collections.Generic;
using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// Unity's <c>FindObjectsByType&lt;T&gt;()</c> / <c>FindAnyObjectByType&lt;T&gt;()</c>, which Godot
    /// has no counterpart for: <c>GetNodesInGroup</c> answers a group, not a C# type, and the gameplay
    /// bootstrap has to wire zones that nothing put in a group.
    ///
    /// A whole-tree walk costs what Unity's scan cost, so the callers keep the Unity source's rule: only
    /// from wiring and from rare events (a rest, a travel), never per frame.
    /// </summary>
    /// <remarks>
    /// The other two scene-level Unity idioms - <c>new GameObject</c>/<c>AddComponent</c> and
    /// <c>SceneManager.GetActiveScene().name</c> - already live in <see cref="GameplayBuildShim"/>. Use
    /// that; this file is only the type search.
    /// </remarks>
    public static class SceneQuery
    {
        private static SceneTree Tree => Engine.GetMainLoop() as SceneTree;

        /// <summary>Unity <c>FindAnyObjectByType&lt;T&gt;()</c>, over the whole current tree.</summary>
        public static T FindFirst<T>() where T : class
        {
            Node root = Tree?.Root;
            return root == null ? null : FindFirstIn<T>(root);
        }

        /// <summary>Unity <c>FindObjectsByType&lt;T&gt;()</c>, over the whole current tree.</summary>
        public static List<T> FindAll<T>() where T : class
        {
            var found = new List<T>();
            Node root = Tree?.Root;
            if (root != null)
            {
                CollectIn(root, found);
            }

            return found;
        }

        private static T FindFirstIn<T>(Node node) where T : class
        {
            if (node is T match)
            {
                return match;
            }

            foreach (Node child in node.GetChildren())
            {
                T found = FindFirstIn<T>(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void CollectIn<T>(Node node, List<T> into) where T : class
        {
            if (node is T match)
            {
                into.Add(match);
            }

            foreach (Node child in node.GetChildren())
            {
                CollectIn(child, into);
            }
        }
    }
}
