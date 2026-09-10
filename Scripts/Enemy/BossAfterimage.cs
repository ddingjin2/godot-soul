using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// A copy of the boss that does nothing. It stands where it was put, fades, and is gone.
    ///
    /// Chapter six is built on it ([[RainbowChapterBossDesign]] 198): the Apostate will not act itself,
    /// and its afterimages are the work it refuses to do. The lesson is to track the source rather than
    /// the spectacle, which only works if the spectacle is genuinely harmless - so this carries no
    /// collider, no health and no damage of any kind. Everything that can hurt the player still comes
    /// out of the one real body.
    /// </summary>
    /// <remarks>
    /// Deliberately not a node that copies the boss every frame. A copy that tracked the original would
    /// read as a second boss, and the fight would become "which of these is attacking" rather than
    /// "which of these is real" - the fake active is the point, and a still image that appeared on the
    /// telegraph is the fake active.
    ///
    /// In Unity this was a behaviour that found or added a SpriteRenderer. Here it simply is the
    /// <see cref="Sprite2D"/>: there is nothing to find, and the fade is the node's own
    /// <see cref="CanvasItem.Modulate"/>.
    /// </remarks>
    public sealed partial class BossAfterimage : Sprite2D
    {
        /// <summary>The one place a BossAfterimage is described. Instanced by RainbowChapterBossBehaviour.</summary>
        public const string ScenePath = "res://Scenes/Effects/BossAfterimage.tscn";

        private float _remaining;
        private float _lifetime;
        private Color _startColor = Colors.White;

        /// <summary>Seconds of fade left. Zero on an unconfigured copy, which never ticks.</summary>
        public float Remaining => _remaining;

        public void Configure(Texture2D sprite, Color color, Vector2 size, int sortingOrder, float lifetime)
        {
            if (sprite != null)
            {
                Texture = sprite;
            }

            // Unity drew this with SpriteDrawMode.Sliced so an authored body size could be stretched over
            // whatever sprite was handed in. Godot's Sprite2D has no size, so the same thing is a scale.
            this.SetSpriteSize(size);
            ZIndex = sortingOrder;

            // Dimmer than the body from the first frame. A copy the player has to squint at is a copy
            // they can rule out, which is the difference between misdirection and a guessing game.
            _startColor = new Color(color.R, color.G, color.B, 0.45f);
            Modulate = _startColor;

            _lifetime = Mathf.Max(0.01f, lifetime);
            _remaining = _lifetime;
        }

        public override void _Process(double delta)
        {
            if (_remaining <= 0f)
            {
                return;
            }

            _remaining -= (float)delta;

            if (_remaining <= 0f)
            {
                QueueFree();
                return;
            }

            float fade = _remaining / _lifetime;
            Modulate = new Color(_startColor.R, _startColor.G, _startColor.B, _startColor.A * fade);
        }
    }
}
