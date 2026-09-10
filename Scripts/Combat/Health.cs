using System;
using Godot;

namespace MyGame.Combat
{
    /// <summary>
    /// The hit points of one actor. A child node of the actor root, found through
    /// <c>NodeExt.GetComponent</c> / <c>GetComponentInParent</c> the way Unity found the component.
    /// </summary>
    public partial class Health : Node
    {
        [Export] private float maxHealth = 100f;
        [Export] private float currentHealth;
        [Export] private bool disableOnDeath = true;

        // These were UnityEvents built with a field initialiser, because Unity edit mode never called
        // Awake and a null UnityEvent would have thrown there. A C# event needs no initialisation, so
        // the workaround is simply gone.
        public event Action<float> OnHealthChanged;
        public event Action OnHealthDepleted;
        public event Action OnDamaged;
        public event Action OnHealed;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float NormalizedHealth => maxHealth > 0 ? currentHealth / maxHealth : 0f;
        public bool IsDead => currentHealth <= 0f;

        /// <summary>
        /// Impulse of the hit that raised the last <see cref="OnDamaged"/>. Health does not move anything
        /// itself; the actor that owns the motor reads this from its own damage listener.
        /// </summary>
        public Vector2 LastHitDirection { get; private set; }
        public float LastKnockback { get; private set; }

        public override void _Ready()
        {
            currentHealth = maxHealth;
        }

        public virtual void ApplyDamage(float amount, Vector2 knockbackDirection, float knockbackForce = 0f, bool ignoreInvulnerability = false)
        {
            if (IsDead) return;

            LastHitDirection = knockbackDirection;
            LastKnockback = knockbackForce;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
            OnHealthChanged?.Invoke(currentHealth);
            OnDamaged?.Invoke();

            if (IsDead && disableOnDeath)
                OnHealthDepleted?.Invoke();
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            OnHealthChanged?.Invoke(currentHealth);
            if (amount > 0)
                OnHealed?.Invoke();
        }

        public void SetHealth(float value)
        {
            currentHealth = Mathf.Clamp(value, 0f, maxHealth);
            OnHealthChanged?.Invoke(currentHealth);
        }

        public void SetMaxHealth(float value)
        {
            maxHealth = value;
            currentHealth = Mathf.Min(currentHealth, maxHealth);
            OnHealthChanged?.Invoke(currentHealth);
        }
    }
}
