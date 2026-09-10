using Godot;

namespace MyGame.Combat
{
    /// <summary>
    /// One attack, on its way to a target. <c>Attacker</c> and <c>Target</c> were Unity
    /// <c>GameObject</c>s; here they are the actor <see cref="Node"/>s that stand in for them.
    /// </summary>
    public readonly struct DamageRequest
    {
        public DamageRequest(
            Node attacker,
            Node target,
            Vector2 hitPoint,
            Vector2 hitDirection,
            float damage,
            float knockback,
            DamageType damageType)
        {
            Attacker = attacker;
            Target = target;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
            Damage = damage;
            Knockback = knockback;
            DamageType = damageType;
        }

        /// <summary>
        /// The same hit at a different weight. Used by <see cref="CombatResolver"/> to fold the target's
        /// damage-taken multipliers in before anything reads the number - the kill check, the poise spend
        /// and the published result all have to agree with what the health bar actually lost.
        /// </summary>
        public DamageRequest WithDamage(float damage)
        {
            return new DamageRequest(Attacker, Target, HitPoint, HitDirection, damage, Knockback, DamageType);
        }

        public Node Attacker { get; }
        public Node Target { get; }
        public Vector2 HitPoint { get; }
        public Vector2 HitDirection { get; }
        public float Damage { get; }
        public float Knockback { get; }
        public DamageType DamageType { get; }
    }
}
