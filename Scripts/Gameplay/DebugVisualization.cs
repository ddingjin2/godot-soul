using Godot;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The designer's X-ray: detection and attack ranges, enemy state, boss phase, the checkpoint box
    /// and the spirit platform, drawn over the arena.
    /// </summary>
    /// <remarks>
    /// Unity drew all of this from <c>OnDrawGizmos</c>, which the editor called and a build never did.
    /// Godot has no gizmo system at runtime, so this is now a real <see cref="Node2D"/> that draws in
    /// <see cref="_Draw"/> and asks for a redraw every frame. Two consequences worth knowing:
    /// it draws in the *game* view rather than only in an editor scene view, and it is therefore off
    /// unless something turns it on (<see cref="Visible"/> is false until asked for). Nothing else about
    /// what is drawn changed - every gizmo call had a Godot counterpart
    /// (<c>DrawArc</c> for a wire sphere, <c>DrawRect</c> for a wire cube, <c>DrawCircle</c> for a
    /// filled one), so nothing was dropped.
    /// </remarks>
    public partial class DebugVisualization : Node2D
    {
        [Export] private bool showPlayerDebug = true;
        [Export] private bool showEnemyDebug = true;
        [Export] private bool showCheckpoint = true;
        [Export] private bool showSpiritPlatform = true;

        /// <summary>Line thickness for every wire shape, and how many segments an arc gets.</summary>
        private const float LineWidth = 2f;
        private const int ArcSegments = 32;

        private PlayerController2D _player;
        private Node2D _checkpoint;
        private Node2D _spiritPlatform;

        public void Initialize(PlayerController2D player, Node2D checkpoint, Node2D spiritPlatform)
        {
            _player = player;
            _checkpoint = checkpoint;
            _spiritPlatform = spiritPlatform;
        }

        public override void _Ready()
        {
            // Off by default: this used to be an editor-only gizmo pass and must not appear in a build
            // unless somebody asks for it. ZIndex over everything, since it is an overlay.
            Visible = false;
            ZIndex = 4096;
        }

        public override void _Process(double delta)
        {
            if (Visible)
                QueueRedraw();
        }

        public override void _Draw()
        {
            if (showPlayerDebug && _player != null && IsInstanceValid(_player))
                DrawPlayerDebug();

            if (showEnemyDebug)
                DrawEnemyDebug();

            if (showCheckpoint && _checkpoint != null && IsInstanceValid(_checkpoint))
                DrawCheckpoint();

            if (showSpiritPlatform && _spiritPlatform != null && IsInstanceValid(_spiritPlatform))
                DrawSpiritPlatform();
        }

        private void DrawPlayerDebug()
        {
            Node2D hitboxAnchor = _player.GetHitboxAnchor();
            if (hitboxAnchor != null)
                WireCircle(hitboxAnchor.GlobalPosition, World.U(0.4f), Colors.Cyan);

            if (_player.IsInParryWindow())
            {
                // The parry window is a duration, not a distance - Unity drew it as a radius anyway, to
                // make the window's length visible as a growing ring. Kept, including the units abuse.
                float parryWindow = _player.GetCurrentParryWindow();
                WireCircle(_player.GlobalPosition, World.U(parryWindow * 2f), new Color(0f, 1f, 0f, 0.3f));
            }
        }

        private void DrawEnemyDebug()
        {
            // Unity swept every MonoBehaviour in the scene and type-tested each one. The enemy group is
            // the same set and costs one lookup.
            foreach (Node node in GetTree().GetNodesInGroup(World.Group.Enemy))
            {
                if (node is MeleeGrunt meleeGrunt)
                {
                    DrawEnemyRanges(meleeGrunt.GlobalPosition, meleeGrunt.DetectionRange, meleeGrunt.AttackRange, Colors.Red);
                    DrawEnemyState(meleeGrunt.GlobalPosition, meleeGrunt.CurrentState);
                }
                else if (node is LeapingAttacker leaping)
                {
                    DrawEnemyRanges(leaping.GlobalPosition, leaping.DetectionRange, leaping.AttackRange, new Color(1f, 0.5f, 0f));
                    DrawEnemyState(leaping.GlobalPosition, leaping.CurrentState);
                }
                else if (node is RangedCaster caster)
                {
                    DrawEnemyRanges(caster.GlobalPosition, caster.DetectionRange, caster.AttackRange, new Color(0.5f, 0f, 1f));
                    DrawEnemyState(caster.GlobalPosition, caster.CurrentState);
                }
                else if (node is WrathMiniBoss boss)
                {
                    DrawEnemyRanges(boss.GlobalPosition, boss.DetectionRange, boss.AttackRange, new Color(1f, 0.2f, 0.2f));
                    DrawBossDebug(boss);
                }
                else if (node is RainbowChapterBossBehaviour chapterBoss)
                {
                    // No rage-mode ring: rage is Wrath's own beat, not something the shared loop has.
                    // What a chapter boss can show is its arrival, its phase and whichever authored
                    // attack is in the air.
                    DrawChapterBossDebug(chapterBoss);
                }
            }
        }

        private void DrawEnemyState(Vector2 position, EnemyState state)
        {
            Color color = state switch
            {
                EnemyState.Combat => Colors.Red,
                EnemyState.Investigate => Colors.Yellow,
                EnemyState.Recovery => Colors.Cyan,
                EnemyState.Stunned => Colors.Gray,
                EnemyState.Patrol => Colors.Green,
                _ => Colors.White
            };

            WireCircle(position + Above(2f), World.U(0.15f), color);
        }

        private void DrawChapterBossDebug(RainbowChapterBossBehaviour boss)
        {
            Vector2 pos = boss.GlobalPosition;
            float attackRange = boss.BossData != null ? boss.BossData.AttackRange : World.U(2f);

            DrawEnemyRanges(pos, boss.CurrentAttackProfile?.Range ?? attackRange, attackRange, new Color(1f, 0.2f, 0.2f));

            // Hollow while it is still being read, filled once it can actually hit - the same
            // distinction the player has to make, drawn where a designer can watch it.
            Color phaseColor = boss.IsPhaseTwo ? Colors.Red : Colors.Yellow;
            if (boss.IsAttackActive)
                DrawCircle(ToLocal(pos + Above(2.5f)), World.U(0.3f), phaseColor);
            else
                WireCircle(pos + Above(2.5f), World.U(0.3f), phaseColor);

            if (boss.IsInIntro)
                WireCircle(pos + Above(3f), World.U(0.18f), Colors.White);
        }

        private void DrawBossDebug(WrathMiniBoss boss)
        {
            Vector2 pos = boss.GlobalPosition;

            WireCircle(pos + Above(2.5f), World.U(0.3f), boss.IsInPhaseTwo ? Colors.Red : Colors.Yellow);

            if (boss.IsInRageMode)
            {
                Color rage = Colors.Red.Lerp(Colors.White, Mathf.PingPong(GameClock.Time * 3f, 1f));
                WireCircle(pos + Above(3f), World.U(0.25f), rage);
            }

            Color stateColor = boss.IsInVictoryState ? Colors.Gray : (boss.IsInIntro ? Colors.Black : Colors.White);
            WireBox(pos + Above(3.5f), new Vector2(0.4f, 0.2f), stateColor);
        }

        private void DrawEnemyRanges(Vector2 position, float detectionRange, float attackRange, Color color)
        {
            // The ranges arrive already in pixels - the enemy tuning classes scale them once, at load -
            // so they are not scaled again here.
            WireCircle(position, detectionRange, new Color(color.R, color.G, color.B, 0.2f));
            WireCircle(position, attackRange, color);
        }

        private void DrawCheckpoint()
        {
            WireBox(_checkpoint.GlobalPosition, new Vector2(1f, 2f), Colors.Green);
            WireBox(_checkpoint.GlobalPosition, new Vector2(3f, 4f), new Color(0f, 1f, 0f, 0.3f));
        }

        private void DrawSpiritPlatform()
        {
            if (!_spiritPlatform.Visible)
                return;

            WireBox(_spiritPlatform.GlobalPosition, new Vector2(2f, 0.3f), new Color(0.5f, 0.5f, 0.8f, 0.5f));
            WireBox(_spiritPlatform.GlobalPosition, new Vector2(4f, 3f), new Color(0.5f, 0.5f, 1f, 0.2f));
        }

        /// <summary>Unity's <c>Vector3.up * n</c>, in Godot space.</summary>
        private static Vector2 Above(float unityUnits) => World.V(new Vector2(0f, unityUnits));

        /// <summary>Gizmos.DrawWireSphere, at a global position with a radius already in pixels.</summary>
        private void WireCircle(Vector2 globalPosition, float radiusPx, Color color)
        {
            DrawArc(ToLocal(globalPosition), Mathf.Max(1f, radiusPx), 0f, Mathf.Tau, ArcSegments, color, LineWidth);
        }

        /// <summary>Gizmos.DrawWireCube, with the size authored in Unity metres.</summary>
        private void WireBox(Vector2 globalPosition, Vector2 unitySize, Color color)
        {
            Vector2 sizePx = new(World.U(unitySize.X), World.U(unitySize.Y));
            Vector2 topLeft = ToLocal(globalPosition) - sizePx * 0.5f;
            DrawRect(new Rect2(topLeft, sizePx), color, filled: false, width: LineWidth);
        }
    }
}
