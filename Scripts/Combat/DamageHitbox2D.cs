using System.Collections.Generic;
using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// A melee swing's damage volume. An Area2D because that is what a Unity trigger collider becomes,
    /// but the hit test itself is still the explicit overlap query the Unity version ran every frame
    /// rather than a signal - the sweep has to be re-run each frame at a moving offset, and it has to
    /// report every overlapping target, not just the ones that entered this frame.
    ///
    /// Distances are pixels. The defaults below are the Unity numbers put through <see cref="World.U"/>.
    /// </summary>
    public partial class DamageHitbox2D : Area2D
    {
        [Export] private uint hitLayers;

        // Pixels, written by Configure from ReadabilityLayout.json. No initialisers: a volume that
        // reached the tree unconfigured reports it rather than swinging at a reach nobody authored
        // (K7b, PLAN_CLOSEOUT decision D1).
        [Export] private float radius;
        [Export] private Vector2 offset;

        // Written per swing by PlayerActionController.SetDamage, never at spawn - so there is no
        // authored initialiser to carry here, and a zero is what an unfired volume is worth.
        [Export] private float damage;

        /// <summary>
        /// Pixels. Authored in Unity metres as <c>CombatTuning.knockbackLight</c> (4 m, the same number
        /// <c>PlayerCombat.json.attackKnockback</c> authors for the swing this volume belongs to) and
        /// converted once inside <see cref="CombatTuningData.Load"/> - never re-scale it here. Was a bare
        /// <c>3f</c>, i.e. 3 px of metres-side value used as a pixel distance, which is effectively no
        /// knockback at all; nothing writes this field, so that literal was what every hit shipped with.
        /// Combat cannot read <c>PlayerCombat.json</c> directly: it must not learn about
        /// <c>MyGame.Player</c> (see CLAUDE.md), and <see cref="CombatTuningData"/> is the Combat-owned
        /// loader for the same authored number.
        /// </summary>
        private static readonly float DefaultKnockbackForce = CombatTuningData.Shared.knockbackLight;

        [Export] private float knockbackForce = DefaultKnockbackForce;
        [Export] private DamageType damageType;
        [Export] private bool autoTrigger;

        public System.Action<Health, Vector2> OnHit;
        public System.Action<Health, Vector2> OnParry;
        public System.Action<Health, Vector2> OnPerfectParry;

        private Node2D _owner;
        private bool _isActive;

        // Keyed on instance id rather than the node: a target freed mid-swing must not leave a dangling
        // reference in the set.
        private readonly HashSet<ulong> _hitThisCycle = new();
        private Color _hitFlashColor = Colors.White;
        private bool _configured;

        public override void _Ready()
        {
            CallDeferred(nameof(CheckConfigured));
        }

        private void CheckConfigured() => TuningGuard.Check(this, _configured, "Configure");

        public void Initialize(Node2D owner)
        {
            _owner = owner;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (!active)
                _hitThisCycle.Clear();
        }

        public void ClearHitCycle()
        {
            _hitThisCycle.Clear();
        }

        public void SetDamage(float dmg)
        {
            damage = dmg;
        }

        public void SetDamageType(DamageType type)
        {
            damageType = type;
        }

        public void Configure(float newRadius, Vector2 newOffset, uint newHitLayers)
        {
            _configured = true;
            radius = newRadius;
            offset = newOffset;
            hitLayers = newHitLayers;
        }

        public void SetOffset(Vector2 newOffset)
        {
            offset = newOffset;
        }

        public void SetHitFlashColor(Color color)
        {
            _hitFlashColor = color;
        }

        // Unity ran this in LateUpdate, after the owner had moved for the frame.
        public override void _Process(double delta)
        {
            if (_isActive || autoTrigger)
                CheckHits();
        }

        public void CheckHits()
        {
            if (_owner == null) return;

            Vector2 worldPos = _owner.GlobalPosition + offset * new Vector2(_owner.GlobalScale.X >= 0 ? 1f : -1f, 1f);
            List<GodotObject> hits = Phys2D.OverlapCircleAll(this, worldPos, radius, hitLayers);

            foreach (GodotObject collider in hits)
            {
                if (collider is not Node2D node) continue;
                if (_hitThisCycle.Contains(node.GetInstanceId())) continue;

                Health health = node.GetComponent<Health>();
                if (health != null)
                {
                    _hitThisCycle.Add(node.GetInstanceId());
                    Vector2 dir = (node.GlobalPosition - _owner.GlobalPosition).Normalized();

                    // Unity asked the collider for its ClosestPoint to the sweep centre. Godot's shape
                    // queries do not return one, so the hit point is the sweep centre nudged towards the
                    // target by at most the sweep radius - same place to within the size of the hitbox,
                    // and it is only ever used to position feedback.
                    Vector2 hitPoint = worldPos + (node.GlobalPosition - worldPos).LimitLength(radius);

                    var request = new DamageRequest(
                        _owner,
                        node,
                        hitPoint,
                        dir,
                        damage,
                        knockbackForce,
                        damageType);

                    DamageResult result = CombatResolver.Resolve(request);

                    if (result.WasParried)
                    {
                        if (result.WasPerfectParried)
                            OnPerfectParry?.Invoke(health, dir);
                        OnParry?.Invoke(health, dir);
                        continue;
                    }

                    if (!result.Applied)
                        continue;

                    CombatFeedback feedback = node.GetComponent<CombatFeedback>();
                    if (node.GetComponentInParent<CombatResultBroadcaster>() == null && feedback != null)
                        feedback.Play(result);

                    OnHit?.Invoke(result.TargetHealth, result.HitDirection);
                }
            }
        }
    }
}
