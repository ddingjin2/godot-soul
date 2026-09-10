using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The slow breathing scale-and-fade that says "this is live" on a bonfire disc, a portal or a
    /// soul stain. Drives its own transform, so the sprite it tints sits under it and keeps whatever
    /// size the builder gave it.
    ///
    /// The four feel numbers are <c>ReadabilityLayout.json</c>'s (<c>markerPulse*</c>), read at
    /// <c>_Ready</c> through the same boundary every other readability number crosses. They used to
    /// be <c>[Export]</c> defaults that none of the five scenes carrying this node ever set - a knob
    /// in code wearing an inspector's clothes.
    /// </summary>
    public partial class GameplayTelegraphPulse : Node2D
    {
        private float pulseSpeed = 4f;
        private float pulseAmount = 0.12f;
        private float alphaMin = 0.16f;
        private float alphaMax = 0.34f;

        private Sprite2D _renderer;
        private Vector2 _baseScale;
        private Color _baseColor;

        public override void _Ready()
        {
            GameplayReadabilityDefaults readability = GameplayReadabilityDefaults.Create();
            pulseSpeed = readability.MarkerPulseSpeed;
            pulseAmount = readability.MarkerPulseAmount;
            alphaMin = readability.MarkerPulseAlphaMin;
            alphaMax = readability.MarkerPulseAlphaMax;

            // This node, or the sprite child the marker builders parent to it.
            _renderer = this.GetComponent<Sprite2D>();
            _baseScale = Scale;
            if (_renderer != null)
                _baseColor = _renderer.Modulate;
        }

        public override void _Process(double delta)
        {
            float t = (Mathf.Sin(GameClock.Time * pulseSpeed) + 1f) * 0.5f;
            Scale = _baseScale * (1f + t * pulseAmount);

            if (_renderer != null)
            {
                Color color = _baseColor;
                color.A = Mathf.Lerp(alphaMin, alphaMax, t);
                _renderer.Modulate = color;
            }
        }
    }
}
