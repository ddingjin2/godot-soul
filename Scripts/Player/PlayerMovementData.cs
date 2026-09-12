using Godot;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// Movement tuning, authored in <c>Resources/Design/PlayerMovement.json</c> in Unity metres.
    /// </summary>
    /// <remarks>
    /// <b>Unit scaling.</b> <see cref="Load"/> multiplies every spatial number by <see cref="World.Ppu"/>
    /// once, at the point the JSON is read, so nothing downstream has to remember to:
    /// <list type="bullet">
    /// <item><description>Scaled (metres -> pixels): <c>moveSpeed</c>, <c>acceleration</c>,
    /// <c>deceleration</c>, <c>gravity</c>, <c>maxFallSpeed</c>, <c>jumpForce</c>.</description></item>
    /// <item><description>Left alone (seconds): <c>coyoteTime</c>, <c>jumpBufferTime</c>.</description></item>
    /// </list>
    /// </remarks>
    public partial class PlayerMovementData : Resource
    {
        /// <summary>Base name of the design file, shared with the Gameplay tuning catalog.</summary>
        public const string FileName = "PlayerMovement";

        [Export] public float moveSpeed;
        [Export] public float acceleration;
        [Export] public float deceleration;
        [Export] public float gravity;
        [Export] public float maxFallSpeed;

        [Export] public float jumpForce;
        [Export] public float coyoteTime;
        [Export] public float jumpBufferTime;

        /// <summary>The authored file scaled into pixels, or null when it is missing (Unity returned null too).</summary>
        public static PlayerMovementData Load()
        {
            var raw = Res.LoadJson<PlayerMovementData>("Design/" + FileName);
            if (raw == null)
            {
                return null;
            }

            raw.moveSpeed = World.U(raw.moveSpeed);
            raw.acceleration = World.U(raw.acceleration);
            raw.deceleration = World.U(raw.deceleration);
            raw.gravity = World.U(raw.gravity);
            raw.maxFallSpeed = World.U(raw.maxFallSpeed);
            raw.jumpForce = World.U(raw.jumpForce);
            return raw;
        }
    }
}
