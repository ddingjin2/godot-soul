using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Plays a folder of pixel frames on the actor's existing <see cref="Sprite2D"/>: one clip per
    /// state, driven by <see cref="SetState"/>. Nothing here reads gameplay - see
    /// <see cref="ActorAnimationDriver"/> for that.
    /// </summary>
    /// <remarks>
    /// It writes only <see cref="Sprite2D.Texture"/> (and the scale once, at bind time), so everything
    /// already living on that sprite keeps working: the actors set <c>FlipH</c> themselves for facing,
    /// <c>CombatFeedback</c> owns the hit flash through <c>Modulate</c>, and the body, its collider and
    /// its hitboxes are never touched.
    /// <para>
    /// <see cref="TryAttach"/> returns null when the actor has no frames, which is the normal answer
    /// while <c>Resources/PixelActors</c> is unauthored. That refusal is what keeps the vector body the
    /// default and makes the pixel switch per-actor rather than all-or-nothing.
    /// </para>
    /// <para>
    /// Godot's <c>AnimatedSprite2D</c> was deliberately not used. This class already owns its own frame
    /// timing, its own one-shot/loop rule and its own ragged per-actor vocabulary, and rebuilding all
    /// three as <c>SpriteFrames</c> resources would be a redesign, not a port. Swapping a texture on a
    /// timer is exactly what the Unity code did.
    /// </para>
    /// </remarks>
    public partial class SpriteFrameAnimator : Node
    {
        /// <summary>How far the sprite's drawn height may sit from the collider's before it is worth saying so.</summary>
        private const float HeightTolerance = 0.10f;

        private Sprite2D _sprite;
        private PixelActorAnim.ClipSet _set;
        private PixelActorAnim.Clip _clip;
        private string _state = "";
        private float _frameTime;
        private bool _finished;

        /// <summary>The state now playing, or an empty string before the first <see cref="SetState"/>.</summary>
        public string CurrentState => _state;

        /// <summary>True while a non-looping clip still has frames left to show.</summary>
        public bool IsPlayingOneShot => _clip != null && !_clip.Loop && !_finished;

        /// <summary>
        /// Adds the animator under <paramref name="actor"/>, or returns null when there is nothing to
        /// play. Unity took the actor's <c>GameObject</c>; here the animator becomes a child node of the
        /// actor root, which is how a Unity component translates.
        /// </summary>
        public static SpriteFrameAnimator TryAttach(Node actor, string actorKey)
        {
            if (actor == null)
                return null;

            SpriteFrameAnimator existing = actor.GetComponent<SpriteFrameAnimator>();
            if (existing != null)
                return existing;

            Sprite2D sprite = actor.GetComponent<Sprite2D>();
            if (sprite == null)
                return null;

            PixelActorAnim.ClipSet set = PixelActorAnim.Load(actorKey);
            if (!set.HasFrames)
                return null;

            var animator = new SpriteFrameAnimator { Name = nameof(SpriteFrameAnimator) };
            actor.AddChild(animator);
            animator.Bind(sprite, set, actorKey);
            return animator;
        }

        /// <summary>
        /// Plays the first of the named states this actor actually has, falling back to idle. A state
        /// already playing is left alone, so calling this every frame does not restart a clip.
        /// </summary>
        /// <remarks>
        /// The candidates are separate parameters rather than a <c>params</c> array because this is
        /// called once per actor per frame, and an array per call is garbage per actor per frame.
        /// Vocabulary is per actor and deliberately ragged - the MeleeGrunt has no attack, the
        /// RangedCaster has no run - so the caller names what it wants in order of preference.
        /// </remarks>
        public void SetState(string first, string second = null, string third = null)
        {
            if (_set == null)
                return;

            string chosen = Resolve(first) ?? Resolve(second) ?? Resolve(third) ?? Resolve(PixelActorAnim.IdleState);
            if (chosen == null || chosen == _state)
                return;

            _state = chosen;
            _clip = _set.Clips[chosen];
            _frameTime = 0f;
            _finished = false;

            if (_clip.Frames.Length > 0)
                _sprite.Texture = _clip.Frames[0];
        }

        /// <summary>
        /// Starts the current clip over. A second swing while the first is still playing is a second
        /// swing, and without this it would read as one long one.
        /// </summary>
        public void Replay()
        {
            if (_clip == null || _clip.Frames.Length == 0)
                return;

            _frameTime = 0f;
            _finished = false;
            _sprite.Texture = _clip.Frames[0];
        }

        private string Resolve(string state)
        {
            return !string.IsNullOrEmpty(state) && _set.Clips.ContainsKey(state) ? state : null;
        }

        private void Bind(Sprite2D sprite, PixelActorAnim.ClipSet set, string actorKey)
        {
            _sprite = sprite;
            _set = set;

            // Unity flipped the renderer to Simple draw mode here, which drew the sprite at its own
            // authored pixels-per-unit and made every SpriteRenderer.size written at spawn inert. Godot
            // imports a PNG at 1:1 pixels and has no such mode, so the manifest's ppu has to be applied
            // as a scale instead: a 34px frame at ppu 34 is one Unity metre, which is World.Ppu pixels.
            // Taking the scale over is the same handover - the authored *VisualSize numbers go inert.
            float scale = _set.PixelsPerUnit > 0f ? World.Ppu / _set.PixelsPerUnit : 1f;
            _sprite.Scale = new Vector2(scale, scale);

            WarnIfSizeIsOff(actorKey);
            SetState(PixelActorAnim.IdleState);
        }

        /// <summary>
        /// Says so when the imported frames do not stand as tall as the actor's collider. The rule is
        /// <c>ppu = frame height in pixels / collider height in units</c>; anything inside ten percent
        /// of that is fine, and the log names the ppu that would have been right.
        /// </summary>
        /// <remarks>
        /// Reads the collider, never writes it. Sprite size is a rendering matter here - the physics
        /// body stays exactly as it was built. Heights are compared in Godot pixels rather than in Unity
        /// metres, which changes nothing: both sides scale by the same World.Ppu, so the ratio and the
        /// suggested ppu come out identical.
        /// </remarks>
        private void WarnIfSizeIsOff(string actorKey)
        {
            Texture2D frame = FirstFrame();
            var body = this.GetComponentInParent<Node2D>();
            if (frame == null || body == null)
                return;

            // Zero fallback extents on purpose: a body with no shape at all should say nothing rather
            // than warn against a made-up height.
            float colliderHeight = body.BodyBounds(Vector2.Zero).Size.Y;
            float spriteHeight = frame.GetHeight() * _sprite.Scale.Y;
            if (colliderHeight <= 0f || spriteHeight <= 0f)
                return;

            float ratio = spriteHeight / colliderHeight;
            if (Mathf.Abs(ratio - 1f) <= HeightTolerance)
                return;

            float colliderUnits = World.ToUnits(colliderHeight);
            GD.PushWarning(
                $"SpriteFrameAnimator: '{actorKey}' frames stand {World.ToUnits(spriteHeight):0.###}u tall against a " +
                $"{colliderUnits:0.###}u collider ({ratio:P0} of it; 90-110% is the band). " +
                $"anim.json says ppu {_set.PixelsPerUnit:0.#}; this {frame.GetHeight():0}px frame wants " +
                $"ppu {frame.GetHeight() / colliderUnits:0.#}.");
        }

        private Texture2D FirstFrame()
        {
            foreach (PixelActorAnim.Clip clip in _set.Clips.Values)
            {
                if (clip.Frames != null && clip.Frames.Length > 0)
                    return clip.Frames[0];
            }

            return null;
        }

        public override void _Process(double delta)
        {
            if (_clip == null || !GodotObject.IsInstanceValid(_sprite) || _clip.Frames.Length == 0)
                return;

            _frameTime += (float)delta * _clip.Fps;
            int index = (int)_frameTime;

            if (index >= _clip.Frames.Length)
            {
                if (_clip.Loop)
                {
                    _frameTime %= _clip.Frames.Length;
                    index = (int)_frameTime;
                }
                else
                {
                    // A one-shot holds its last frame until the driver moves it on.
                    index = _clip.Frames.Length - 1;
                    _finished = true;
                }
            }

            _sprite.Texture = _clip.Frames[index];
        }
    }
}
