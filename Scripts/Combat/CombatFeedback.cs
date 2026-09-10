using System;
using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// The visible half of taking a hit: a colour flash on the actor's sprite and a squash on its scale.
    /// </summary>
    public partial class CombatFeedback : Node2D
    {
        // Hit Flash
        [Export] private float hitFlashDuration = 0.08f;
        [Export] private Color hitFlashColor = Colors.White;
        [Export] private Color invulnFlashColor = new Color(0.5f, 0.5f, 1f, 1f);

        // Attack Impact
        [Export] private float impactScaleDuration = 0.1f;
        [Export] private float impactScaleAmount = 1.3f;

        public event Action OnHitFeedback;
        public event Action OnInvulnerableHit;

        private Sprite2D _sr;
        private Color _originalColor;
        private bool _isFlashing;

        // Unity used Invoke(nameof(EndFlash), d) and a coroutine. Both become a countdown in _Process:
        // same scaled-time behaviour, and nothing is left scheduled against a node that has been freed.
        private float _flashTimer;
        private float _impactTimer;
        private Vector2 _impactOriginalScale;

        public override void _Ready()
        {
            _sr = this.GetComponentInParent<Sprite2D>();
            if (_sr != null)
                _originalColor = _sr.Modulate;
        }

        public void TriggerHitFlash()
        {
            if (_sr == null || _isFlashing) return;

            PlayHitFeedback();
        }

        public void Play(DamageResult result)
        {
            if (result.WasParried)
            {
                TriggerParryFeedback();
                return;
            }

            if (!result.Applied)
                return;

            PlayHitFeedback(result.DamageType);
        }

        private void PlayHitFeedback(DamageType damageType = DamageType.Standard)
        {
            _ = damageType;
            OnHitFeedback?.Invoke();

            if (_sr == null || _isFlashing || !GraphicsOptions.HitFlash) return;

            _isFlashing = true;
            _sr.Modulate = hitFlashColor;
            _flashTimer = hitFlashDuration;
        }

        public void TriggerInvulnerableFlash()
        {
            TriggerInvulnerableFlash(AudioFeedbackCue.Invulnerable);
        }

        public void TriggerParryFeedback()
        {
            TriggerInvulnerableFlash(AudioFeedbackCue.Parry);
        }

        private void TriggerInvulnerableFlash(AudioFeedbackCue cue)
        {
            _ = cue;
            OnInvulnerableHit?.Invoke();
            if (_sr == null || !GraphicsOptions.HitFlash) return;

            _sr.Modulate = invulnFlashColor;
            _flashTimer = hitFlashDuration;
        }

        public void TriggerImpactScale()
        {
            if (!GraphicsOptions.HitFlash || _sr == null) return;

            // Unity squashed transform.localScale, which on a one-GameObject actor was the sprite. Here
            // the sprite is its own node, so the squash goes there - scaling this node would move nothing.
            if (_impactTimer <= 0f)
                _impactOriginalScale = _sr.Scale;

            _impactTimer = impactScaleDuration;
        }

        public override void _Process(double delta)
        {
            if (_flashTimer > 0f)
            {
                _flashTimer -= (float)delta;
                if (_flashTimer <= 0f)
                    EndFlash();
            }

            if (_impactTimer > 0f && _sr != null)
            {
                _impactTimer -= (float)delta;
                if (_impactTimer <= 0f)
                {
                    _sr.Scale = _impactOriginalScale;
                }
                else
                {
                    // Starts at impactScaleAmount and eases back to 1 over the duration, as the Unity
                    // coroutine did with Lerp(1, amount, 1 - t).
                    float t = 1f - (_impactTimer / impactScaleDuration);
                    _sr.Scale = _impactOriginalScale * Mathf.Lerp(1f, impactScaleAmount, 1f - t);
                }
            }
        }

        private void EndFlash()
        {
            if (_sr != null)
                _sr.Modulate = _originalColor;
            _isFlashing = false;
            _flashTimer = 0f;
        }

        public void SetOriginalColor(Color color)
        {
            _originalColor = color;
            if (_sr != null && !_isFlashing)
                _sr.Modulate = _originalColor;
        }

        // Unity's OnDisable. Godot has no enable/disable callback, so this runs when the node leaves the
        // tree - the sprite must not be left stuck on the flash colour.
        public override void _ExitTree()
        {
            EndFlash();
        }
    }
}
