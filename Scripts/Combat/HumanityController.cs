using System;
using Godot;

namespace MyGame.Combat
{
    public partial class HumanityController : Node
    {
        [Export] private float maxHumanity = 100f;
        [Export] private float currentHumanity = 100f;
        [Export] private float lowHumanityThreshold = 30f;
        [Export] private float humanityLossOnHit = 5f;
        [Export] private float humanityRegenRate = 0.5f;
        [Export] private float humanityRegenDelay = 5f;

        /// <summary>
        /// Writes the authored humanity numbers. Gauge points and seconds - no unit conversion, and
        /// none is wanted: nothing here is a distance.
        ///
        /// Pushed in from the outside rather than loaded here, because these live in
        /// <c>PlayerResources.json</c> and <c>MyGame.Combat</c> must not learn about
        /// <c>MyGame.Player</c> (see CLAUDE.md). <c>GameplayPlayerSpawner</c> owns the call, exactly as
        /// it does for <see cref="Poise.Configure"/>.
        /// </summary>
        public void Configure(float max, float lowThreshold, float lossOnHit, float regenRate, float regenDelay)
        {
            maxHumanity = Mathf.Max(0f, max);
            lowHumanityThreshold = lowThreshold;
            humanityLossOnHit = lossOnHit;
            humanityRegenRate = regenRate;
            humanityRegenDelay = regenDelay;
            currentHumanity = Mathf.Min(currentHumanity, maxHumanity);
        }

        public event Action<float> OnHumanityChanged;
        public event Action OnHumanityDepleted;
        public event Action OnHumanityRestored;
        public event Action OnLowHumanityWarning;

        public float CurrentHumanity => currentHumanity;
        public float MaxHumanity => maxHumanity;
        public float NormalizedHumanity => maxHumanity > 0 ? currentHumanity / maxHumanity : 0f;
        public bool IsLow => currentHumanity <= lowHumanityThreshold;
        public bool IsEmpty => currentHumanity <= 0f;

        private float _regenTimer;
        private bool _wasLow;

        public override void _Process(double delta)
        {
            if (currentHumanity < maxHumanity)
            {
                _regenTimer += (float)delta;
                if (_regenTimer >= humanityRegenDelay)
                {
                    float regen = humanityRegenRate * (float)delta;
                    if (regen > 0.001f)
                    {
                        currentHumanity = Mathf.Min(maxHumanity, currentHumanity + regen);
                        OnHumanityChanged?.Invoke(currentHumanity);
                        OnHumanityRestored?.Invoke();
                    }
                }
            }

            CheckLowWarning();
        }

        private void CheckLowWarning()
        {
            if (IsLow && !_wasLow)
            {
                OnLowHumanityWarning?.Invoke();
                _wasLow = true;
            }
            else if (!IsLow)
            {
                _wasLow = false;
            }
        }

        public bool TrySpend(float amount)
        {
            if (currentHumanity >= amount)
            {
                currentHumanity -= amount;
                _regenTimer = 0f;
                OnHumanityChanged?.Invoke(currentHumanity);
                return true;
            }
            return false;
        }

        public void Spend(float amount)
        {
            currentHumanity = Mathf.Max(0f, currentHumanity - amount);
            _regenTimer = 0f;
            OnHumanityChanged?.Invoke(currentHumanity);

            if (IsEmpty)
                OnHumanityDepleted?.Invoke();
        }

        public void Restore(float amount)
        {
            currentHumanity = Mathf.Min(maxHumanity, currentHumanity + amount);
            OnHumanityChanged?.Invoke(currentHumanity);
            OnHumanityRestored?.Invoke();
        }

        public void OnHit()
        {
            Spend(humanityLossOnHit);
            _regenTimer = 0f;
        }

        public void SetHumanity(float value)
        {
            currentHumanity = Mathf.Clamp(value, 0f, maxHumanity);
            OnHumanityChanged?.Invoke(currentHumanity);
            CheckLowWarning();
        }
    }
}
