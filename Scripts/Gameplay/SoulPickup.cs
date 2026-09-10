using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The souls the player dropped where they died, waiting to be walked over. Built from code like the
    /// rest of the greybox actors, so there is no scene file to keep in step with it.
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
        public const string ObjectName = "SoulPickup";

        private static readonly Color StainColor = new(0.51764706f, 0.57254902f, 0.627451f, 0.85f);

        /// <summary>Reach of the stain, in Unity metres.</summary>
        private const float PickupRadius = 0.6f;

        /// <summary>How big the disc is drawn, in Unity metres.</summary>
        private const float StainSize = 0.55f;

        [Export] private int souls;

        private float _pickupDelay;
        private float _armedAt;

        public int Souls => souls;

        /// <summary><paramref name="position"/> is a Godot world position, already in pixels.</summary>
        public static SoulPickup Create(Vector2 position, int souls, float pickupDelay)
        {
            var sprite = new Sprite2D
            {
                Name = "Stain",
                Texture = GameplayVisualFactory.CreateDiscSprite(),
                Modulate = StainColor,
                ZIndex = 40
            };
            sprite.SetSpriteSize(new Vector2(World.U(StainSize), World.U(StainSize)));

            // Reuses the telegraph pulse so the stain reads as something to walk into, not scenery.
            var pulse = new GameplayTelegraphPulse { Name = "Pulse" };
            pulse.AddChild(sprite);

            var pickup = new SoulPickup
            {
                Name = ObjectName,
                GlobalPosition = position,
                CollisionLayer = World.Layer.Trigger,
                CollisionMask = World.Layer.Player,
                souls = Mathf.Max(0, souls)
            };
            pickup._pickupDelay = Mathf.Max(0f, pickupDelay);
            pickup._armedAt = GameClock.Time + pickup._pickupDelay;

            pickup.AddChild(new CollisionShape2D
            {
                Name = "Reach",
                Shape = new CircleShape2D { Radius = World.U(PickupRadius) }
            });
            pickup.AddChild(pulse);

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
