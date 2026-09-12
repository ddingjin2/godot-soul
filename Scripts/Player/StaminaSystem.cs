using System;
using Godot;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// The stamina gauge. A pure manager - it has no transform of its own, so it is a plain
    /// <see cref="Node"/> child of the player body rather than a Node2D.
    /// </summary>
    public partial class StaminaSystem : Node
    {
        [Export] private float maxStamina;
        [Export] private float currentStamina;
        [Export] private float staminaRegenRate;
        [Export] private float staminaRegenDelay;

        // Costs
        [Export] private float attackCost;
        [Export] private float dodgeCost;
        [Export] private float parryCost;

        // Blocking
        [Export] private float blockCostPerSecond;
        [Export] private float blockStaminaThreshold;

        public event Action<float> OnStaminaChanged;
        public event Action OnStaminaDepleted;
        public event Action OnStaminaReady;

        public float MaxStamina => maxStamina;
        public float CurrentStamina => currentStamina;
        public float NormalizedStamina => maxStamina > 0 ? currentStamina / maxStamina : 0f;
        public bool HasStamina => currentStamina >= blockStaminaThreshold;
        public bool CanAttack => currentStamina >= attackCost;
        public bool CanDodge => currentStamina >= dodgeCost;
        public bool CanParry => currentStamina >= parryCost;

        private float _regenTimer;
        private bool _wasDepleted;
        private bool _configured;

        /// <summary>
        /// Fills from the ceiling, which is zero until <see cref="ApplyTuning"/> has run - and
        /// ApplyTuning refills too, so this line only ever repeats what the spawner already wrote.
        /// </summary>
        public override void _Ready()
        {
            currentStamina = maxStamina;
            CallDeferred(nameof(CheckConfigured));
        }

        private void CheckConfigured() => TuningGuard.Check(this, _configured, "ApplyTuning");

        public override void _Process(double delta)
        {
            Tick((float)delta);
        }

        /// <summary>
        /// The old Update body, exposed so a test or an edit-mode runner can step the gauge without a
        /// scene tree driving it.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (currentStamina < maxStamina)
            {
                _regenTimer += deltaTime;
                if (_regenTimer >= staminaRegenDelay)
                {
                    float regen = staminaRegenRate * deltaTime;
                    if (regen > 0.001f)
                    {
                        float prevStamina = currentStamina;
                        currentStamina = Mathf.Min(maxStamina, currentStamina + regen);

                        if (prevStamina < blockStaminaThreshold && currentStamina >= blockStaminaThreshold)
                        {
                            OnStaminaReady?.Invoke();
                        }

                        OnStaminaChanged?.Invoke(currentStamina);
                    }
                }
            }
            else if (_wasDepleted && currentStamina >= blockStaminaThreshold)
            {
                OnStaminaReady?.Invoke();
            }

            CheckDepletedWarning();
        }

        private void CheckDepletedWarning()
        {
            bool isDepleted = currentStamina <= 0f;
            if (isDepleted && !_wasDepleted)
            {
                OnStaminaDepleted?.Invoke();
                _wasDepleted = true;
            }
            else if (!isDepleted)
            {
                _wasDepleted = false;
            }
        }

        public bool TrySpendAttack()
        {
            if (!CanAttack)
            {
                return false;
            }

            Spend(attackCost);
            return true;
        }

        public void ApplyTuning(PlayerResourceData tuning, bool refill = true)
        {
            if (tuning == null)
            {
                return;
            }

            _configured = true;
            maxStamina = Mathf.Max(1f, tuning.maxStamina);
            staminaRegenRate = tuning.staminaRegenRate;
            staminaRegenDelay = tuning.staminaRegenDelay;
            attackCost = tuning.attackCost;
            dodgeCost = tuning.dodgeCost;
            parryCost = tuning.parryCost;
            blockCostPerSecond = tuning.blockCostPerSecond;
            blockStaminaThreshold = tuning.blockStaminaThreshold;
            currentStamina = refill ? maxStamina : Mathf.Clamp(currentStamina, 0f, maxStamina);
            OnStaminaChanged?.Invoke(currentStamina);
        }

        public bool TrySpendDodge()
        {
            if (!CanDodge)
            {
                return false;
            }

            Spend(dodgeCost);
            return true;
        }

        public bool TrySpendParry()
        {
            if (!CanParry)
            {
                return false;
            }

            Spend(parryCost);
            return true;
        }

        public float SpendBlockCost(float dt)
        {
            float cost = blockCostPerSecond * dt;
            cost = Mathf.Min(cost, currentStamina);
            currentStamina = Mathf.Max(0f, currentStamina - cost);
            _regenTimer = 0f;
            OnStaminaChanged?.Invoke(currentStamina);
            return cost;
        }

        public void Spend(float amount)
        {
            currentStamina = Mathf.Max(0f, currentStamina - amount);
            _regenTimer = 0f;
            OnStaminaChanged?.Invoke(currentStamina);
        }

        public void Restore(float amount)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + amount);
            OnStaminaChanged?.Invoke(currentStamina);
        }

        public void SetStamina(float value)
        {
            currentStamina = Mathf.Clamp(value, 0f, maxStamina);
            OnStaminaChanged?.Invoke(currentStamina);
        }

        /// <summary>
        /// Moves the ceiling without touching what is in the gauge, the way <c>Health.SetMaxHealth</c>
        /// does. Endurance levels come through here rather than through <see cref="ApplyTuning"/>, which
        /// would overwrite every other cost with the authored value.
        /// </summary>
        public void SetMaxStamina(float value)
        {
            maxStamina = Mathf.Max(1f, value);
            currentStamina = Mathf.Min(currentStamina, maxStamina);
            OnStaminaChanged?.Invoke(currentStamina);
        }

        public void ResetRegenTimer()
        {
            _regenTimer = 0f;
        }
    }
}
