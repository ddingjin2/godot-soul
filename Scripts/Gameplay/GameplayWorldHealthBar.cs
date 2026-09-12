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

        /// <summary>
        /// Bar size, offset from the actor and fill colour, all in Godot pixels with +Y down, so above
        /// the actor is negative. <see cref="Initialize"/> is the only writer and every spawner calls
        /// it with <c>GameplayReadabilityDefaults</c>'s numbers; zero until then, because a bar nobody
        /// initialised is a wiring fault rather than a bar with other numbers (PLAN_CLOSEOUT D1/K5b).
        /// <c>Scenes/World/WorldHealthBar.tscn</c> used to author the same three values a second time.
        /// </summary>
        private Vector2 _size;
        private Vector2 _offset;
        private Color _fillColor;

        /// <summary>
        /// The frame, and the bone the fill lifts toward at low health - INK_950 #06070A a0.92 and
        /// BONE_100 #C3BDB1 as shipped, but the numbers are the artist's, in
        /// <c>Resources/Art/Readability.json</c>, and <see cref="Build"/> is what reads them.
        /// Static because one palette serves every bar in the scene; written rather than readonly
        /// because the file is what writes them.
        /// </summary>
        private static Color FrameColor;
        private static Color LowHealthTint;

        // Read off GameplayReadabilityDefaults and the palette in Build, already pixels. Zero until
        // then: a missing design file is an error, not a bar with different numbers (PLAN_CLOSEOUT D1).
        private float _frameMarginPx;
        private float _lowHealthThreshold;
        private float _lowHealthTintBlend;

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

            // The designer's frame margin and low-health read, from ReadabilityLayout.json by way of the
            // same boundary the spawners use for the bar's size and offset; the frame and tint colours
            // off the artist's palette beside it. Create() is null when either file is missing, which is
            // a broken build rather than a bar with other numbers (PLAN_CLOSEOUT D1).
            GameplayReadabilityDefaults readability = GameplayReadabilityDefaults.Create();
            if (readability == null)
            {
                GD.PushError("GameplayWorldHealthBar: Art/Readability.json or Design/ReadabilityLayout.json is missing; the bar has no margin, threshold or frame colour.");
            }
            else
            {
                _frameMarginPx = readability.HealthBarFrameMargin;
                _lowHealthThreshold = readability.HealthBarLowHealthThreshold;
                _lowHealthTintBlend = readability.HealthBarLowHealthTintBlend;
                FrameColor = readability.HealthBarFrameColor;
                LowHealthTint = readability.HealthBarLowHealthTint;
            }

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
                _frameRenderer.SetSpriteSize(sizePx + new Vector2(_frameMarginPx, _frameMarginPx));

                // The scene used to author this same colour and no longer does (PLAN_CLOSEOUT B3): this
                // line ran over it on every bar anyway, so the copy in the .tscn was a second place to
                // edit the frame and never a second source. It is what the P0 suite reads back.
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
                _fillRenderer.Modulate = normalized <= _lowHealthThreshold ? _fillColor.Lerp(LowHealthTint, _lowHealthTintBlend) : _fillColor;
        }

    }
}
