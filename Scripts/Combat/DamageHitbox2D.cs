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
        [Export] private float radius = World.Ppu * 0.5f;
        [Export] private Vector2 offset = new Vector2(World.Ppu, 0f);
        [Export] private float damage = 10f;
        [Export] private float knockbackForce = 3f;
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
