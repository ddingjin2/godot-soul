using System;
using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// Single sink for resolved combat results. Applies every side effect of a
    /// <see cref="DamageResult"/> (feedback, hit stop, camera shake, audio, resources) and then
    /// raises <see cref="OnResult"/> for anything wired up externally.
    /// Side effects run directly in <see cref="Publish"/> instead of through per-effect listener nodes,
    /// so they do not depend on node lifecycle callbacks and stay testable from a bare instance.
    /// </summary>
    public partial class CombatResultBroadcaster : Node
    {
        // Hit Stop
        [Export] private float lightHitStopDuration = 0.08f;
        [Export] private float heavyHitStopDuration = 0.12f;
        [Export] private float parryHitStopDuration = 0.08f;

        // Optional Overrides
        [Export] private CombatFeedback feedback;
        [Export] private AudioFeedback audioFeedback;

        public event Action<DamageResult> OnResult;

        public void Publish(DamageResult result)
        {
            ApplyFeedback(result);
            ApplyHitStop(result);
            ApplyCameraShake(result);
            ApplyAudio(result);
            ApplyResources(result);

            OnResult?.Invoke(result);
        }

        private void ApplyFeedback(DamageResult result)
        {
            if (feedback == null)
                feedback = this.GetComponentInParent<CombatFeedback>();

            if (feedback != null)
                feedback.Play(result);
        }

        private void ApplyHitStop(DamageResult result)
        {
            if (HitStopManager.Instance == null)
                return;

            if (result.WasParried)
                HitStopManager.Instance.TriggerHitStop(parryHitStopDuration);
            else if (result.Applied)
                HitStopManager.Instance.TriggerHitStop(result.DamageType == DamageType.Heavy ? heavyHitStopDuration : lightHitStopDuration);
        }

        private static void ApplyCameraShake(DamageResult result)
        {
            if (CameraShake.Instance == null)
                return;

            if (result.WasParried)
                CameraShake.Instance.TriggerShake(CameraShakePreset.Invulnerable);
            else if (result.Applied)
                CameraShake.Instance.TriggerShake(result.DamageType == DamageType.Heavy ? CameraShakePreset.HeavyHit : CameraShakePreset.LightHit);
        }

        private void ApplyAudio(DamageResult result)
        {
            if (audioFeedback == null)
                audioFeedback = this.GetComponentInParent<AudioFeedback>();
            if (audioFeedback == null)
                return;

            if (result.WasParried)
                audioFeedback.Play(AudioFeedbackCue.Parry);
            else if (result.Applied)
                audioFeedback.Play(result.DamageType == DamageType.Heavy ? AudioFeedbackCue.HeavyHit : AudioFeedbackCue.LightHit);
        }

        private static void ApplyResources(DamageResult result)
        {
            if (result.WasParried)
            {
                result.Target?.GetComponentInParent<SinResonanceController>()?.OnSuccessfulParry();
                return;
            }

            if (!result.Applied)
                return;

            SinResonanceController attackerResonance = result.Attacker != null
                ? result.Attacker.GetComponentInParent<SinResonanceController>()
                : null;
            SinResonanceController targetResonance = result.Target != null
                ? result.Target.GetComponentInParent<SinResonanceController>()
                : null;

            if (attackerResonance != null && attackerResonance != targetResonance)
                attackerResonance.OnAttackHit();

            targetResonance?.OnDamageTaken(result.FinalDamage);
            result.Target?.GetComponentInParent<HumanityController>()?.OnHit();
            AwardSouls(result);
        }

        /// <summary>
        /// A kill moves the target's whole wallet to whoever landed the blow. Every actor carries a
        /// <see cref="SoulsWallet"/>, so this is the only place souls change hands in combat.
        /// </summary>
        private static void AwardSouls(DamageResult result)
        {
            if (!result.Killed || result.Attacker == null || result.Target == null)
                return;

            SoulsWallet dropped = result.Target.GetComponentInParent<SoulsWallet>();
            SoulsWallet purse = result.Attacker.GetComponentInParent<SoulsWallet>();
            if (dropped == null || purse == null || dropped == purse)
                return;

            purse.Add(dropped.Drain());
        }
    }
}
