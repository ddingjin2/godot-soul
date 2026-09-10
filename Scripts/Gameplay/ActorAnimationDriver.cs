using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Turns the state an actor is already in into a clip name for <see cref="SpriteFrameAnimator"/>.
    /// </summary>
    /// <remarks>
    /// It only reads signals the combat code already published - no new API was added to Player, Enemy
    /// or Combat for this, and none should be. Where an archetype exposes a bool it is polled; where it
    /// only raises an event, the event latches a one-shot clip and the clip's own length ends it.
    /// <para>
    /// Unity ran this in LateUpdate so it read the state the gameplay Update had just written. Godot has
    /// no late phase, so the same ordering comes from a high <c>ProcessPriority</c>: nodes tick in
    /// ascending priority, and 100 puts this behind every actor in the scene.
    /// </para>
    /// </remarks>
    public partial class ActorAnimationDriver : Node
    {
        /// <summary>Horizontal speed above which an actor is running rather than standing. Unity's 0.15 units/s in pixels.</summary>
        private static readonly float MoveEpsilon = World.U(0.15f);

        /// <summary>The vector-era blade, authored on the player and on nothing else.</summary>
        private const string VectorSwordName = "ReadableSword";

        private SpriteFrameAnimator _animator;

        private PlayerController2D _player;
        private EnemyStateMachine _enemy;
        private LeapingAttacker _leaper;
        private RainbowChapterBossBehaviour _chapterBoss;

        private bool _attackLatch;

        /// <summary>
        /// Gives <paramref name="actor"/> pixel frames if it has any, and the vector-era idle bob if it
        /// does not. One call covers the whole decision, so both spawners ask the same question.
        /// </summary>
        /// <remarks>Unity took the actor's <c>GameObject</c>; here it is the actor's root node.</remarks>
        public static void AttachTo(Node actor, string actorKey)
        {
            if (actor == null)
                return;

            SpriteFrameAnimator animator = SpriteFrameAnimator.TryAttach(actor, actorKey);
            if (animator == null)
            {
                // No frames for this actor yet: the greybox body and its breathing stay exactly as they
                // were. This is the whole of the gradual switch.
                ActorIdleBob.AttachTo(actor);
                return;
            }

            ClearGreyboxTint(actor);
            HideVectorSword(actor);

            ActorAnimationDriver driver = actor.GetComponent<ActorAnimationDriver>();
            if (driver == null)
            {
                driver = new ActorAnimationDriver { Name = nameof(ActorAnimationDriver) };
                actor.AddChild(driver);
            }

            driver.Bind(animator);
        }

        /// <summary>
        /// Pixel art carries its own colour, so the archetype tint that identified a greybox body has to
        /// come off - including the copy <c>CombatFeedback</c> keeps, or the first hit flash would
        /// restore it.
        /// </summary>
        /// <remarks>
        /// Three copies of that tint have to be dealt with, not one. The sprite is what is on screen
        /// now; <c>CombatFeedback</c> keeps a copy it restores after a hit flash; and the two bosses keep
        /// their own resting colour and repaint it when a telegraph ends. Miss any of them and the
        /// greybox colour comes back on the first hit or the first attack.
        /// <para>
        /// The telegraph tints themselves stay - they are the warning, and a warning reads no better on
        /// pixel art than on a box.
        /// </para>
        /// </remarks>
        private static void ClearGreyboxTint(Node actor)
        {
            // Unity's SpriteRenderer.color is Godot's CanvasItem.Modulate.
            Sprite2D sprite = actor.GetComponent<Sprite2D>();
            if (sprite != null)
                sprite.Modulate = Colors.White;

            CombatFeedback feedback = actor.GetComponent<CombatFeedback>();
            feedback?.SetOriginalColor(Colors.White);

            WrathMiniBoss wrath = actor.GetComponent<WrathMiniBoss>();
            wrath?.SetRestingColor(Colors.White);

            RainbowChapterBossBehaviour chapterBoss = actor.GetComponent<RainbowChapterBossBehaviour>();
            chapterBoss?.SetRestingColor(Colors.White);
        }

        /// <summary>
        /// Switches off the greybox sword. A pixel frame draws the weapon into the body, so the vector
        /// blade would hang over the top of it.
        /// </summary>
        /// <remarks>
        /// The drawn sprites only, never the node that carries them: <c>PlayerAttackAnimator2D</c> lives
        /// on the sword node and <c>GameplayPlayerSpawner</c> finds it by name, so hiding the node itself
        /// would take its <c>AnimationPlayer</c> off screen along with the method track that opens and
        /// closes the hitbox. Nothing else under an actor is touched - the health bar, the role marker
        /// and the danger readouts are how a fight stays legible, and they are not vector-era leftovers.
        /// The fallback path never calls this: without frames the sword is still the only thing that
        /// shows an attack.
        /// </remarks>
        private static void HideVectorSword(Node actor)
        {
            foreach (Node node in actor.FindComponentsInChildren<Node>())
            {
                if (node.Name != VectorSwordName)
                    continue;

                foreach (Sprite2D drawn in node.FindComponentsInChildren<Sprite2D>())
                    drawn.Visible = false;
            }
        }

        private void Bind(SpriteFrameAnimator animator)
        {
            _animator = animator;

            // Unity read the actor's Rigidbody2D for speed; the enemy *is* its CharacterBody2D here and
            // the player reports Velocity through its controller, so there is no separate body to hold.
            _player = this.GetComponentInParent<PlayerController2D>();
            if (_player != null)
            {
                // IsAttacking stays true across a combo, so the bool alone cannot tell a second swing
                // from a long first one. The event can.
                _player.OnAttackPerformed += LatchAttack;
                return;
            }

            _enemy = this.GetComponentInParent<EnemyStateMachine>();
            _leaper = _enemy as LeapingAttacker;
            _chapterBoss = _enemy as RainbowChapterBossBehaviour;

            // Only the chapter boss says out loud that an attack is running. The rest announce the start
            // and nothing else, so the start is what we listen for.
            switch (_enemy)
            {
                case MeleeGrunt grunt:
                    grunt.OnAttackStart += LatchAttack;
                    break;
                case LeapingAttacker leaper:
                    leaper.OnLeapStart += LatchAttack;
                    break;
                case RangedCaster caster:
                    caster.OnCastStart += LatchAttack;
                    break;
                case WrathMiniBoss wrath:
                    wrath.OnAttackStart += LatchAttack;
                    break;
            }
        }

        public override void _Ready()
        {
            // Unity's LateUpdate. Godot ticks _Process in ascending ProcessPriority, so anything above
            // the default 0 runs after the gameplay nodes have written the state this reads.
            ProcessPriority = 100;
        }

        public override void _ExitTree()
        {
            // Unity's UnityEvent listeners died with the component; a C# event holds a strong reference
            // to this node until it is unsubscribed, so every += above needs its -=.
            if (_player != null)
                _player.OnAttackPerformed -= LatchAttack;

            switch (_enemy)
            {
                case MeleeGrunt grunt:
                    grunt.OnAttackStart -= LatchAttack;
                    break;
                case LeapingAttacker leaper:
                    leaper.OnLeapStart -= LatchAttack;
                    break;
                case RangedCaster caster:
                    caster.OnCastStart -= LatchAttack;
                    break;
                case WrathMiniBoss wrath:
                    wrath.OnAttackStart -= LatchAttack;
                    break;
            }
        }

        /// <summary>Public because it is an event listener, not because anything should call it.</summary>
        public void LatchAttack()
        {
            _attackLatch = true;
        }

        public override void _Process(double delta)
        {
            if (_animator == null)
                return;

            if (_player != null)
                DrivePlayer();
            else if (_enemy != null)
                DriveEnemy();
        }

        /// <summary>
        /// Restarts the clip when a fresh attack was announced while one was already playing.
        /// <see cref="SpriteFrameAnimator.SetState"/> deliberately ignores a state it is already in.
        /// </summary>
        private void ConsumeAttackLatch()
        {
            if (!_attackLatch)
                return;

            _attackLatch = false;
            _animator.Replay();
        }

        private void DrivePlayer()
        {
            if (_player.CurrentState == PlayerState.Dead)
            {
                _animator.SetState("death", "hurt", "idle");
                return;
            }

            if (_player.IsStaggered)
            {
                _animator.SetState("hurt", "idle");
                return;
            }

            if (_player.IsAttacking || _player.IsHeavyAttacking)
            {
                _animator.SetState("attack", "run", "idle");
                ConsumeAttackLatch();
                return;
            }

            if (_player.IsDodging)
            {
                _animator.SetState("dodge", "run", "idle");
                return;
            }

            if (!_player.IsGrounded)
            {
                _animator.SetState("jump", "run", "idle");
                return;
            }

            _animator.SetState(Mathf.Abs(_player.Velocity.X) > MoveEpsilon ? "run" : "idle", "idle");
        }

        private void DriveEnemy()
        {
            // Death and the stagger outrank a running clip: an enemy that dies mid-swing should stop
            // swinging.
            EnemyState state = _enemy.CurrentState;
            if (state == EnemyState.Dead)
            {
                _animator.SetState("death", "hurt", "idle");
                return;
            }

            if (state == EnemyState.Stunned)
            {
                _animator.SetState("hurt", "idle");
                return;
            }

            if (_leaper != null && _leaper.IsLeaping)
            {
                _animator.SetState("jump", "attack", "run");
                ConsumeAttackLatch();
                return;
            }

            if (_chapterBoss != null && _chapterBoss.IsAttackRunning)
            {
                _animator.SetState("attack", "idle");
                ConsumeAttackLatch();
                return;
            }

            if (_attackLatch)
            {
                _animator.SetState("attack", "idle");
                ConsumeAttackLatch();
                return;
            }

            // Let a one-shot finish before movement claims the body back.
            if (_animator.IsPlayingOneShot)
                return;

            float speed = Mathf.Abs(_enemy.Velocity.X);
            _animator.SetState(speed > MoveEpsilon ? "run" : "idle", "idle");
        }
    }
}
