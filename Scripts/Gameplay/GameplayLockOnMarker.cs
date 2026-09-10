using Godot;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The world read for <see cref="PlayerLockOn"/>: a small disc over whatever is currently held.
    ///
    /// Lock-on is otherwise invisible - the only sign of it is that the player stops turning, which
    /// reads as a broken control rather than a held target. Lives in Gameplay because the sprite
    /// factory and the palette do; the lock itself stays in Player and knows nothing about this.
    /// </summary>
    public sealed partial class GameplayLockOnMarker : Node2D
    {
        private const string MarkerObjectName = "LockOnMarker";

        /// <summary>Marker size and hover height, in Unity metres.</summary>
        private const float MarkerSize = 0.45f;
        private const float MarkerHeight = 1.3f;

        private PlayerLockOn _lockOn;
        private Node2D _marker;

        public override void _Ready()
        {
            // Unity's [RequireComponent(typeof(PlayerLockOn))] on the same GameObject. The lock is a
            // sibling node under the actor root here, which is what GetComponentInParent reaches.
            _lockOn = this.GetComponentInParent<PlayerLockOn>();
            BuildMarker();

            if (_lockOn == null)
            {
                GD.PushWarning("GameplayLockOnMarker: no PlayerLockOn on this actor; the marker will never show.");
                return;
            }

            _lockOn.OnTargetChanged += OnTargetChanged;

            // The lock may already be held if this node is added after acquisition.
            OnTargetChanged(_lockOn.Target);
        }

        public override void _ExitTree()
        {
            if (_lockOn != null)
                _lockOn.OnTargetChanged -= OnTargetChanged;

            // The marker is deliberately unparented from the player, so nothing else would ever collect it.
            if (_marker != null && IsInstanceValid(_marker))
                _marker.QueueFree();
        }

        /// <summary>
        /// Followed each frame rather than parented to the target. A parent would inherit the enemy's
        /// sprite flip and its hit-reaction scale punch, and would be freed along with it on death -
        /// which is exactly the frame the marker still needs to be around to hide itself.
        /// </summary>
        public override void _Process(double delta)
        {
            if (_marker == null || !_marker.Visible)
                return;

            Node2D target = _lockOn?.Target;
            if (target == null || !IsInstanceValid(target))
            {
                _marker.Visible = false;
                return;
            }

            _marker.GlobalPosition = target.GlobalPosition + World.V(new Vector2(0f, MarkerHeight));
        }

        private void OnTargetChanged(Node2D target)
        {
            if (_marker == null)
                return;

            _marker.Visible = target != null;
            if (target != null)
                _marker.GlobalPosition = target.GlobalPosition + World.V(new Vector2(0f, MarkerHeight));
        }

        private void BuildMarker()
        {
            if (_marker != null)
                return;

            GameplayReadabilityDefaults readability = GameplayReadabilityDefaults.Create();

            // Not parented to the player either: the player's own transform is flipped by facing, and
            // the marker would swing across the screen with every turn.
            var sprite = new Sprite2D
            {
                Name = MarkerObjectName,
                Texture = GameplayVisualFactory.CreateDiscSprite(),
                Modulate = readability.PlayerAttackReadoutColor,
                ZIndex = readability.RoleMarkerSortingOrder,
                Visible = false
            };
            sprite.SetSpriteSize(new Vector2(World.U(MarkerSize), World.U(MarkerSize)));

            GameplayBuildShim.SceneRoot?.AddChild(sprite);
            _marker = sprite;
        }
    }
}
