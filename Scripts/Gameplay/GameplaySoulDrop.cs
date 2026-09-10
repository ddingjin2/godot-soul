using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The half of the soul loop that needs a world object: dying empties the player's wallet onto the
    /// ground where they fell, and walking back over it takes the souls back.
    ///
    /// Only one stain exists at a time. Dying on the way back to the last one loses it for good, which is
    /// the pressure the whole currency is there to create - without it, souls are just a score.
    /// </summary>
    public sealed partial class GameplaySoulDrop : Node2D
    {
        private SoulsWallet _wallet;
        private DeathStateController _death;
        private PlayerController2D _player;
        private SoulPickup _activeStain;
        private Vector2 _lastGroundedPosition;
        private float _pickupDelay = GameplayTuningDefaults.SoulStainPickupDelay;

        public SoulPickup ActiveStain => _activeStain != null && IsInstanceValid(_activeStain) ? _activeStain : null;

        /// <summary>
        /// <paramref name="pickupDelay"/> is required rather than defaulted. It briefly had
        /// <see cref="GameplayTuningDefaults.SoulStainPickupDelay"/> as a default on the claim that test
        /// runners called the three-argument form - QA found no such caller. An unused default is only a
        /// way for the next caller to forget the delay and silently get the fallback instead of the
        /// authored value, which is the exact failure the wiring was added to remove.
        /// </summary>
        public void Initialize(SoulsWallet wallet, DeathStateController death, PlayerController2D player,
            float pickupDelay)
        {
            Unsubscribe();

            _wallet = wallet;
            _death = death;
            _player = player;
            _pickupDelay = pickupDelay;
            _lastGroundedPosition = GlobalPosition;

            if (_death != null)
                _death.OnDeath += DropSouls;
        }

        public override void _Process(double delta)
        {
            // The stain goes where the player last stood, not where they died. Falling into the pit or
            // dying mid-air would otherwise drop it somewhere it can never be walked back over, which
            // turns a recoverable loss into a permanent one.
            if (_player != null && _player.IsGrounded)
                _lastGroundedPosition = GlobalPosition;
        }

        public override void _ExitTree()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_death != null)
                _death.OnDeath -= DropSouls;
        }

        public void DropSouls()
        {
            int dropped = _wallet != null ? _wallet.Drain() : 0;

            // A killing blow taken in spirit form is the second OnDeath of the same death sequence -
            // every fall death takes that path - and it must not erase the stain the first one just
            // dropped. The first death raises OnDeath before entering spirit form, so spirit state
            // here means same-sequence, and a real second death after a respawn does not take this
            // branch. The wallet is still drained above either way, so the killer can never pocket
            // it; anything spilled inside the spirit window only lands if no stain is open yet,
            // because the road holds one debt at a time.
            if (_death != null && _death.IsInSpiritState)
            {
                if (dropped > 0 && ActiveStain == null)
                    _activeStain = SoulPickup.Create(_lastGroundedPosition, dropped, _pickupDelay);
                return;
            }

            ActiveStain?.QueueFree();
            _activeStain = null;

            if (dropped <= 0)
                return;

            _activeStain = SoulPickup.Create(_lastGroundedPosition, dropped, _pickupDelay);
        }
    }
}
