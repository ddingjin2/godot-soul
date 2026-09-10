using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// Where a hit is decided. A plain static class, not a node: it owns no state and lives nowhere in
    /// the scene tree, exactly as it did in Unity.
    /// </summary>
    public static class CombatResolver
    {
        public static DamageResult Resolve(DamageRequest request)
        {
            if (request.Target == null)
                return Publish(CreateNoOpResult(request, false, false, null));

            DamageReceiver receiver = request.Target.GetComponent<DamageReceiver>();
            Health health = receiver != null ? receiver.TargetHealth : request.Target.GetComponent<Health>();
            if (health == null)
                return Publish(CreateNoOpResult(request, false, false, null));

            var guard = request.Target.GetComponent<IDamageGuard>();

            if (guard != null && guard.IsInParryWindow())
            {
                bool perfect = guard.IsInPerfectParryWindow();
                guard.NotifyParrySuccess(perfect);
                return Publish(CreateNoOpResult(request, true, perfect, health));
            }

            bool wasAlive = !health.IsDead;
            if (!wasAlive)
                return Publish(CreateNoOpResult(request, false, false, health));

            if (guard != null && guard.IsInvulnerable())
                return Publish(CreateNoOpResult(request, false, false, health));

            // Folded in here, before anything reads the number: the kill check below, the poise spend and
            // the result the HUD renders all have to be the damage that was actually taken. Scaling it
            // inside DamageReceiver instead would leave a hit that kills reported as one that did not.
            request = request.WithDamage(IncomingDamage(request));

            bool killed = request.Damage >= health.CurrentHealth;

            // TODO: route knockback to PlayerMotor2D/enemy motor APIs once existing damage responses are ready for a behavior-changing pass.
            if (receiver != null)
                receiver.ApplyDamage(request);
            else
                health.ApplyDamage(request.Damage, request.HitDirection, request.Knockback);

            bool staggered = ApplyPoise(request, killed);

            return Publish(new DamageResult(
                request.Attacker,
                request.Target,
                true,
                killed,
                false,
                false,
                request.Damage,
                request.HitPoint,
                request.HitDirection,
                health,
                request.DamageType,
                staggered));
        }

        /// <summary>
        /// What this target actually takes. Two multipliers, both of them the player's:
        /// <see cref="SinResonanceController.GetDamageTakenMultiplier"/> - what the active sin costs in
        /// skin, which is Pride's whole downside and Gluttony's whole upside - and the difficulty the run
        /// is being played on.
        ///
        /// The sin controller is also how "is this the player" is answered. Combat is not allowed to know
        /// about <c>MyGame.Player</c>, and the sins are a thing only the voyager has, so an actor carrying
        /// one is the voyager. An enemy has neither and takes its hit unscaled - difficulty makes enemies
        /// tougher through their health, not through a shield on every blow.
        /// </summary>
        private static float IncomingDamage(DamageRequest request)
        {
            var sins = request.Target.GetComponentInParent<SinResonanceController>();
            if (sins == null)
                return request.Damage;

            return Mathf.Max(0f, request.Damage * sins.GetDamageTakenMultiplier() * DifficultySettings.PlayerDamageTakenMultiplier);
        }

        /// <summary>
        /// Spends the target's poise for this hit and breaks its action when the gauge empties.
        /// Runs here rather than in the broadcaster because a stagger has to land before anything reads
        /// the result: a listener that saw <see cref="DamageResult.Staggered"/> would otherwise be
        /// looking at an actor that has not been staggered yet.
        /// A killing blow is skipped - there is nothing left to interrupt, and breaking poise on a
        /// corpse would fire a stagger reaction on an actor already running its death path.
        /// </summary>
        private static bool ApplyPoise(DamageRequest request, bool killed)
        {
            if (killed)
                return false;

            // Searched from the parent like the kill reward is: the target can be a child hurtbox, and
            // the gauge always sits on the actor root.
            var poise = request.Target.GetComponentInParent<Poise>();
            if (poise == null || !poise.ApplyHit(request.Damage, request.DamageType))
                return false;

            request.Target.GetComponentInParent<IStaggerable>()?.OnPoiseBroken();
            return true;
        }

        private static DamageResult Publish(DamageResult result)
        {
            CombatResultBroadcaster broadcaster = result.Target != null
                ? result.Target.GetComponentInParent<CombatResultBroadcaster>()
                : null;
            broadcaster?.Publish(result);
            return result;
        }

        private static DamageResult CreateNoOpResult(DamageRequest request, bool wasParried, bool wasPerfectParried, Health targetHealth)
        {
            return new DamageResult(
                request.Attacker,
                request.Target,
                false,
                targetHealth != null && targetHealth.IsDead,
                wasParried,
                wasPerfectParried,
                0f,
                request.HitPoint,
                request.HitDirection,
                targetHealth,
                request.DamageType);
        }
    }
}
