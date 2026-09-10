using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>The four things souls buy. Ordered as the level-up panel lists them.</summary>
    public enum PlayerStat
    {
        Vitality,
        Endurance,
        Strength,
        Resolve
    }

    /// <summary>
    /// The soul sink. Souls buy levels, levels raise the four numbers the player already fights with -
    /// maximum health, maximum stamina, base attack damage and maximum poise - and the levels survive
    /// death. Only the wallet is dropped when you die ([[WorldSetting]], the soul stain), which is the
    /// genre contract this system exists to complete: without a sink a trash kill is a toll, and dying
    /// costs nothing worth slowing down for.
    ///
    /// Humanity is not touched. It is permanent spend in this fiction and levelling is not a way to burn
    /// it - see the note in <c>CheckpointZone.Activate</c> about not turning a rest into a full restore.
    /// </summary>
    /// <remarks>
    /// <para>Pure logic with nothing in the scene tree, so it is a plain class owned by
    /// <see cref="PlayerController2D"/> rather than a node. It was a MonoBehaviour in Unity only because
    /// that was the only way to sit on the player, which is why <see cref="EnsureOn"/> now looks the
    /// controller up instead of adding a component.</para>
    ///
    /// <para><b>Bases are captured, not accumulated.</b> Every level is applied as
    /// <c>base + perLevel * level</c> written absolutely onto the target component, never as an increment
    /// added to whatever is there. That is what makes <see cref="ApplyToPlayer"/> safe to call any number
    /// of times: two purchases cannot double-count, and a re-run of the same recompute cannot drift.</para>
    ///
    /// <para><b>Bind after tuning, never before.</b> The bases come off the live components the first time
    /// this object is used, so it must not be bound until <c>PlayerResourceData</c> and
    /// <c>PlayerCombatData</c> have been applied to them - otherwise the untuned defaults get captured as
    /// the base instead of the authored ones. That is why nothing binds on construction: the first call to
    /// any public member below binds, and every one of those calls happens after the spawner has finished
    /// tuning. If a tuning file is ever re-applied at runtime, call <see cref="Bind"/> again at that
    /// moment and then <see cref="ApplyToPlayer"/>.</para>
    /// </remarks>
    public sealed class PlayerProgression
    {
        /// <summary>Raised whenever any level changes, from a purchase or from a loaded save.</summary>
        public event Action OnLevelsChanged;

        private readonly Node _owner;

        private int vitalityLevel;
        private int enduranceLevel;
        private int strengthLevel;
        private int resolveLevel;

        private ProgressionTuningData _tuning;
        private SoulsWallet _wallet;
        private Health _health;
        private StaminaSystem _stamina;
        private Poise _poise;
        private PlayerActionController _actions;

        private bool _bound;
        private float _baseMaxHealth;
        private float _baseMaxStamina;
        private float _baseAttackDamage;
        private float _baseMaxPoise;

        /// <param name="owner">Any node on the player actor; the four target components are resolved off it.</param>
        public PlayerProgression(Node owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// The player node this progression belongs to. It was a component in Unity, so callers that
        /// used to pass the progression itself to a component lookup pass this instead.
        /// </summary>
        public Node Owner => _owner;

        // Named accessors mirror the four save fields one for one, so the save bridge round trip is a
        // read and a write with nothing to translate.
        public int VitalityLevel => vitalityLevel;
        public int EnduranceLevel => enduranceLevel;
        public int StrengthLevel => strengthLevel;
        public int ResolveLevel => resolveLevel;

        /// <summary>Levels bought across every stat. This is what the cost curve is driven by.</summary>
        public int TotalLevels => vitalityLevel + enduranceLevel + strengthLevel + resolveLevel;

        public ProgressionTuningData Tuning
        {
            get
            {
                EnsureBound();
                return _tuning;
            }
        }

        /// <summary>
        /// The progression belonging to a player actor. Every caller outside this folder - the save
        /// bridge, the level-up panel, a test - goes through here rather than reaching into the
        /// controller, exactly as they went through the Unity <c>EnsureOn(GameObject)</c>.
        /// </summary>
        public static PlayerProgression EnsureOn(Node player)
        {
            if (player == null)
            {
                return null;
            }

            PlayerController2D controller = player as PlayerController2D ?? player.GetComponentInParent<PlayerController2D>();
            if (controller == null)
            {
                return null;
            }

            PlayerProgression progression = controller.Progression;
            progression?.EnsureBound();
            return progression;
        }

        /// <summary>
        /// Hands over the tuning explicitly and re-reads the bases off the components as they stand.
        /// Optional: leaving it unbound loads the same design file lazily. Call it only at a moment when
        /// no level bonus is applied - right after a tuning pass - for the reason in the remarks above.
        /// </summary>
        public void Bind(ProgressionTuningData tuning)
        {
            _tuning = tuning;
            _bound = false;
            EnsureBound();
        }

        public int LevelOf(PlayerStat stat)
        {
            switch (stat)
            {
                case PlayerStat.Vitality: return vitalityLevel;
                case PlayerStat.Endurance: return enduranceLevel;
                case PlayerStat.Strength: return strengthLevel;
                case PlayerStat.Resolve: return resolveLevel;
                default: return 0;
            }
        }

        /// <summary>How far this stat can still go. Zero means it is capped.</summary>
        public bool IsAtCap(PlayerStat stat)
        {
            EnsureBound();
            return LevelOf(stat) >= Mathf.Max(0, _tuning.maxLevelPerStat);
        }

        /// <summary>
        /// Souls for the next level of this stat, or zero when it is capped. Zero is the panel's cue to
        /// show "max" rather than a price, and <see cref="TryPurchase"/> refuses it either way because
        /// <see cref="SoulsWallet.TrySpend"/> rejects a non-positive amount.
        /// </summary>
        public int CostOf(PlayerStat stat)
        {
            EnsureBound();
            return IsAtCap(stat) ? 0 : _tuning.CostForNextLevel(TotalLevels);
        }

        /// <summary>Whether the purse covers the next level and the stat has room for it.</summary>
        public bool CanPurchase(PlayerStat stat)
        {
            int cost = CostOf(stat);
            return cost > 0 && _wallet != null && _wallet.Souls >= cost;
        }

        /// <summary>
        /// Spends the souls and takes the level. False when the purse is short or the stat is capped, and
        /// nothing is spent in either case - the wallet is the one that decides, so there is no window
        /// where the level lands and the souls do not.
        /// </summary>
        /// <remarks>
        /// Where this may be called from is not this object's business, the same way
        /// <c>GateTravelZone</c> and not the panel decides whether the player stands in the portal. The
        /// rest interaction is what puts the panel on screen; nothing else opens it.
        /// </remarks>
        public bool TryPurchase(PlayerStat stat)
        {
            EnsureBound();

            int cost = CostOf(stat);
            if (cost <= 0 || _wallet == null || !_wallet.TrySpend(cost))
            {
                return false;
            }

            float healthBefore = _health != null ? _health.MaxHealth : 0f;
            float staminaBefore = _stamina != null ? _stamina.MaxStamina : 0f;
            float poiseBefore = _poise != null ? _poise.MaxPoise : 0f;

            Raise(stat);
            ApplyToPlayer();

            // The capacity just bought is usable now rather than after the next rest. Only the difference
            // is granted, so a purchase is not a sideways heal.
            _health?.Heal(_health.MaxHealth - healthBefore);
            _stamina?.Restore(_stamina.MaxStamina - staminaBefore);
            _poise?.SetPoise(_poise.CurrentPoise + (_poise.MaxPoise - poiseBefore));

            OnLevelsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Writes the levels a save slot holds and applies them. The counterpart of the four
        /// <c>GameSaveData</c> level fields; clamped here so a hand-edited slot cannot hand the player a
        /// level past the cap or a negative one.
        /// </summary>
        public void SetLevels(int vitality, int endurance, int strength, int resolve)
        {
            EnsureBound();

            int cap = Mathf.Max(0, _tuning.maxLevelPerStat);
            vitalityLevel = Mathf.Clamp(vitality, 0, cap);
            enduranceLevel = Mathf.Clamp(endurance, 0, cap);
            strengthLevel = Mathf.Clamp(strength, 0, cap);
            resolveLevel = Mathf.Clamp(resolve, 0, cap);

            ApplyToPlayer();
            OnLevelsChanged?.Invoke();
        }

        /// <summary>
        /// Recomputes every stat from its base and writes it. Idempotent by construction - it sets
        /// absolute values rather than adding - so calling it twice, or after a tuning pass has reset a
        /// maximum, lands on the same numbers.
        /// </summary>
        public void ApplyToPlayer()
        {
            EnsureBound();

            _health?.SetMaxHealth(_baseMaxHealth + Gain(_tuning.vitalityPerLevel, vitalityLevel));
            _stamina?.SetMaxStamina(_baseMaxStamina + Gain(_tuning.endurancePerLevel, enduranceLevel));
            _actions?.SetAttackDamage(_baseAttackDamage + Gain(_tuning.strengthPerLevel, strengthLevel));
            _poise?.SetMaxPoise(_baseMaxPoise + Gain(_tuning.resolvePerLevel, resolveLevel));
        }

        /// <summary>
        /// A level is never allowed to take a stat below where it started. The design file is
        /// hand-authored, and a negative per-level number would otherwise walk the player's maximum
        /// health down to zero, which reads as death rather than as a bad tuning value.
        /// </summary>
        private static float Gain(float perLevel, int level)
        {
            return Mathf.Max(0f, perLevel) * Mathf.Max(0, level);
        }

        private void Raise(PlayerStat stat)
        {
            switch (stat)
            {
                case PlayerStat.Vitality: vitalityLevel++; break;
                case PlayerStat.Endurance: enduranceLevel++; break;
                case PlayerStat.Strength: strengthLevel++; break;
                case PlayerStat.Resolve: resolveLevel++; break;
            }
        }

        private void EnsureBound()
        {
            if (_bound)
            {
                return;
            }

            _bound = true;

            _wallet = _owner.GetComponentInParent<SoulsWallet>();
            _health = _owner.GetComponentInParent<Health>();
            _stamina = _owner.GetComponentInParent<StaminaSystem>();
            _poise = _owner.GetComponentInParent<Poise>();
            _actions = _owner.GetComponentInParent<PlayerActionController>();

            _tuning ??= ProgressionTuningData.Load();

            _baseMaxHealth = _health != null ? _health.MaxHealth : 0f;
            _baseMaxStamina = _stamina != null ? _stamina.MaxStamina : 0f;
            _baseAttackDamage = _actions != null ? _actions.AttackDamage : 0f;
            _baseMaxPoise = _poise != null ? _poise.MaxPoise : 0f;
        }
    }
}
