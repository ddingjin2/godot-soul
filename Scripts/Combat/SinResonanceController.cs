using System;
using Godot;

namespace MyGame.Combat
{
    public partial class SinResonanceController : Node
    {
        // Resonance
        [Export] private float currentResonance = 0f;
        [Export] private float maxResonance = 100f;
        [Export] private float resonancePerHit = 5f;
        [Export] private float resonancePerParry = 25f;
        [Export] private float resonancePerDamage = 3f;

        /// <summary>
        /// One row per sin. A sin with no row, or a row left at its neutral values, changes nothing.
        /// Not [Export]ed - see <see cref="SinTuningData.sins"/> for why the table stays plain C#.
        /// </summary>
        private SinModifiers[] sinModifiers = DefaultSinModifiers();

        // Activation
        [Export] private float activeDuration = 5f;
        [Export] private float cooldownDuration = 10f;
        [Export] private float resonanceCost = 50f;

        // Humanity Cost
        [Export] private float humanityCostOnActivate = 5f;

        public event Action<SinState> OnSinActivated;
        public event Action OnSinDeactivated;
        public event Action<SinState> OnSinStateChanged;

        /// <summary>
        /// Raised when a sin takes effect. The owning actor applies the modifiers; this controller
        /// deliberately does not know what kind of actor that is.
        /// </summary>
        public Action<SinState, float, float, float, float, bool> OnApplyModifiers;

        /// <summary>Raised when the modifiers should return to their neutral values.</summary>
        public Action OnResetModifiers;

        public float CurrentResonance => currentResonance;
        public float NormalizedResonance => maxResonance > 0 ? currentResonance / maxResonance : 0f;
        public SinState CurrentSin => _activeSin;
        public bool IsSinActive => _activeSin != SinState.None;
        public float ResonancePerHit => resonancePerHit;
        public float ResonancePerParry => resonancePerParry;
        public float ResonancePerDamage => resonancePerDamage;

        private SinState _activeSin = SinState.None;
        private float _activeTimer;
        private float _cooldownTimer;
        private HumanityController _humanity;

        /// <summary>
        /// An empty table means the shipped defaults were lost somewhere; refill it rather than run every
        /// sin at neutral. In Unity this also ran from OnValidate, to put the table in front of a designer
        /// in the inspector - the table is a JSON file here, so there is nothing to repair in the editor.
        /// </summary>
        private void EnsureTable()
        {
            if (sinModifiers == null || sinModifiers.Length == 0)
                sinModifiers = DefaultSinModifiers();
        }

        public void Initialize(HumanityController humanity)
        {
            _humanity = humanity;
        }

        /// <summary>
        /// Takes the designer-authored numbers over the built-in ones. Null - which is what a scene with
        /// no design file yields - leaves the shipped defaults in place, so the controller is never worse
        /// off for the file being absent.
        /// </summary>
        public void ApplyTuning(SinTuningData tuning)
        {
            if (tuning == null)
                return;

            maxResonance = tuning.maxResonance;
            resonancePerHit = tuning.resonancePerHit;
            resonancePerParry = tuning.resonancePerParry;
            resonancePerDamage = tuning.resonancePerDamage;
            activeDuration = tuning.activeDuration;
            cooldownDuration = tuning.cooldownDuration;
            resonanceCost = tuning.resonanceCost;
            humanityCostOnActivate = tuning.humanityCostOnActivate;

            // An authored file with no rows is a file someone is still filling in, not an instruction to
            // run every sin neutral. Only a table that actually has rows replaces the shipped one.
            if (tuning.sins != null && tuning.sins.Length > 0)
                sinModifiers = tuning.sins;

            currentResonance = Mathf.Min(currentResonance, maxResonance);
        }

        public override void _Process(double delta)
        {
            if (_activeSin != SinState.None)
            {
                _activeTimer -= (float)delta;
                if (_activeTimer <= 0f)
                    DeactivateSin();
            }

            if (_cooldownTimer > 0f)
                _cooldownTimer -= (float)delta;
        }

        public void RequestActivateSin(SinState sin)
        {
            if (_activeSin != SinState.None) return;
            if (_cooldownTimer > 0f) return;

            TryActivateSin(sin);
        }

        public void AddResonance(float amount)
        {
            currentResonance = Mathf.Min(maxResonance, currentResonance + amount);
        }

        public void OnAttackHit()
        {
            AddResonance(resonancePerHit);
        }

        public void OnSuccessfulParry()
        {
            AddResonance(resonancePerParry);
        }

        public void OnDamageTaken(float damage)
        {
            AddResonance(resonancePerDamage * (damage / 10f));
        }

        private void TryActivateSin(SinState sin)
        {
            if (currentResonance < resonanceCost) return;
            if (_humanity != null)
            {
                if (!_humanity.TrySpend(humanityCostOnActivate))
                    return;
            }

            ActivateSin(sin);
        }

        private void ActivateSin(SinState sin)
        {
            OnResetModifiers?.Invoke();

            _activeSin = sin;
            _activeTimer = activeDuration;
            _cooldownTimer = cooldownDuration;
            currentResonance -= resonanceCost;

            ApplySinEffects(sin);
            OnSinActivated?.Invoke(sin);
            OnSinStateChanged?.Invoke(sin);
        }

        private void DeactivateSin()
        {
            SinState previous = _activeSin;
            _activeSin = SinState.None;

            OnResetModifiers?.Invoke();

            OnSinDeactivated?.Invoke();
            OnSinStateChanged?.Invoke(SinState.None);
        }

