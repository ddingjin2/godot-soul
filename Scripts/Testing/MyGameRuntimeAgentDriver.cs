using System;
using Godot;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;
using UnityTestAgent.Agent;
using UnityTestAgent.Core;
using UnityTestAgent.Input;
using UnityTestAgent.Observation;

namespace MyGame.Testing
{
    /// <summary>
    /// Drop this node into a running gameplay scene and the game plays itself. Used by the runtime
    /// driver fixture and by hand when someone wants to watch a scenario rather than assert on it.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="Node"/>: it has no position of its own and only needs a physics tick. Unity's
    /// <c>[DisallowMultipleComponent]</c> has no Godot counterpart and is dropped - two of these under
    /// one parent is a scene-authoring mistake, not something the engine can refuse.
    ///
    /// <c>disablePlayerInput</c> is gone with the Unity input path. This driver now feeds the InputMap
    /// (see <see cref="MyGameAgentInputDriver"/>), so <see cref="PlayerInputReceiver"/> is the thing it
    /// talks *through*; switching the receiver off would silence the agent rather than free it.
    /// </remarks>
    public partial class MyGameRuntimeAgentDriver : Node
    {
        [Export] private bool autoStart = true;
        [Export] private bool retargetWhenTargetDies = true;
        [Export] private float targetSearchInterval = 0.5f;
        [Export] private bool logStatus = true;
        [Export] private float statusLogInterval = 1f;

        private PlayerController2D _player;
        private Node _targetEnemy;
        private IUnityTestAgent _agent;
        private IAgentInputDriver _inputDriver;
        private IGameStateProbe _probe;
        private float _nextTargetSearchTime;
        private float _nextStatusLogTime;

        public bool IsRunning { get; private set; }
        public int AppliedActionCount { get; private set; }
        public AgentDecision LastDecision { get; private set; }
        public AgentObservation LastObservation { get; private set; }
        public PlayerController2D ControlledPlayer => _player;
        public Node TargetEnemy => _targetEnemy;

        public override void _Ready()
        {
            if (autoStart)
                StartAgent();
        }

        public override void _PhysicsProcess(double delta)
        {
            if (!IsRunning)
                return;

            if (_agent == null || _inputDriver == null || _probe == null)
            {
                GD.Print("[UnityTestAgent] Runtime driver dependencies were missing; restarting agent driver.");
                IsRunning = false;
                StartAgent();
                if (!IsRunning)
                    return;
            }

            if (!GodotObject.IsInstanceValid(_player))
            {
                StopAgent();
                return;
            }

            if (retargetWhenTargetDies && GameClock.Time >= _nextTargetSearchTime && !IsLiveTarget(_targetEnemy))
            {
                _targetEnemy = FindNearestLiveEnemy(PlayerPosition());
                _probe = CreateProbe();
                _nextTargetSearchTime = GameClock.Time + Mathf.Max(0.05f, targetSearchInterval);
            }

            LastObservation = _probe.Capture();
            LastDecision = _agent.Decide(LastObservation);
            _inputDriver.Apply(LastDecision.Action);
            AppliedActionCount++;

            if (logStatus && GameClock.Time >= _nextStatusLogTime)
            {
                LogStatus();
                _nextStatusLogTime = GameClock.Time + Mathf.Max(0.1f, statusLogInterval);
            }
        }

        public bool StartAgent()
        {
            _player = FindPlayer();
            if (_player == null)
            {
                GD.PushWarning("[UnityTestAgent] Runtime driver could not find PlayerController2D.");
                return false;
            }

            // The agent drives the player through the InputMap, so a cutscene locking
            // PlayerInputReceiver *would* stop it - and it would keep the camera besides. Cut the
            // cutscenes out entirely while the agent has the controls.
            CutsceneDirector.SkipAll = true;

            _targetEnemy = FindNearestLiveEnemy(PlayerPosition());
            // SimpleCombatAgent's own attackRange default is Unity metres; the probe reports pixels.
            _agent = new SimpleCombatAgent(World.U(1.5f));
            _inputDriver = new MyGameAgentInputDriver(_player);
            _probe = CreateProbe();
            _nextTargetSearchTime = GameClock.Time + Mathf.Max(0.05f, targetSearchInterval);
            _nextStatusLogTime = GameClock.Time;
            IsRunning = true;

            GD.Print("[UnityTestAgent] Runtime driver started. Target=" + (_targetEnemy != null ? _targetEnemy.Name.ToString() : "<none>"));
            return true;
        }

