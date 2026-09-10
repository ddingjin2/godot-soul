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
    /// The exported defaults are already in pixels, so an instance nobody loaded is still usable.
    /// </remarks>
    public partial class PlayerMovementData : Resource
    {
        /// <summary>Base name of the design file, shared with the Gameplay tuning catalog.</summary>
        public const string FileName = "PlayerMovement";

        [Export] public float moveSpeed = World.U(6f);
        [Export] public float acceleration = World.U(30f);
        [Export] public float deceleration = World.U(25f);
        [Export] public float gravity = World.U(20f);
        [Export] public float maxFallSpeed = World.U(15f);

        [Export] public float jumpForce = World.U(12f);
        [Export] public float coyoteTime = 0.1f;
        [Export] public float jumpBufferTime = 0.12f;

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
