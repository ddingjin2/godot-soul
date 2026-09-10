using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;
using MyGame.UI;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The script on the gameplay scene's root node. Everything the arena is - the camera, the ground,
    /// the player, the enemies, the HUD, the cutscene layer - is built here, because the Unity project
    /// built its scenes from code and this port keeps that.
    /// </summary>
    public partial class GameplayBootstrap : Node
    {
        public const string PlayerObjectName = "Player";
        public const string HudObjectName = "HUD";
        public const string HitStopManagerObjectName = "HitStopManager";
        public const string EnemyRespawnerObjectName = "EnemyRespawner";

        private const string EnterCutsceneKey = "GameplayEnter";

        // CutsceneDirection.md 4: bars are already at this height on the first rendered frame.
        private const float EnterLetterboxHeight = 64f;

        // The same section's rig beat: one unit of settle, landing with the fade. Read off the rig rather
        // than off GameplaySceneDefaults, so whatever FrameCombatRoom decided stays the resting position.
        private const float EnterSettleHeight = 1f;
        private const float EnterSettleDuration = 1.2f;

        private GameplayPlayerContext _player;
        private PlayerProgression _progression;

        public override void _Ready()
        {
            // Unity applied the graphics options from a [RuntimeInitializeOnLoadMethod] before any scene
            // loaded. Godot has no such hook, so the bootstrap is where they are applied - once, before
            // anything renders. Without this call the options screen writes settings nothing reads.
            GraphicsOptions.LoadAndApply();

            _player = ValidateScene();
            BindLevelUpToTheSlot();

#if DEBUG
            // Unity installed the debug jump from its own RuntimeInitializeOnLoadMethod; here the
            // bootstrap does it, and the node parents itself to the tree root so it survives the jump.
            GameplayDebugSceneJump.Install(this);
#endif
        }

        public override void _ExitTree()
        {
            // The event lives on the player, which dies with the scene, so this is belt and braces -
            // but a scene reloaded often enough is exactly where a listener nobody removes turns into a
            // second writer of the save.
            if (_progression != null)
                _progression.OnLevelsChanged -= WriteLevelsToSlot;
        }

        /// <summary>
        /// Makes a level land in the slot the moment it is bought. The rest that opens the level-up panel
        /// has already written the save by the time the panel exists - <c>CheckpointZone.Activate</c>
        /// writes and then raises <c>OnActivated</c> - so without this a purchase waits for the next rest,
        /// gate or boss kill, and a player who buys and quits on the spot pays the souls for nothing.
        /// </summary>
        /// <remarks>
        /// Here rather than on <see cref="CheckpointZone"/> because a chapter now has several of those and
        /// every one of them would subscribe, giving one purchase as many pref writes as the level has
        /// bonfires. The bootstrap is one per scene and already holds the player.
        /// </remarks>
        private void BindLevelUpToTheSlot()
        {
            if (_player.GameObject == null)
                return;

            // Progression is a plain C# object hanging off the controller in this port, not a node, so
            // it is reached through EnsureOn rather than through a component lookup.
            _progression = PlayerProgression.EnsureOn(_player.GameObject);
            if (_progression != null)
                _progression.OnLevelsChanged += WriteLevelsToSlot;
        }

        private void WriteLevelsToSlot()
        {
            // Restoring a slot raises this same event, and mid-restore the player is a mixture: the levels
            // are in, the health, humanity and souls are still the spawner's fresh-run numbers. Capturing
            // that would write a zero purse straight over the slot being loaded. Subscribing after
            // ValidateScene already keeps the load's own SetLevels away from this listener; the flag is
            // what keeps it true if those calls are ever reordered.
            if (GameplaySaveBridge.IsApplying)
                return;

            GameSave.Write(GameplaySaveBridge.Capture(_player));
        }

        private GameplayPlayerContext ValidateScene()
        {
            // Unity read a per-scene GameplaySceneDefaultsAsset out of Resources/Gameplay. That folder of
            // ScriptableObject YAML is not ported - the design JSON supersedes it - so the layout comes
            // from the scene's own SceneLayout_<Name>.json alone, which is what CreateForScene(name, null)
            // already meant.
            GameplaySceneDefaults scene = GameplaySceneDefaults.CreateForScene(GameplayBuildShim.ActiveSceneName, null);
            GameplayReadabilityDefaults readability = GameplayReadabilityDefaults.Create();

            Camera2D cam = GameplaySystemBootstrapper.EnsureCamera(readability.BackgroundColor);
            GameplaySystemBootstrapper.EnsureHitStopManager(HitStopManagerObjectName);

            // Before anything spawns: difficulty scales enemy health, and the enemies are built below.
            // Only from a slot the game is actually resuming - a New Game has already told the settings
            // which difficulty was picked, and the slot still holds the last run's.
            if (GameSave.LoadOnNextGameplayStart)
                DifficultySettings.Apply(GameSave.Read());

            GameplayEnvironment environment = GameplayEnvironmentBuilder.Build(scene, readability);
            GameplayPlayerContext player = GameplayPlayerSpawner.Spawn(scene, readability, environment.Checkpoint, environment.SpiritPlatform);
            GameplayEnemyContext enemies = GameplayEnemySpawner.Spawn(scene, readability, player);
            GameplayHud hud = GameplayHudSpawner.Spawn(player, enemies);

            var respawner = new GameplayEnemyRespawner { Name = EnemyRespawnerObjectName };
            AddChild(respawner);

            // No-op until zones are placed in the scene, which keeps the single spawn point as it is.
            CheckpointZone.InitializeAll(player, respawner);

            // Resting is what opens the level-up panel. Subscribed here rather than threaded through
            // InitializeAll because that signature is the one every checkpoint test calls, and the panel
            // belongs to the HUD anyway - the same split GateTravelZone makes two lines below.
            foreach (CheckpointZone zone in SceneQuery.FindAll<CheckpointZone>())
                zone.OnActivated += () => hud.SetLevelUpVisible(true, player.GameObject);

            // The gate portal, on the other hand, is built into every arena by the environment builder.
            // Wired after the HUD exists, because the panel it opens is the HUD's.
            GateTravelZone.InitializeAll(player, hud);

            RestoreSaveIfRequested(player);

            GameplaySystemBootstrapper.FrameCombatRoom(cam, scene);
            GameplaySystemBootstrapper.ConfigureCameraFollow(cam, player.Transform, scene);

            GameplayCutsceneTriggers cutscenes = SetUpCutscenes(cam, player, enemies);

            // Wired after the cutscene layer exists, because a respawn has to hand the new boss to the
            // intro shot as well as to the victory hook. Nothing can respawn before _Ready returns, so the
            // subscription being late costs nothing.
            respawner.Initialize(scene, readability, player, enemies, hud.GetComponent<GameplayVictoryController>(), cutscenes);

            // Handed back so _Ready can subscribe the level-up save hook - after RestoreSaveIfRequested,
            // never before it.
            return player;
        }

        /// <summary>
        /// Stands the cutscene layer up, tells it what the roles map to, subscribes the triggers that
        /// fire from gameplay events, and plays the entry cutscene. Hands the triggers back so a respawn
        /// can re-point them at the boss it spawns.
        /// </summary>
        private static GameplayCutsceneTriggers SetUpCutscenes(Camera2D cam, GameplayPlayerContext player, GameplayEnemyContext enemies)
        {
            CutsceneOverlay overlay = CutsceneOverlay.Create();
            CutsceneDirector director = CutsceneDirector.Create(overlay);

            Node2D rig = GameplaySystemBootstrapper.EnsureCameraRig(cam);
            if (rig != null)
                director.Binder.Register(CutsceneRole.CameraRig, rig);

            director.Binder.Register(CutsceneRole.Overlay, overlay);
            director.Binder.Register(CutsceneRole.Signals, director);
            director.Binder.Register(CutsceneRole.Player, player.GameObject);

            // CutsceneRole.Boss is registered by Initialize below rather than here: the respawner rebinds
            // through the same call, and one owner of that binding is what keeps the two from drifting.
            var triggers = new GameplayCutsceneTriggers { Name = nameof(GameplayCutsceneTriggers) };
            director.AddChild(triggers);
            triggers.Initialize(director, player.DeathController, enemies.Boss);

            // The fade goes up here rather than on a clip: Build leaves it at alpha 0, and a director
            // that is the first writer renders the arena lit for the frames before it evaluates. _Ready
            // still runs ahead of the first rendered frame, so this is the last place the blackout can be
            // raised without the flash. If the shot is missing, Play clears it.
            overlay.SetBlackout(EnterLetterboxHeight);
            director.Play(EnterCutsceneKey);

            // Play disables camera follow, and CutsceneRigMove treats follow coming back on as the rig
            // being handed away - so the settle has to be asked for after Play, not before, or it releases
            // on its first frame. A missing shot re-enables follow inside Play and this cancels itself,
            // which is the behaviour we want when there is no cutscene to settle out of.
            if (rig != null)
            {
                Vector2 settled = rig.GlobalPosition;

                // Vector2.Up is (0, -1) in Godot, so this still lifts the rig by one authored unit.
                rig.GlobalPosition = settled + Vector2.Up * World.U(EnterSettleHeight);
                CutsceneRigMove.Play(rig, settled, EnterSettleDuration);
            }

            return triggers;
        }

        /// <summary>
        /// Runs after the HUD exists so restoring health, humanity and souls raises the change events the
        /// HUD is already listening to, instead of leaving the readouts showing the fresh-start numbers.
        /// The flag is consumed here, so a later New Game does not silently load the old save.
        /// </summary>
        private static void RestoreSaveIfRequested(GameplayPlayerContext player)
        {
            if (!GameSave.LoadOnNextGameplayStart)
                return;

            GameSave.LoadOnNextGameplayStart = false;
            GameplaySaveBridge.Apply(GameSave.Read(), player);
        }
    }
}
