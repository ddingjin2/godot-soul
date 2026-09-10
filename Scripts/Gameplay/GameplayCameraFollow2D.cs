using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Writes the world position of the camera rig, not of the camera itself. The camera hangs under the
    /// rig so <see cref="MyGame.Combat.CameraShake"/> can own the camera's own offset and the cutscene
    /// director can own the rig, with none of the three overwriting another's frame.
    /// </summary>
    /// <remarks>
    /// The dead zone, the look-ahead and the follow speed are literals that live only in this file, so
    /// they are written as Unity metres and wrapped in <see cref="World.U"/> / <see cref="World.V"/>
    /// here. The bounds are not: they come off <c>GameplaySceneDefaults</c>, which has already converted
    /// them.
    /// </remarks>
    public sealed partial class GameplayCameraFollow2D : Node2D
    {
        [Export] private Node2D target;

        // A dead zone is a half-extent, not a point, so it scales with World.U and stays positive.
        private Vector2 _deadZone = new(World.Ppu * 1.2f, World.Ppu * 0.6f);
        private Vector2 _lookAhead = World.V(new Vector2(1.4f, 1.2f));
        private float _smoothTime = 0.25f;
        private float _maxFollowSpeed = World.Ppu * 12f;
        private float _minX = World.Ppu * -6f;
        private float _maxX = World.Ppu * 32f;
        private float _minY = World.Ppu * -8f;
        private float _maxY = World.Ppu * 0.2f;

        private Vector2 _velocity;

        /// <summary>
        /// Both bound pairs arrive already in Godot space, as min/max: GameplaySceneDefaults is the
        /// conversion boundary and it has already scaled them and, for the vertical pair, flipped the
        /// sign and swapped the ends (a Unity 0.5..7.5 reaches here as -750..-50). Converting again
        /// here would scale them a second time.
        /// </summary>
        public void Initialize(Node2D followTarget, Vector2 horizontalBounds, Vector2 verticalBounds)
        {
            target = followTarget;
            _minX = horizontalBounds.X;
            _maxX = horizontalBounds.Y;
            _minY = verticalBounds.X;
            _maxY = verticalBounds.Y;
        }

        public override void _Process(double delta)
        {
            if (target == null)
                return;

            Vector2 current = GlobalPosition;
            Vector2 desired = current;

            // The look-ahead leads the way the player faces. Facing is the sign of the actor's x scale,
            // exactly as in Unity, and a zero scale reads as facing right rather than as no look-ahead.
            float facing = Mathf.Sign(Mathf.IsZeroApprox(target.Scale.X) ? 1f : target.Scale.X);
            float targetX = target.GlobalPosition.X + facing * _lookAhead.X;

            // _lookAhead is already Godot-space: World.V turned the authored +1.2 (up) into -120.
            float targetY = target.GlobalPosition.Y + _lookAhead.Y;
            float deltaX = targetX - current.X;
            float deltaY = targetY - current.Y;

            if (Mathf.Abs(deltaX) > _deadZone.X)
                desired.X = targetX - Mathf.Sign(deltaX) * _deadZone.X;

            if (Mathf.Abs(deltaY) > _deadZone.Y)
                desired.Y = targetY - Mathf.Sign(deltaY) * _deadZone.Y;

            desired.X = Mathf.Clamp(desired.X, _minX, _maxX);
            desired.Y = Mathf.Clamp(desired.Y, _minY, _maxY);

            GlobalPosition = SmoothDamp(current, desired, ref _velocity, _smoothTime, _maxFollowSpeed, (float)delta);
        }

        /// <summary>
        /// Unity's <c>Vector3.SmoothDamp</c>, which Godot has no counterpart for. Carried over rather
        /// than replaced with a lerp: the critically damped spring is what stops the camera snapping
        /// when the player reverses inside the dead zone, and a lerp's frame-rate dependence would make
        /// the follow feel different on every machine.
        /// </summary>
        private static Vector2 SmoothDamp(
            Vector2 current, Vector2 target, ref Vector2 velocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            smoothTime = Mathf.Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

            Vector2 change = current - target;
            float maxChange = maxSpeed * smoothTime;
            change = change.LimitLength(maxChange);

            Vector2 originalTo = current - change;
            Vector2 temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * exp;
            Vector2 output = originalTo + (change + temp) * exp;

            // Overshoot guard, exactly as Unity's: without it the spring can walk past the target and
            // pull itself back, which reads as a twitch at the end of every follow.
            if ((target - current).Dot(output - target) > 0f)
            {
                output = target;
                velocity = (output - target) / deltaTime;
            }

            return output;
        }
    }
}
