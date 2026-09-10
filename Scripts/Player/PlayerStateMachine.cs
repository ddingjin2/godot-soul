using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Player
{
    public enum PlayerState
    {
        Grounded,
        Airborne,
        Attack,
        HeavyAttack,
        Dash,
        Parry,
        Spirit,
        Dead
    }

    /// <summary>
    /// Reads the player's flags and names the state they add up to. Pure logic with nothing in the
    /// scene tree, so it is a plain class composed into <see cref="PlayerController2D"/> rather than a
    /// node - it was a MonoBehaviour in Unity only because that was the only way to be on the player.
    /// </summary>
    public sealed class PlayerStateMachine
    {
        public PlayerState CurrentState { get; private set; } = PlayerState.Grounded;

        private Health _health;
        private DeathStateController _deathState;

        public void Initialize(Health health, DeathStateController deathState)
        {
            _health = health;
            _deathState = deathState;
        }

        public void UpdateState(PlayerController2D player)
        {
            if (player == null)
            {
                return;
            }

            _health ??= player.GetComponentInParent<Health>();
            _deathState ??= player.GetComponentInParent<DeathStateController>();

            if (_health != null && _health.IsDead)
            {
                CurrentState = PlayerState.Dead;
            }
            else if (_deathState != null && _deathState.IsInSpiritState)
            {
                CurrentState = PlayerState.Spirit;
            }
            else if (player.IsDodging)
            {
                CurrentState = PlayerState.Dash;
            }
            else if (player.IsParrying)
            {
                CurrentState = PlayerState.Parry;
            }
            else if (player.IsHeavyAttacking)
            {
                CurrentState = PlayerState.HeavyAttack;
            }
            else if (player.IsAttacking)
            {
                CurrentState = PlayerState.Attack;
            }
            else if (!player.IsGrounded)
            {
                CurrentState = PlayerState.Airborne;
            }
            else
            {
                CurrentState = PlayerState.Grounded;
            }
        }
    }
}
