using Godot;

namespace MyGame.Combat
{
    /// <summary>
    /// What a resolved hit actually did. <c>Attacker</c> and <c>Target</c> were Unity
    /// <c>GameObject</c>s; here they are the actor <see cref="Node"/>s that stand in for them.
    /// </summary>
    public readonly struct DamageResult
    {
        public DamageResult(
            Node attacker,
            Node target,
            bool applied,
            bool killed,
            bool wasParried,
            bool wasPerfectParried,
            float finalDamage,
            Vector2 hitPoint,
            Vector2 hitDirection,
            Health targetHealth,
            DamageType damageType = DamageType.None,
            bool staggered = false)
        {
            Attacker = attacker;
            Target = target;
            Applied = applied;
            Killed = killed;
            WasParried = wasParried;
            WasPerfectParried = wasPerfectParried;
            FinalDamage = finalDamage;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
            TargetHealth = targetHealth;
            DamageType = damageType;
            Staggered = staggered;
        }

        public Node Attacker { get; }
        public Node Target { get; }
        public bool Applied { get; }
        public bool Killed { get; }
        public bool WasParried { get; }
        public bool WasPerfectParried { get; }
        public float FinalDamage { get; }
        public Vector2 HitPoint { get; }
        public Vector2 HitDirection { get; }
        public Health TargetHealth { get; }
        public DamageType DamageType { get; }

        /// <summary>This hit emptied the target's <see cref="Poise"/> gauge and broke its action.</summary>
        public bool Staggered { get; }
    }
}
