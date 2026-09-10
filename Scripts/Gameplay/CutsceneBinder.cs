using System.Collections.Generic;
using Godot;

namespace MyGame.Gameplay
{
    /// <summary>
    /// role -> node registry that <see cref="CutsceneDirector"/> reads when a step needs the object it
    /// is about to move. The gameplay scene is built entirely by code, so there is nothing authored to
    /// point a cutscene at ahead of time; the lookup has to happen at Play time and this is where it
    /// finds its targets.
    /// </summary>
    /// <remarks>
    /// Unity stored <c>GameObject</c>s; here it is <see cref="Node"/> rather than <see cref="Node2D"/>,
    /// because two of the five roles are not spatial at all - the overlay is a <c>CanvasLayer</c> and
    /// the Signals role is the director's own node. <see cref="Get2D"/> is what the roles that do move
    /// ask through.
    /// </remarks>
    public sealed class CutsceneBinder
    {
        private readonly Dictionary<CutsceneRole, Node> _targets = new();

        public void Register(CutsceneRole role, Node target)
        {
            if (target == null)
                _targets.Remove(role);
            else
                _targets[role] = target;
        }

        public bool TryGet(CutsceneRole role, out Node target)
        {
            // A freed node stays in the dictionary as a live-looking C# reference - Godot's Node has no
            // overloaded ==, so the emptiness has to be asked for with IsInstanceValid. Same class of
            // trap as Unity's fake-null, checked in the same place.
            if (_targets.TryGetValue(role, out target) && GodotObject.IsInstanceValid(target))
                return true;

            target = null;
            return false;
        }

        /// <summary>The spatial roles - camera rig, player, boss - as the <see cref="Node2D"/> a move needs.</summary>
        public Node2D Get2D(CutsceneRole role)
        {
            return TryGet(role, out Node target) ? target as Node2D : null;
        }
    }
}
