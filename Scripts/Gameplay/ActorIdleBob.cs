using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Procedural idle breathing: a small sine pulse on the actor's sprite height, so a static
    /// one-frame sprite does not read as a still image. Purely cosmetic.
    /// </summary>
    /// <remarks>
    /// Unity pulsed <c>SpriteRenderer.size</c>, which only does anything in Sliced or Tiled draw mode,
    /// and it went out of its way never to touch the Transform - because a Unity actor carried its
    /// SpriteRenderer on the same GameObject as its Rigidbody2D and CapsuleCollider2D, so scaling the
    /// transform would have resized the collider four percent at a time, twice a second.
    /// <para>
    /// Godot has no draw mode and no renderer size: a <see cref="Sprite2D"/> is sized by its own
    /// <c>Scale</c>, which is what <c>EnemyShim.SetSpriteSize</c> writes. So the pulse moves
    /// <c>Scale.Y</c> instead - and the trap the Unity comment was defending against is simply gone,
    /// because the sprite is its own node and the collider is a sibling <c>CollisionShape2D</c>. This
    /// node touches nothing but that one sprite's scale; keep it that way.
    /// </para>
    /// <para>
    /// <see cref="amplitude"/> is a fraction of the rest height, not a distance - it needs no
    /// <c>World.U</c> - and the pulse is a symmetric sine about that rest pose, so Godot's flipped Y
    /// axis changes nothing here either. Both are worth saying out loud, because every other spatial
    /// number in this folder needed one conversion or the other.
    /// </para>
    /// </remarks>
    public partial class ActorIdleBob : Node
    {
        // WorldTuning.json's actorIdleBob*, read at _Ready. No copy of them here: without the file the
        // bob stops rather than breathing on numbers nobody authored (PLAN_CLOSEOUT D1). Were [Export]
        // defaults, which no scene could have set: the bob is attached from code, never authored.
        private float period;
        private float amplitude;

        private Sprite2D _sprite;
        private Vector2 _baseScale;
        private float _phase;
        private bool _ready;

        /// <summary>
        /// Adds the bob to the sprite on <paramref name="actor"/>, or refuses and returns null when
        /// there is nothing to bob. Wired at spawn rather than authored into a scene, because the whole
        /// arena is built from code.
        /// </summary>
        /// <remarks>
        /// Unity took the actor's <c>GameObject</c>; the Godot equivalent is the actor's root node, and
        /// the bob itself becomes a child of it - a Unity component is a child node here.
        /// </remarks>
        public static ActorIdleBob AttachTo(Node actor)
        {
            if (actor == null)
                return null;

            // Idempotent: the same seed code runs from more than one spawn path, and a second bob on the
            // same sprite would beat against the first.
            ActorIdleBob existing = actor.GetComponent<ActorIdleBob>();
            if (existing != null)
                return existing;

            Sprite2D sprite = actor.GetComponent<Sprite2D>();
            if (sprite == null || sprite.Texture == null)
            {
                GD.PushWarning($"ActorIdleBob: '{actor.Name}' has no sprite - nothing to bob.");
                return null;
            }

            var bob = new ActorIdleBob { Name = nameof(ActorIdleBob) };
            actor.AddChild(bob);
            return bob;
        }

        public override void _Ready()
        {
            WorldTuningData world = GameplayTuningCatalog.Load()?.WorldTuning;
            if (world == null)
            {
                GD.PushError("ActorIdleBob: Design/WorldTuning.json is missing; the idle breathing has no period or depth.");
                SetProcess(false);
                return;
            }

            period = world.actorIdleBobPeriod;
            amplitude = world.actorIdleBobAmplitude;

            _sprite = this.GetComponentInParent<Sprite2D>();
            if (_sprite == null || _sprite.Texture == null)
            {
                SetProcess(false);
                return;
            }

            // Rest pose must look exactly like it did before the bob was attached. The sizes written at
            // spawn - readability's *VisualSize, WrathMiniBoss's bossBodySize,
            // RainbowChapterBossBehaviour's BodySize - all land on this scale, so seeding from it keeps
            // them exactly as authored instead of overwriting three numbers with a fourth.
            _baseScale = _sprite.Scale;

            // Per-instance phase. Every actor breathing on the same beat reads as one machine rather
            // than as several bodies, which is worse than not breathing at all.
            _phase = GD.Randf() * Mathf.Pi * 2f;
            _ready = true;
        }

        public override void _ExitTree()
        {
            if (_ready && GodotObject.IsInstanceValid(_sprite))
                _sprite.Scale = _baseScale;
        }

        public override void _Process(double delta)
        {
            if (!_ready || period <= 0f || !GodotObject.IsInstanceValid(_sprite))
                return;

            float pulse = Mathf.Sin(_phase + GameClock.Time * (Mathf.Pi * 2f / period));
            _sprite.Scale = new Vector2(_baseScale.X, _baseScale.Y * (1f + amplitude * pulse));
        }
    }
}
