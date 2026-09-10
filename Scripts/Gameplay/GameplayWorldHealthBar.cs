using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The little bar over an enemy's head. Two sprites - a frame and a fill - with the fill scaled
    /// along x by the normalised health.
    /// </summary>
    /// <remarks>
    /// Unity used <c>SpriteRenderer.size</c> under <c>SpriteDrawMode.Sliced</c> for the bar's dimensions
    /// and the fill transform's <c>localScale</c> for how full it is - two independent knobs on one
    /// object. Godot's Sprite2D has no size, so size *is* scale (<c>EnemyShim.SetSpriteSize</c>), and the
    /// two knobs are split across two nodes: the fill pivot carries the fullness, the sprite under it
    /// carries the size.
    /// </remarks>
    public partial class GameplayWorldHealthBar : Node2D
    {
        /// <summary>
        /// The authored bar. Its root is the <c>HealthBar</c> child this component looks for, not a
        /// GameplayWorldHealthBar: the component is added to an actor, the tree under it is instanced.
        /// </summary>
        private const string ScenePath = "res://Scenes/World/WorldHealthBar.tscn";

        /// <summary>Bar size in Godot pixels.</summary>
        private Vector2 _size = new(World.Ppu * 1.2f, World.Ppu * 0.12f);

        /// <summary>Offset from the actor in Godot pixels (+Y down), so above it is negative.</summary>
        private Vector2 _offset = new(0f, World.Ppu * -1.2f);

        private Color _fillColor = new(0.85f, 0.08f, 0.08f);

        /// <summary>INK_950 #06070A a0.92.</summary>
        private static readonly Color FrameColor = new(0.023529412f, 0.02745098f, 0.039215688f, 0.92f);

        /// <summary>BONE_100 #C3BDB1. Low health lifts the fill toward bone, not toward white.</summary>
        private static readonly Color LowHealthTint = new(0.7647059f, 0.7411765f, 0.69411767f);

        /// <summary>The frame's margin around the fill. A literal of this file's own, so Unity metres.</summary>
        private const float FrameMargin = 0.08f;

        private Health _health;
        private Node2D _barRoot;
        private Sprite2D _frameRenderer;
        private Node2D _fill;
        private Sprite2D _fillRenderer;
        private float _baseFillWidth;
        private bool _subscribed;

        /// <summary>
        /// <paramref name="barSize"/> and <paramref name="barOffset"/> arrive in Godot pixels with +Y
        /// down: <c>GameplayReadabilityDefaults</c> is the conversion boundary and has already scaled the
        /// size and flipped the offset. Nothing is converted here.
        /// </summary>
        public void Initialize(Health health, Vector2 barSize, Vector2 barOffset, Color color)
        {
            Unsubscribe();
            _health = health;
            _size = barSize;
            _offset = barOffset;
            _fillColor = color;
            Build();
            ApplyLayout();
            Subscribe();
            UpdateBar(_health != null ? _health.CurrentHealth : 0f);
        }

        public override void _Ready()
        {
            if (_barRoot == null)
                Build();

            _health ??= this.GetComponentInParent<Health>();
            Subscribe();
            UpdateBar(_health != null ? _health.CurrentHealth : 0f);
        }

        public override void _ExitTree()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_health == null || _subscribed)
                return;

            _health.OnHealthChanged += UpdateBar;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_health == null || !_subscribed)
                return;

            _health.OnHealthChanged -= UpdateBar;
            _subscribed = false;
        }

        /// <summary>
        /// Lookup first, instance second - and it never builds a node itself any more. An actor whose
        /// bar is already authored under it keeps that one; anything else gets
        /// <c>Scenes/World/WorldHealthBar.tscn</c>, whose root <i>is</i> the <c>HealthBar</c> node,
        /// because the component this method sits on is added by
        /// <c>GameplayBuildShim.AddComponent</c> rather than instanced.
        /// </summary>
        private void Build()
        {
            if (_barRoot != null)
                return;

            // Reused when the actor was built with the bar already under it; _barRoot is not exported
            // and is null on every fresh instance.
            _barRoot = GetNodeOrNull<Node2D>("HealthBar");
            if (_barRoot == null)
            {
                _barRoot = GD.Load<PackedScene>(ScenePath).Instantiate<Node2D>();
                AddChild(_barRoot);
            }

            _frameRenderer = _barRoot.GetNodeOrNull<Sprite2D>("Frame");

            // The pivot the fullness scales, with the sized sprite beneath it.
            _fill = _barRoot.GetNodeOrNull<Node2D>("Fill");
            _fillRenderer = _fill?.GetNodeOrNull<Sprite2D>("FillSprite");

            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (_barRoot == null)
                return;

            _barRoot.Position = _offset;

            Vector2 sizePx = _size;
            _baseFillWidth = sizePx.X;

            if (_frameRenderer != null)
            {
                _frameRenderer.SetSpriteSize(sizePx + new Vector2(World.U(FrameMargin), World.U(FrameMargin)));

                // The scene authors this same colour; re-applying it is what keeps an authored bar and a
                // freshly instanced one identical, and it is what the P0 suite reads back.
                _frameRenderer.Modulate = FrameColor;
            }

            if (_fillRenderer != null)
            {
                _fillRenderer.SetSpriteSize(sizePx);
                _fillRenderer.Modulate = _fillColor;
            }

            if (_fill != null)
            {
                _fill.Scale = Vector2.One;
                _fill.Position = Vector2.Zero;
            }
        }

        private void UpdateBar(float _)
        {
            if (_fill == null || _health == null)
                return;

            float normalized = Mathf.Clamp(_health.NormalizedHealth, 0f, 1f);
            _fill.Scale = new Vector2(normalized, 1f);
            _fill.Position = new Vector2(-_baseFillWidth * (1f - normalized) * 0.5f, 0f);
            if (_fillRenderer != null)
                _fillRenderer.Modulate = normalized <= 0.3f ? _fillColor.Lerp(LowHealthTint, 0.35f) : _fillColor;
        }

    }
}
