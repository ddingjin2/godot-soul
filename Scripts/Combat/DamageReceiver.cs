using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    public partial class DamageReceiver : Node
    {
        [Export] private Health health;

        // GetComponentInParent, not GetComponent: Health is a sibling node under the actor root here,
        // where in Unity it was a second component on the same GameObject.
        public Health TargetHealth => health != null ? health : this.GetComponentInParent<Health>();

        public void Initialize(Health targetHealth)
        {
            health = targetHealth;
        }

        public void ApplyDamage(DamageRequest request)
        {
            TargetHealth?.ApplyDamage(request.Damage, request.HitDirection, request.Knockback);
        }
    }
}
