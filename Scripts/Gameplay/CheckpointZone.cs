using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// A bonfire. Stand inside the trigger, press Interact, and the run is written to the save slot,
    /// the respawn point moves here, health and stamina come back full and every enemy is put back.
    /// Humanity is deliberately not restored: the setting treats it as spent for good, so a rest buys
    /// safety at a price that never refunds (see Docs/WorldSetting.md).
    /// The component is inert until <see cref="Initialize"/> wires it, so a scene holding no zones keeps
    /// the single spawn point behaviour it has today.
    /// </summary>
    /// <remarks>
    /// Unity's trigger <c>CircleCollider2D</c> is an <see cref="Area2D"/> here, sitting on
    /// <c>World.Layer.Trigger</c> and watching <c>World.Layer.Player</c>. Godot's
    /// <c>BodyEntered</c> only ever reports solid bodies, so Unity's "ignore trigger colliders" filter
    /// is now the engine's job rather than this file's.
    /// </remarks>
    public sealed partial class CheckpointZone : Area2D
    {
        private const string MarkerObjectName = "CheckpointZoneMarker";

        /// <summary>Shared with <see cref="GateTravelZone"/>, which tints the same disc differently.</summary>
        private const string MarkerScenePath = "res://Scenes/World/MarkerDisc.tscn";

        /// <summary>Where the player reappears after dying. Falls back to this node itself.</summary>
        [Export] private Node2D respawnPoint;

        /// <summary>
        /// This bonfire's place in the chapter's checkpoint list. Written to the save on a rest, and
        /// read back to decide where a Continue resumes.
        /// </summary>
        [Export] private int checkpointIndex;

        /// <summary>Which of the chapter's checkpoints this is. Zero is where the chapter starts.</summary>
        public int CheckpointIndex => checkpointIndex;

        /// <summary>
        /// Set by whoever places the zone. Separate from the exported field so an arena built from
        /// layout data can number its bonfires without a scene per index.
        /// </summary>
        public void SetCheckpointIndex(int index)
        {
            checkpointIndex = Mathf.Max(0, index);
        }

        /// <summary>
        /// Raised after a rest lands. Left here as the seam for a HUD confirmation, which belongs to `ui`.
        /// </summary>
        public event Action OnActivated;

        public bool PlayerInside => _playerInside;

        private GameplayPlayerContext _player;
        private GameplayEnemyRespawner _respawner;
        private PlayerInputReceiver _input;
        private bool _playerInside;

        /// <summary>
        /// Wires every zone in the scene once the player and the enemy respawner exist. Finding nothing
        /// is the normal case until zones are placed, and costs one tree walk.
        /// </summary>
        public static void InitializeAll(GameplayPlayerContext player, GameplayEnemyRespawner respawner)
        {
            foreach (CheckpointZone zone in SceneQuery.FindAll<CheckpointZone>())
                zone.Initialize(player, respawner);
        }

        /// <summary>
        /// Interact is taken off <see cref="PlayerInputReceiver"/> rather than read from
        /// <see cref="GameplayInput"/> here, because a zero timescale does not stop _Process: reading the
        /// key directly would let the player rest through the pause menu and the victory panel, both of
        /// which stop play by disabling that receiver.
        /// </summary>
        public void Initialize(GameplayPlayerContext player, GameplayEnemyRespawner respawner)
        {
            Unsubscribe();

            _player = player;
            _respawner = respawner;
            _input = player.GameObject?.GetComponent<PlayerInputReceiver>();

            EnsureTrigger();
            EnsureMarker();

            if (_input != null)
                _input.OnInteract += TryActivate;
        }

        /// <summary>
        /// Binds what <c>Scenes/World/Checkpoint.tscn</c> authors but must not own: the reach from
        /// <c>WorldTuning.json</c> and the marker colour from <c>Readability.json</c>. Both are
        /// designer data, so the scene carries only the shipped values and these overwrite them.
        /// </summary>
        /// <remarks>
        /// Called by the arena builder while the instance is still detached, and that timing is the
        /// whole reason this is public rather than folded into <see cref="_Ready"/>:
        /// <see cref="GameplayTelegraphPulse"/> caches the colour it finds when it readies and writes
        /// that back every frame after, so a tint applied once the marker is in the tree never shows.
        /// </remarks>
        public void BindAuthoredTriggerAndMarker()
        {
            EnsureTrigger();
            EnsureMarker();
        }

        public override void _Ready()
        {
            EnsureTrigger();
            BodyEntered += OnBodyEntered;
            BodyExited += OnBodyExited;
        }

        public override void _ExitTree()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_input != null)
                _input.OnInteract -= TryActivate;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (IsPlayerBody(body))
                _playerInside = true;
        }

        private void OnBodyExited(Node2D body)
        {
            if (IsPlayerBody(body))
                _playerInside = false;
        }

        /// <summary>
        /// Only the player's solid body counts. In Unity the attack hitbox was a trigger collider on the
        /// same Rigidbody2D, so a swing at the zone's edge read as "inside" while the body never entered
        /// - the flag has to track feet, not reach. Here the hitbox is an <see cref="Area2D"/> and
        /// <c>BodyEntered</c> never reports it, so the group test is all that is left of the filter.
        /// </summary>
        private static bool IsPlayerBody(Node body)
        {
            return body != null && body.IsInGroup(World.Group.Player);
        }

        public void TryActivate()
        {
            if (!_playerInside)
                return;

            // Resting out of the spirit state would hand back full health in the middle of the death
            // sequence, and the respawn that follows would overwrite it anyway.
            if (_player.DeathController != null && _player.DeathController.IsInSpiritState)
                return;

            Activate();
        }

        public void Activate()
        {
            // An unwired zone has no player context (GameplayPlayerContext is a struct, so check its
            // payload). Writing a save from that state captures an empty run - souls 0, everything
            // default - straight over the real slot.
            if (_player.GameObject == null)
            {
                GD.PushWarning($"CheckpointZone on {Name} activated before Initialize; ignoring.");
                return;
            }

            Node2D respawn = respawnPoint ?? this;

            if (_player.DeathController != null)
                _player.DeathController.SetCheckpoint(respawn);

            if (_player.Health != null)
                _player.Health.SetHealth(_player.Health.MaxHealth);

            if (_player.Stamina != null)
                _player.Stamina.SetStamina(_player.Stamina.MaxStamina);

            if (_player.Controller != null)
                _player.Controller.RefillHealCharges();

            // Humanity is skipped on purpose. Do not "fix" this into a full restore.

            // Captured after the refill, so the slot holds the rested run rather than the one walked in
            // with. The index is stamped on afterwards because Capture cannot know which bonfire it was
            // called from - a rest is the only thing that moves it, and this is the rest.
            GameSaveData saved = GameplaySaveBridge.Capture(_player);
            saved.checkpointIndex = checkpointIndex;
            GameSave.Write(saved);

            // Found here rather than held from Initialize: a rest is rare enough that the lookup costs
            // nothing, and zones placed in a scene with no HUD then need no special case.
            SceneQuery.FindFirst<MyGame.UI.GameplayHud>()?.ShowCheckpointSaved();

            // The same routine death uses. Enemies coming back is the price of the rest, and a second
            // return path would drift away from the one the respawn already proved.
            if (_respawner != null)
                _respawner.RespawnEnemies();

            GD.Print($"CheckpointZone: rested at {Name}.");
            OnActivated?.Invoke();
        }

        /// <summary>
        /// The designer-owned reach in Godot pixels. There is no constant behind it any more: a build
        /// with no <c>WorldTuning.json</c> gets a zone of no reach and an error saying why, rather than
        /// a bonfire that lights up at a distance nobody authored (PLAN_CLOSEOUT A4, decision D1).
        /// Reads through the cached catalog, so the cost is one dictionary hit after the first zone in a
        /// scene.
        /// </summary>
        /// <remarks>
        /// Already in Godot pixels: <c>WorldTuningData</c> scales its spatial fields when the JSON
        /// loads, so nothing is scaled here.
        /// </remarks>
        private static float AuthoredRadius()
        {
            WorldTuningData world = GameplayTuningCatalog.Load()?.WorldTuning;
            if (world != null)
                return world.checkpointZoneRadius;

            GD.PushError("CheckpointZone: Design/WorldTuning.json is missing; the zone has no reach.");
            return 0f;
        }

        /// <summary>
        /// Binds the designer's reach onto the trigger <c>Scenes/World/Checkpoint.tscn</c> authors. The
        /// radius is <c>WorldTuning.json</c>'s and is rewritten on every bind; the scene carries an
        /// empty <c>CircleShape2D</c> so the number has exactly one home (K5b item 3).
        /// Unity had to warn about an authored *solid* collider it must not convert; an
        /// <see cref="Area2D"/> cannot be solid, so that branch has nothing left to guard and is gone.
        /// </summary>
        private void EnsureTrigger()
        {
            CollisionLayer = World.Layer.Trigger;
            CollisionMask = World.Layer.Player;
            SetDeferred(Area2D.PropertyName.Monitoring, true);

            CollisionShape2D trigger = this.GetComponent<CollisionShape2D>();
            if (trigger == null)
            {
                GD.PushError($"CheckpointZone: '{Name}' has no CollisionShape2D; Scenes/World/Checkpoint.tscn authors one named 'Trigger'. This bonfire can never be rested at.");
                return;
            }

            // The scene marks its CircleShape2D resource_local_to_scene, so this is this bonfire's reach
            // and not every bonfire's.
            if (trigger.Shape is CircleShape2D circle)
                circle.Radius = AuthoredRadius();
        }

        /// <summary>
        /// Greybox marker: <c>Scenes/World/MarkerDisc.tscn</c>, the same scene
        /// <see cref="GateTravelZone"/> instances. The pulse owns the transform and the disc hangs
        /// under it - that is how Unity's "two components on one marker GameObject" comes apart, and
        /// the scene authors it. Only the marker's size (this zone's own trigger radius) and the
        /// designer-owned colour are bound here.
        /// </summary>
        /// <remarks>
        /// Every checkpoint comes from <c>Scenes/World/Checkpoint.tscn</c>, which carries the marker, so
        /// this only binds. The disc is sized and tinted while the instance is still detached, and that
        /// order is load-bearing for the same reason it was in Unity: the pulse caches the colour it
        /// finds when it is readied and writes it back every frame after that.
        /// </remarks>
        private void EnsureMarker()
        {
            var marker = GetNodeOrNull<GameplayTelegraphPulse>(MarkerObjectName);
            if (marker == null)
            {
                GD.PushError($"CheckpointZone: '{Name}' has no '{MarkerObjectName}'; Scenes/World/Checkpoint.tscn authors a {MarkerScenePath} instance under that name. This bonfire is invisible.");
                return;
            }

            var shape = this.GetComponent<CollisionShape2D>()?.Shape as CircleShape2D;
            float radius = shape != null ? shape.Radius : AuthoredRadius();
            GameplayReadabilityDefaults readability = GameplayReadabilityDefaults.Create();

            Sprite2D disc = marker.GetNode<Sprite2D>("Disc");
            disc.SetSpriteSize(new Vector2(radius * 2f, radius * 2f));

            // Null when either readability file is missing, which the loader has already named. The disc
            // keeps the scene's own tint rather than being painted a colour from nowhere.
            if (readability == null)
                GD.PushError("CheckpointZone: Art/Readability.json or Design/ReadabilityLayout.json is missing; the bonfire marker keeps no designer colour.");
            else
                disc.Modulate = readability.CheckpointLabelColor;
        }
    }
}
