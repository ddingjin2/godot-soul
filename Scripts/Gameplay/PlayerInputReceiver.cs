using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Reads the keyboard once a frame and pushes it at the player. A child node of the player actor,
    /// which is how Unity's "another component on the player GameObject" translates.
    /// </summary>
    public partial class PlayerInputReceiver : Node
    {
        /// <summary>
        /// Raised on the interact key. Routed through this component rather than read where it is needed
        /// so it inherits the freeze: pause and victory both stop play by disabling this receiver, while
        /// a zero timescale on its own would leave any other _Process still reading the key.
        /// </summary>
        /// <remarks>Unity's <c>UnityEvent</c>; subscribe with <c>+=</c>, not <c>AddListener</c>.</remarks>
        public event Action OnInteract;

        /// <summary>
        /// Unity's <c>Behaviour.enabled</c>, which the pause menu, the victory panel and the travel
        /// portal all write to stop the player acting. Godot has no such flag, so it maps onto whether
        /// this node processes - and processing is the whole of what this component does.
        /// </summary>
        public bool Enabled
        {
            get => IsProcessing();
            set => SetProcess(value);
        }

        private PlayerController2D _player;
        private SinResonanceController _sin;
        private PlayerLockOn _lockOn;

        public override void _Ready()
        {
            // The player's components are sibling nodes under the actor root here, not components on one
            // GameObject, so this is GetComponentInParent rather than GetComponent.
            _player = this.GetComponentInParent<PlayerController2D>();
            _sin = this.GetComponentInParent<SinResonanceController>();
            _lockOn = this.GetComponentInParent<PlayerLockOn>();
        }

        public override void _Process(double delta)
        {
            if (_player == null)
                return;

            // Vertical is already Godot-space (+Y down) - GameplayInput is where the axis is flipped.
            _player.SetMoveInput(new Vector2(GameplayInput.Horizontal, GameplayInput.Vertical));

            if (GameplayInput.JumpPressed)
                _player.RequestJump();

            if (GameplayInput.DashPressed)
                _player.RequestDodge();

            if (GameplayInput.HeavyAttackPressed)
                _player.RequestHeavyAttack();
            else if (GameplayInput.AttackPressed)
                _player.RequestAttack();

            if (GameplayInput.ParryPressed)
                _player.RequestParry();

            if (GameplayInput.HealPressed)
                _player.RequestHeal();

            if (GameplayInput.InteractPressed)
                OnInteract?.Invoke();

            if (GameplayInput.LockOnPressed && _lockOn != null)
                _lockOn.Toggle();

            if (_sin == null)
                return;

            // One table lookup rather than a branch per sin: adding the eighth sin should not mean
            // editing this file at all.
            SinState pressed = GameplayInput.PressedSin();
            if (pressed != SinState.None)
                _sin.RequestActivateSin(pressed);
        }
    }
}