        private void ApplySinEffects(SinState sin)
        {
            // Shortening the table is how a live sin goes quietly neutral: every lookup still succeeds,
            // it just returns nothing. Said out loud here, once per activation rather than on the
            // per-hit lookups, so a designer editing SinTuning.json finds out from the console instead
            // of from a sin that stopped mattering.
            if (!HasRow(sin))
                GD.PushWarning($"SinResonanceController: {sin} has no row in the sin modifier table, so activating it changes nothing. Add a row to SinTuning.json.");

            SinModifiers m = ModifiersFor(sin);
            OnApplyModifiers?.Invoke(
                sin,
                m.speedMultiplier,
                m.damageMultiplier,
                m.dodgeDurationMultiplier,
                m.parryWindowMultiplier,
                m.disableHeal);
        }

        public float GetDamageTakenMultiplier()
        {
            return ModifiersFor(_activeSin).damageTakenMultiplier;
        }

        public bool HasPerfectParryBonus()
        {
            return ModifiersFor(_activeSin).perfectParryBonus;
        }

        /// <summary>
        /// Looks a sin up in the table. Matching on the row's own <see cref="SinModifiers.sin"/> rather
        /// than on array index means a designer can reorder or shorten the table without silently
        /// handing one sin another sin's numbers.
        /// </summary>
        public bool HasRow(SinState sin)
        {
            EnsureTable();

            for (int i = 0; i < sinModifiers.Length; i++)
            {
                if (sinModifiers[i] != null && sinModifiers[i].sin == sin)
                    return true;
            }

            return false;
        }

        private SinModifiers ModifiersFor(SinState sin)
        {
            EnsureTable();

            for (int i = 0; i < sinModifiers.Length; i++)
            {
                if (sinModifiers[i] != null && sinModifiers[i].sin == sin)
                    return sinModifiers[i];
            }

            return Neutral;
        }

        /// <summary>
        /// A fresh instance per lookup, not one shared readonly field. Every sin without a row would
        /// otherwise hand back the same object, and one line that ever wrote through it would poison all
        /// of them at once. Only reached when a sin has no row, so the allocation is not on a hot path.
        /// </summary>
        /// <remarks>
        /// <see cref="SinModifiers"/> stays a class for this reason too: as a struct a <c>default</c>
        /// value would be all zeroes - a sin that multiplies speed and damage by nothing.
        /// </remarks>
        private static SinModifiers Neutral => new SinModifiers();

        /// <summary>
        /// The shipped table. Wrath, Sloth and Pride carry exactly the numbers their hardcoded fields
        /// held; anything not named here is neutral (1.0 / false).
        ///
        /// Gluttony, Greed, Envy and Lust were neutral placeholders until the team lead called it on
        /// 2026-08-10: each one now owns a lever the other six do not lean on, so pressing it changes a
        /// different thing about the fight rather than being a second Wrath.
        ///
        /// Gluttony eats the hit - the only sin that lowers damage taken, paid for in speed and a heavier
        /// roll. Greed is the all-in: the largest damage number in the table, the largest damage taken,
        /// and no flask, so it is a purse you cannot stop carrying. Envy mirrors the attacker - the widest
        /// parry window there is, at full speed, and it costs defence rather than movement (which is what
        /// keeps it from being Sloth). Lust is distance: fastest feet and the longest roll, bought with
        /// damage and almost all of the parry window.
        ///
        /// These are first numbers, derived rather than felt, and this table is the fallback - the
        /// authored values live in <c>Resources/Design/SinTuning.json</c> and outrank it.
        /// </summary>
        private static SinModifiers[] DefaultSinModifiers()
        {
            return new[]
            {
                new SinModifiers
                {
                    sin = SinState.Wrath,
                    speedMultiplier = 1.3f,
                    damageMultiplier = 1.5f,
                    dodgeDurationMultiplier = 0.7f,
                    parryWindowMultiplier = 0.8f,
                    disableHeal = true
                },
                new SinModifiers
                {
                    sin = SinState.Sloth,
                    speedMultiplier = 0.7f,
                    parryWindowMultiplier = 1.5f
                },
                new SinModifiers
                {
                    sin = SinState.Pride,
                    damageMultiplier = 1.3f,
                    damageTakenMultiplier = 1.5f,
                    perfectParryBonus = true
                },
                new SinModifiers
                {
                    sin = SinState.Gluttony,
                    speedMultiplier = 0.85f,
                    damageMultiplier = 1.15f,
                    dodgeDurationMultiplier = 1.3f,
                    damageTakenMultiplier = 0.75f
                },
                new SinModifiers
                {
                    sin = SinState.Greed,
                    damageMultiplier = 1.4f,
                    damageTakenMultiplier = 1.4f,
                    disableHeal = true
                },
                new SinModifiers
                {
                    sin = SinState.Envy,
                    speedMultiplier = 1.1f,
                    damageMultiplier = 1.1f,
                    parryWindowMultiplier = 1.6f,
                    damageTakenMultiplier = 1.2f
                },
                new SinModifiers
                {
                    sin = SinState.Lust,
                    speedMultiplier = 1.35f,
                    damageMultiplier = 0.85f,
                    dodgeDurationMultiplier = 1.35f,
                    parryWindowMultiplier = 0.7f
                }
            };
        }
    }
}
