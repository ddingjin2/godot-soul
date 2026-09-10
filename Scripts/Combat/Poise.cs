using System;
using Godot;

namespace MyGame.Combat
{
    /// <summary>
    /// Stagger resistance: the second gauge a hit spends. While poise holds, the actor keeps doing what
    /// it was doing; at zero its action breaks and it stands open for a moment.
    ///
    /// Poise cost is derived from the hit inside <see cref="ApplyHit"/> rather than carried on
    /// <see cref="DamageRequest"/>, so every attack in the game gets a poise cost without a single call
    /// site changing. Per-actor weighting lives on this node, which is where designers tune it.
    /// </summary>
    public partial class Poise : Node
    {
        [Export] private float maxPoise = 50f;
        [Export] private float currentPoise = 50f;
        [Export] private float heavyHitMultiplier = 2f;
        [Export] private float regenDelay = 2f;
        [Export] private float regenRate = 25f;

        public event Action<float> OnPoiseChanged;
        public event Action OnPoiseBroken;

        public float MaxPoise => maxPoise;
        public float CurrentPoise => currentPoise;
        public float NormalizedPoise => maxPoise > 0f ? currentPoise / maxPoise : 0f;

        /// <summary>A zero or negative maximum means this actor cannot be staggered at all.</summary>
        public bool CanBreak => maxPoise > 0f;

        private float _regenTimer;

        public override void _Ready()
        {
            currentPoise = maxPoise;
        }

        public override void _Process(double delta)
        {
            if (currentPoise >= maxPoise)
                return;

            _regenTimer += (float)delta;
            if (_regenTimer < regenDelay)
                return;

            float regen = regenRate * (float)delta;
            if (regen <= 0.001f)
                return;

            currentPoise = Mathf.Min(maxPoise, currentPoise + regen);
            OnPoiseChanged?.Invoke(currentPoise);
        }

        public void Configure(float max, float heavyMultiplier, float delay, float rate)
        {
            maxPoise = Mathf.Max(0f, max);
            heavyHitMultiplier = Mathf.Max(1f, heavyMultiplier);
            regenDelay = Mathf.Max(0f, delay);
            regenRate = Mathf.Max(0f, rate);
            currentPoise = maxPoise;
            _regenTimer = 0f;
            OnPoiseChanged?.Invoke(currentPoise);
        }

        /// <summary>
        /// Spends poise for one landed hit. Returns true when this hit broke the gauge.
        /// The gauge refills on a break so the next hit in the same combo cannot break it again;
        /// without that, one fast weapon would hold an actor in a permanent stagger.
        /// </summary>
        public bool ApplyHit(float damage, DamageType damageType)
        {
            if (!CanBreak || damage <= 0f)
                return false;

            currentPoise -= damage * (damageType == DamageType.Heavy ? heavyHitMultiplier : 1f);
            _regenTimer = 0f;

            if (currentPoise > 0f)
            {
                OnPoiseChanged?.Invoke(currentPoise);
                return false;
            }

            currentPoise = maxPoise;
            OnPoiseChanged?.Invoke(currentPoise);
            OnPoiseBroken?.Invoke();
            return true;
        }

        public void SetPoise(float value)
        {
            currentPoise = Mathf.Clamp(value, 0f, maxPoise);
            OnPoiseChanged?.Invoke(currentPoise);
        }

        /// <summary>
        /// Moves the ceiling and leaves the gauge where it is. Separate from <see cref="Configure"/>,
        /// which refills and resets the regen timer - a caller that only wants a bigger maximum, such as
        /// a Resolve level, must not hand the actor a free stagger reset with it.
        /// </summary>
        public void SetMaxPoise(float value)
        {
            maxPoise = Mathf.Max(0f, value);
            currentPoise = Mathf.Min(currentPoise, maxPoise);
            OnPoiseChanged?.Invoke(currentPoise);
        }
    }
}
