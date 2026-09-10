using System.Collections.Generic;
using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// The handful of <c>Physics2D</c> queries the Unity project used, expressed once against Godot's
    /// space state. Ported code calls these instead of building query parameters at every call site.
    ///
    /// Radii and distances are in Godot pixels - scale authored Unity values through
    /// <see cref="World.U"/> before calling.
    /// </summary>
    public static class Phys2D
    {
        /// <summary>Unity <c>Physics2D.OverlapCircle</c>. Returns the first collider found, or null.</summary>
        public static GodotObject OverlapCircle(Node2D context, Vector2 position, float radius, uint mask)
        {
            List<GodotObject> hits = OverlapCircleAll(context, position, radius, mask, maxResults: 1);
            return hits.Count > 0 ? hits[0] : null;
        }

        /// <summary>Unity <c>Physics2D.OverlapCircleAll</c>. Bodies and areas both, like Unity's version.</summary>
        public static List<GodotObject> OverlapCircleAll(
            Node2D context, Vector2 position, float radius, uint mask, int maxResults = 32)
        {
            var results = new List<GodotObject>();
            PhysicsDirectSpaceState2D space = context?.GetWorld2D()?.DirectSpaceState;
            if (space == null)
            {
                return results;
            }

            var shape = new CircleShape2D { Radius = Mathf.Max(0.01f, radius) };
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = shape,
                Transform = new Transform2D(0f, position),
                CollisionMask = mask,
                CollideWithBodies = true,
                CollideWithAreas = true,
            };

            foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, maxResults))
            {
                if (hit.TryGetValue("collider", out Variant collider))
                {
                    var obj = collider.AsGodotObject();
                    if (obj != null)
                    {
                        results.Add(obj);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Unity <c>Physics2D.Raycast</c>. <paramref name="direction"/> is a Godot-space direction
        /// (+Y down); <paramref name="distance"/> is in pixels.
        /// </summary>
        public static RayHit2D Raycast(
            Node2D context, Vector2 origin, Vector2 direction, float distance, uint mask)
        {
            PhysicsDirectSpaceState2D space = context?.GetWorld2D()?.DirectSpaceState;
            if (space == null)
            {
                return default;
            }

            var query = PhysicsRayQueryParameters2D.Create(
                origin, origin + direction.Normalized() * distance, mask);
            query.CollideWithBodies = true;
            query.CollideWithAreas = false;

            Godot.Collections.Dictionary hit = space.IntersectRay(query);
            if (hit == null || hit.Count == 0)
            {
                return default;
            }

            return new RayHit2D
            {
                Hit = true,
                Point = hit["position"].AsVector2(),
                Normal = hit["normal"].AsVector2(),
                Collider = hit["collider"].AsGodotObject(),
                Distance = origin.DistanceTo(hit["position"].AsVector2()),
            };
        }

        /// <summary>Whether a collider found by a query is on one of the given layers.</summary>
        public static bool IsOnLayer(GodotObject collider, uint mask)
        {
            return collider switch
            {
                CollisionObject2D body => (body.CollisionLayer & mask) != 0,
                _ => false,
            };
        }

        /// <summary>
        /// The actor node a query result belongs to. Hitboxes and bodies are child nodes of the actor,
        /// so callers that want "the enemy that was hit" walk up until they find a node in
        /// <paramref name="group"/>.
        /// </summary>
        public static Node FindActorInGroup(GodotObject collider, string group)
        {
            for (Node node = collider as Node; node != null; node = node.GetParent())
            {
                if (node.IsInGroup(group))
                {
                    return node;
                }
            }

            return null;
        }
    }

    /// <summary>Unity <c>RaycastHit2D</c>, cut down to the fields this project reads.</summary>
    public struct RayHit2D
    {
        public bool Hit;
        public Vector2 Point;
        public Vector2 Normal;
        public GodotObject Collider;
        public float Distance;

        /// <summary>Mirrors Unity's <c>if (hit)</c> truth test on a <c>RaycastHit2D</c>.</summary>
        public static implicit operator bool(RayHit2D hit) => hit.Hit;
    }
}
