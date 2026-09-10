using System.Collections.Generic;
using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// The Unity component idioms the ported gameplay code leans on, expressed once against Godot's
    /// scene tree.
    ///
    /// Unity put several components on one GameObject; Godot puts them on sibling child nodes under an
    /// actor root. So <c>GetComponent&lt;T&gt;()</c> becomes "am I a T, or is one of my direct children
    /// a T", and <c>GetComponentInParent&lt;T&gt;()</c> walks that same test up the ancestors. Named
    /// <c>Find*</c> rather than <c>Get*</c> so it cannot collide with a Godot member or with another
    /// folder's own helper.
    /// </summary>
    public static class EnemyShim
    {
        /// <summary>Unity <c>GetComponent&lt;T&gt;()</c>: this node, or the first direct child of that type.</summary>
        public static T FindComponent<T>(this Node self) where T : class
        {
            if (self is T match)
            {
                return match;
            }

            if (self == null)
            {
                return null;
            }

            foreach (Node child in self.GetChildren())
            {
                if (child is T found)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Unity <c>GetComponentInParent&lt;T&gt;()</c>: this node, then each ancestor, self-and-children each time.</summary>
        public static T FindComponentInParent<T>(this Node self) where T : class
        {
            for (Node node = self; node != null; node = node.GetParent())
            {
                T found = node.FindComponent<T>();
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Unity <c>GetComponentsInChildren&lt;T&gt;()</c>: this node and every descendant, depth first.</summary>
        public static List<T> FindComponentsInChildren<T>(this Node self) where T : class
        {
            var found = new List<T>();
            Collect(self, found);
            return found;
        }

        private static void Collect<T>(Node node, List<T> into) where T : class
        {
            if (node == null)
            {
                return;
            }

            if (node is T match)
            {
                into.Add(match);
            }

            foreach (Node child in node.GetChildren())
            {
                Collect(child, into);
            }
        }

        /// <summary>
        /// Unity's <c>SpriteRenderer.size</c> under <c>SpriteDrawMode.Sliced</c>, which the ported code
        /// used to give a body an authored width and height. Godot's Sprite2D has no size, so the same
        /// intent is a scale against the texture's own pixel size.
        /// </summary>
        /// <param name="sizePx">Wanted on-screen size, already in Godot pixels.</param>
        public static void SetSpriteSize(this Sprite2D sprite, Vector2 sizePx)
        {
            if (sprite?.Texture == null)
            {
                return;
            }

            Vector2 texture = sprite.Texture.GetSize();
            if (texture.X <= 0f || texture.Y <= 0f)
            {
                return;
            }

            sprite.Scale = new Vector2(sizePx.X / texture.X, sizePx.Y / texture.Y);
        }

        /// <summary>
        /// The world-space box a body's first collision shape covers - Unity's <c>Collider2D.bounds</c>.
        /// Only the three shapes this project builds are understood; anything else falls back to
        /// <paramref name="fallbackHalfExtents"/> around the body's own origin, which is what a body with
        /// no shape at all (every test fixture) gets.
        /// </summary>
        public static Rect2 BodyBounds(this Node2D body, Vector2 fallbackHalfExtents)
        {
            Vector2 half = fallbackHalfExtents;
            Vector2 centre = body.GlobalPosition;

            CollisionShape2D shape = body.FindComponent<CollisionShape2D>();
            if (shape?.Shape != null)
            {
                centre = shape.GlobalPosition;
                half = shape.Shape switch
                {
                    RectangleShape2D rect => rect.Size * 0.5f,
                    CapsuleShape2D capsule => new Vector2(capsule.Radius, capsule.Height * 0.5f),
                    CircleShape2D circle => new Vector2(circle.Radius, circle.Radius),
                    _ => half,
                };
            }

            return new Rect2(centre - half, half * 2f);
        }
    }
}
