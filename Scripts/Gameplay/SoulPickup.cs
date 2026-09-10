using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The souls the player dropped where they died, waiting to be walked over. The stain itself is
    /// authored at <c>Scenes/World/SoulPickup.tscn</c>; <see cref="Create"/> instances it and binds the
    /// two things that vary, the soul count and the pickup delay.
    ///
    /// The stain lands under the player's own feet on any grounded death, and spirit form neither moves
    /// them nor turns their collider off, so "whoever touches it takes it" would refund every death on
    /// the next physics step. A dead toucher is refused, and the stain stays shut for a moment after
    /// they are back on their feet so respawning on top of it is not the same as walking back to it.
    /// </summary>
    /// <remarks>
    /// Unity used <c>OnTriggerEnter2D</c> plus <c>OnTriggerStay2D</c>, and a never-sleeping kinematic
    /// Rigidbody2D to keep stay firing. Godot has no stay signal, so the overlap is polled every frame
    /// instead - which is what the pair of Unity callbacks amounted to, and it keeps the case the
    /// comment above is about: a player who respawns standing on the stain never sends a fresh enter.
    /// </remarks>
    public sealed partial class SoulPickup : Area2D
    {
        /// <summary>The scene root's name, which is what a lookup for a dropped stain matches.</summary>
        public const string ObjectName = "SoulPickup";

        /// <summary>The authored stain: the reach shape, the pulse and the disc, sized and tinted.</summary>
        private const string ScenePath = "res://Scenes/World/SoulPickup.tscn";

        [Export] private int souls;

        private float _pickupDelay;
        private float _armedAt;

        public int Souls => souls;

        /// <summary><paramref name="position"/> is a Godot world position, already in pixels.</summary>
        public static SoulPickup Create(Vector2 position, int souls, float pickupDelay)
        {
            var pickup = GD.Load<PackedScene>(ScenePath).Instantiate<SoulPickup>();

            // Written before the node enters the tree, as GameplayBuildShim.NewObject does: a body
            // added at the origin and moved afterwards sweeps from the origin on its first physics
            // step, hitting whatever stands in between.
            pickup.GlobalPosition = position;
            pickup.souls = Mathf.Max(0, souls);
            pickup._pickupDelay = Mathf.Max(0f, pickupDelay);
            pickup._armedAt = GameClock.Time + pickup._pickupDelay;

            // Unity's Instantiate put it in the active scene; so does this.
            GameplayBuildShim.SceneRoot?.AddChild(pickup);
            pickup.GlobalPosition = position;
            return pickup;
        }

        public override void _Process(double delta)
        {
            if (souls <= 0)
                return;

            foreach (Node2D body in GetOverlappingBodies())
                TryCollect(body);
        }

        private void TryCollect(Node2D other)
        {
            if (other == null)
                return;

            PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
            if (player == null)
                return;

            // Every frame a corpse is lying in it pushes the clock back, so the delay is counted from
            // the respawn rather than from the death.
            var death = player.GetComponentInParent<DeathStateController>();
            if (death != null && death.IsInSpiritState)
            {
                _armedAt = GameClock.Time + _pickupDelay;
                return;
            }

            if (GameClock.Time < _armedAt)
                return;

            SoulsWallet wallet = other.GetComponentInParent<SoulsWallet>();
            if (wallet == null)
                return;

            wallet.Add(souls);
            souls = 0;
            QueueFree();
        }
    }
}
