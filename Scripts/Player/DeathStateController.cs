using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// Death, the spirit walk that follows it, and the respawn at the end. A component node under the
    /// player body: the transform it moves on respawn is the body's, not its own.
    /// </summary>
    public partial class DeathStateController : Node2D
    {
        [Export] private float spiritStateDuration;

        /// <summary>
        /// The colour the body wears while it walks as a spirit. The artist's, not the designer's, so it
        /// comes from <c>Resources/Art/Readability.json</c> beside <c>spiritPlatformColor</c> - the
        /// platform the spirit walks to - rather than from the resource file the four numbers below come
        /// from. It was the last colour literal in a component (PLAN_CLOSEOUT K7b).
        /// </summary>
        [Export] private Color spiritTint;

        /// <summary>Fraction of max health the player keeps on entering spirit form.</summary>
        [Export] private float spiritEntryHealthPercent;

        [Export] private float respawnHealthPercent;
        [Export] private float respawnHumanityPercent;

        private bool _configured;

        public override void _Ready()
        {
            CallDeferred(nameof(CheckConfigured));
        }

        private void CheckConfigured() => TuningGuard.Check(this, _configured, "ApplyTuning");

        /// <summary>
        /// Writes the authored death numbers from <c>PlayerResources.json</c>, and the spirit tint from
        /// <c>Readability.json</c> beside them. Seconds and 0..1 fractions - none of them is a distance,
        /// so nothing is scaled here or in <see cref="PlayerResourceData.Load"/>. Two files in one call
        /// because there is one moment on the spawn path where both are in hand and the controller is
        /// tuned exactly once; a null <paramref name="resources"/> writes neither and the deferred check
        /// above reports it (PLAN_CLOSEOUT decision D1).
        /// </summary>
        public void ApplyTuning(PlayerResourceData resources, Color spiritTintColor)
        {
            if (resources == null)
                return;

            _configured = true;
            spiritStateDuration = resources.spiritStateDuration;
            spiritEntryHealthPercent = resources.spiritEntryHealthPercent;
            respawnHealthPercent = resources.respawnHealthPercent;
            respawnHumanityPercent = resources.respawnHumanityPercent;
            spiritTint = spiritTintColor;
        }

        public event Action OnDeath;
        public event Action OnEnterSpiritState;
        public event Action OnExitSpiritState;
        public event Action OnRespawn;
        public event Action OnSpiritPlatformActivated;

        public bool IsInSpiritState => _isInSpiritState;
        public int DeathCount => _deathCount;

        private Health _health;
        private HumanityController _humanity;
        private AudioFeedback _audioFeedback;
        private Sprite2D _spriteRenderer;
        private Node2D _checkpoint;
        private Node _spiritPlatform;
        private bool _isInSpiritState;
        private int _deathCount;
        private Color _originalColor = Colors.White;

        public void Initialize(Health health, HumanityController humanity, Node2D checkpoint, Node spiritPlatform)
        {
            _health = health;
            _humanity = humanity;
            _checkpoint = checkpoint;
            _spiritPlatform = spiritPlatform;

            _spriteRenderer = this.GetComponentInParent<Sprite2D>();
            _audioFeedback = this.GetComponentInParent<AudioFeedback>();
            if (_spriteRenderer != null)
            {
                _originalColor = _spriteRenderer.Modulate;
            }

            if (_health != null)
            {
                _health.OnHealthDepleted -= OnPlayerDeath;
                _health.OnHealthDepleted += OnPlayerDeath;
            }

            SetNodeActive(_spiritPlatform, false);
        }

        public override void _ExitTree()
        {
            if (_health != null)
            {
                _health.OnHealthDepleted -= OnPlayerDeath;
            }
        }

        public void SetCheckpoint(Node2D checkpoint)
        {
            _checkpoint = checkpoint;
        }

        public void SetSpiritPlatform(Node platform)
        {
            _spiritPlatform = platform;
            SetNodeActive(_spiritPlatform, _isInSpiritState);
        }

        private void OnPlayerDeath()
        {
            if (_isInSpiritState)
            {
                // A fatal hit while already in spirit form is still a death, so it still costs the
                // wallet. Returning without raising OnDeath left the wallet full when the resolver
                // published the result, and the kill reward then handed the whole balance to whoever
                // landed the blow instead of leaving a stain. The rest of the sequence is skipped
                // because it is already running: the spirit timer, the platform and the death count
                // all belong to the death that put the player here.
                OnDeath?.Invoke();
                return;
            }

            _deathCount++;
            OnDeath?.Invoke();

            if (_spiritPlatform != null)
            {
                SetNodeActive(_spiritPlatform, true);
                OnSpiritPlatformActivated?.Invoke();
            }

            EnterSpiritState();
        }

        /// <summary>
        /// Unity's <c>Invoke(nameof(ExitSpiritState), spiritStateDuration)</c> is a scene-tree timer and
        /// an await here. Scaled time either way, so hit-stop holds the spirit walk open just as it did.
        /// </summary>
        private async void EnterSpiritState()
        {
            _isInSpiritState = true;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.Modulate = spiritTint;
            }

            _health?.SetHealth(_health.MaxHealth * spiritEntryHealthPercent);

            OnEnterSpiritState?.Invoke();

            await ToSignal(GetTree().CreateTimer(spiritStateDuration), SceneTreeTimer.SignalName.Timeout);

            // The player can be freed mid-walk by a scene change; Unity's Invoke was cancelled with the
            // object, an await is not.
            if (!IsInstanceValid(this) || !IsInsideTree())
            {
                return;
            }

            ExitSpiritState();
        }

        private void ExitSpiritState()
        {
            _isInSpiritState = false;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.Modulate = _originalColor;
            }

            OnExitSpiritState?.Invoke();
            Respawn();
        }

        private void Respawn()
        {
            // World.V converts the Unity fallback (-3, 0.5) in metres, +Y up, to pixels with +Y down.
            Vector2 respawnPos = _checkpoint != null
                ? _checkpoint.GlobalPosition
                : World.V(new Vector2(-3f, 0.5f));

            // The transform that moves is the player body's. In Unity this component sat on the player
            // GameObject and wrote its own; here it is a child, and writing its own would slide the
            // component out from under the body and leave the body where it died.
            var body = this.GetComponentInParent<CharacterBody2D>();
            if (body != null)
            {
                body.GlobalPosition = respawnPos;
                body.Velocity = Vector2.Zero;
            }

            if (_health != null && respawnHealthPercent > 0)
            {
                _health.SetHealth(_health.MaxHealth * respawnHealthPercent);
            }

            if (_humanity != null && respawnHumanityPercent > 0)
            {
                _humanity.SetHumanity(_humanity.MaxHumanity * respawnHumanityPercent);
            }

            SetNodeActive(_spiritPlatform, false);

            _audioFeedback?.Play(AudioFeedbackCue.Respawn);

            this.GetComponentInParent<PlayerController2D>()?.ResetForRespawn();

            OnRespawn?.Invoke();
        }

        /// <summary>
        /// Unity's <c>GameObject.SetActive</c>. Godot has no single switch: hiding covers the visual,
        /// ProcessMode covers the scripts, and the collision shapes have to be told separately or a
        /// switched-off spirit platform would still hold the player up.
        /// </summary>
        private static void SetNodeActive(Node node, bool active)
        {
            if (node == null)
            {
                return;
            }

            if (node is CanvasItem item)
            {
                item.Visible = active;
            }

            node.ProcessMode = active ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;

            foreach (Node child in node.FindChildren("*", nameof(CollisionShape2D), true, false))
            {
                ((CollisionShape2D)child).SetDeferred(CollisionShape2D.PropertyName.Disabled, !active);
            }
        }
    }
}
