using System.Collections.Generic;
using Godot;

namespace MyGame.Enemy
{
    /// <summary>
    /// Reuses one caster's projectiles instead of freeing them. A projectile is the only thing in the
    /// slice created and thrown away on a timer, so it is the only thing generating garbage at a steady
    /// rate during a fight.
    ///
    /// One pool per caster rather than one static pool for the game. A static pool would outlive the
    /// scene, hold references to freed nodes across a reload, and need a key per projectile scene. A
    /// field on the caster dies with the caster and needs none of that.
    /// </summary>
    /// <remarks>
    /// Not a node, exactly as in Unity. What it does need that Unity gave it for free is somewhere to
    /// put a new projectile: <c>Object.Instantiate</c> dropped one at the scene root, so the caster
    /// hands the pool that parent when it builds it.
    ///
    /// A released projectile is hidden and its processing switched off rather than freed - the whole
    /// point of the pool - and re-armed on the way back out.
    /// </remarks>
    public sealed class EnemyProjectilePool
    {
        private readonly PackedScene _scene;
        private readonly Node _parent;
        private readonly Color? _tint;
        private readonly int? _sortingOrder;
        private readonly Stack<EnemyProjectile> _idle = new();

        /// <param name="scene">
        /// <see cref="EnemyProjectile.ScenePath"/>, instanced per shot. This used to be a detached
        /// template node copied with <c>Duplicate()</c>, which is what Rule 2 names: a scene instance
        /// carries no runtime state its template happened to be left in.
        /// </param>
        /// <param name="parent">Where new projectiles are parented - the scene root, like Unity's Instantiate.</param>
        /// <param name="tint">Readability's projectile colour, or null to keep the scene's own.</param>
        /// <param name="sortingOrder">Readability's projectile sorting order, or null to keep the scene's own.</param>
        public EnemyProjectilePool(PackedScene scene, Node parent, Color? tint = null, int? sortingOrder = null)
        {
            _scene = scene;
            _parent = parent;
            _tint = tint;
            _sortingOrder = sortingOrder;
        }

        public int IdleCount => _idle.Count;

        public EnemyProjectile Spawn(Vector2 position)
        {
            if (!GodotObject.IsInstanceValid(_scene) || !GodotObject.IsInstanceValid(_parent))
            {
                return null;
            }

            while (_idle.Count > 0)
            {
                EnemyProjectile pooled = _idle.Pop();

                // A scene reload frees pooled nodes without emptying the stack. IsInstanceValid catches
                // those, so the loop drops them rather than handing back a corpse.
                if (!GodotObject.IsInstanceValid(pooled))
                {
                    continue;
                }

                pooled.GlobalPosition = position;
                pooled.Visible = true;
                pooled.ProcessMode = Node.ProcessModeEnum.Inherit;
                return pooled;
            }

            Node body = _scene.Instantiate();

            // Unity threw the instance away when the prefab carried no EnemyProjectile component; the
            // scene equivalent is a scene whose root is not one.
            if (body is not EnemyProjectile projectile)
            {
                body?.QueueFree();
                return null;
            }

            _parent.AddChild(projectile);
            projectile.GlobalPosition = position;
            projectile.SetAppearance(_tint, _sortingOrder);
            projectile.SetPool(this);
            return projectile;
        }

        internal void Release(EnemyProjectile projectile)
        {
            if (!GodotObject.IsInstanceValid(projectile))
            {
                return;
            }

            projectile.Visible = false;
            projectile.ProcessMode = Node.ProcessModeEnum.Disabled;
            _idle.Push(projectile);
        }
    }
}
