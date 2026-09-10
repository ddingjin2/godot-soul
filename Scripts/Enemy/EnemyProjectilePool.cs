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
    /// scene, hold references to freed nodes across a reload, and need a key per template that then
    /// leaks an entry every time a runtime-built template is thrown away. A field on the caster dies
    /// with the caster and needs none of that.
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
        private readonly Node2D _prefab;
        private readonly Node _parent;
        private readonly Stack<EnemyProjectile> _idle = new();

        /// <param name="prefab">A detached template node, duplicated per shot. Never added to the tree itself.</param>
        /// <param name="parent">Where new projectiles are parented - the scene root, like Unity's Instantiate.</param>
        public EnemyProjectilePool(Node2D prefab, Node parent)
        {
            _prefab = prefab;
            _parent = parent;
        }

        public int IdleCount => _idle.Count;

        public EnemyProjectile Spawn(Vector2 position)
        {
            if (!GodotObject.IsInstanceValid(_prefab) || !GodotObject.IsInstanceValid(_parent))
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

            var body = (Node2D)_prefab.Duplicate();

            // Unity threw the instance away when the prefab carried no EnemyProjectile component; the
            // node equivalent is a template whose root is not one.
            if (body is not EnemyProjectile projectile)
            {
                body.QueueFree();
                return null;
            }

            _parent.AddChild(projectile);
            projectile.GlobalPosition = position;
            projectile.Visible = true;
            projectile.ProcessMode = Node.ProcessModeEnum.Inherit;
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