        public void StopAgent()
        {
            IsRunning = false;
            CutsceneDirector.SkipAll = false;

            // Unity zeroed the move input on the controller. The keys are what is held now, so it is
            // the keys that have to come up - a walk left held into the next scene is a stuck keyboard.
            (_inputDriver as MyGameAgentInputDriver)?.ReleaseAll();

            GD.Print("[UnityTestAgent] Runtime driver stopped.");
        }

        /// <summary>Lets go of the keys if the scene is torn down without anyone calling <see cref="StopAgent"/>.</summary>
        public override void _ExitTree()
        {
            if (IsRunning)
                StopAgent();
        }

        private void LogStatus()
        {
            string playerText = LastObservation != null && LastObservation.Player != null
                ? "player=(" + LastObservation.Player.Position.X.ToString("F2") + "," + LastObservation.Player.Position.Y.ToString("F2") + ") hp=" + LastObservation.Player.HitPoints + " stamina=" + LastObservation.Player.Stamina.ToString("F0")
                : "player=<none>";

            string targetText = LastObservation != null && LastObservation.Enemies.Count > 0
                ? "target=" + LastObservation.Enemies[0].Id + " pos=(" + LastObservation.Enemies[0].Position.X.ToString("F2") + "," + LastObservation.Enemies[0].Position.Y.ToString("F2") + ") hp=" + LastObservation.Enemies[0].HitPoints + " alive=" + LastObservation.Enemies[0].IsAlive + " attacking=" + LastObservation.Enemies[0].IsAttacking
                : "target=<none>";

            GD.Print("[UnityTestAgent] Runtime status: actions=" + AppliedActionCount + " lastAction=" + LastDecision.Action.Type + " reason=" + LastDecision.Reason + " " + playerText + " " + targetText);
        }

        private IGameStateProbe CreateProbe()
        {
            var enemies = _targetEnemy != null ? new[] { _targetEnemy } : Array.Empty<Node>();
            return new MyGameStateProbe(_player, enemies, Array.Empty<Node>(), null, null, null);
        }

        private Vector2 PlayerPosition()
        {
            Node2D body = _player.GetComponentInParent<PlayerMotor2D>();
            return body != null ? body.GlobalPosition : _player.GlobalPosition;
        }

        /// <summary>
        /// Unity's <c>FindAnyObjectByType&lt;PlayerController2D&gt;()</c>. Godot has no type-wide search,
        /// so this walks down from the player group the spawner joins the actor to.
        /// </summary>
        private PlayerController2D FindPlayer()
        {
            foreach (Node node in GetTree().GetNodesInGroup(World.Group.Player))
            {
                PlayerController2D found = node.GetComponentInChildren<PlayerController2D>();
                if (found != null)
                    return found;
            }

            return GetTree().CurrentScene?.GetComponentInChildren<PlayerController2D>();
        }

        private Node FindNearestLiveEnemy(Vector2 position)
        {
            EnemyStateMachine nearest = null;
            var nearestDistance = float.MaxValue;

            foreach (Node node in GetTree().GetNodesInGroup(World.Group.Enemy))
            {
                if (node is not EnemyStateMachine enemy || !IsLiveTarget(enemy))
                    continue;

                float distance = Mathf.Abs(enemy.GlobalPosition.X - position.X);
                if (distance < nearestDistance)
                {
                    nearest = enemy;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private static bool IsLiveTarget(Node target)
        {
            if (!GodotObject.IsInstanceValid(target) || !target.IsInsideTree())
                return false;

            if (target is CanvasItem { Visible: false })
                return false;

            var health = target.GetComponentInChildren<MyGame.Combat.Health>();
            return health == null || !health.IsDead;
        }
    }
}
