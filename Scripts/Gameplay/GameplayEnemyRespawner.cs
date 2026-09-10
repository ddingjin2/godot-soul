using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Puts every enemy back when the player respawns, so dying costs the ground you had taken.
    /// Rebuilds through <see cref="GameplayEnemySpawner"/> instead of restoring the survivors in place:
    /// a killed enemy is already gone by way of EnemyDeathCleanup, and a survivor carries live telegraph
    /// timers, rage flags and phase-two state that a partial reset would have to unwind one by one.
    /// </summary>
    public sealed partial class GameplayEnemyRespawner : Node
    {
        private GameplaySceneDefaults _scene;
        private GameplayReadabilityDefaults _readability;
        private GameplayPlayerContext _player;
        private GameplayVictoryController _victory;
        private GameplayCutsceneTriggers _cutscenes;
        private GameplayEnemyContext _enemies;

        public GameplayEnemyContext Enemies => _enemies;

        public void Initialize(
            GameplaySceneDefaults scene,
            GameplayReadabilityDefaults readability,
            GameplayPlayerContext player,
            GameplayEnemyContext enemies,
            GameplayVictoryController victory,
            GameplayCutsceneTriggers cutscenes)
        {
            Unsubscribe();

            _scene = scene;
            _readability = readability;
            _player = player;
            _enemies = enemies;
            _victory = victory;
            _cutscenes = cutscenes;

            // Unity's UnityEvent.AddListener; a plain C# event here, so += and -=.
            if (_player.DeathController != null)
                _player.DeathController.OnRespawn += RespawnEnemies;
        }

        /// <summary>Unity's <c>OnDestroy</c>.</summary>
        public override void _ExitTree()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_player.DeathController != null)
                _player.DeathController.OnRespawn -= RespawnEnemies;
        }

        public void RespawnEnemies()
        {
            if (_scene == null)
                return;

            DestroyCurrentEnemies();

            _enemies = GameplayEnemySpawner.Spawn(_scene, _readability, _player);

            if (_victory != null)
                _victory.RebindBoss(_enemies.Boss);

            // Same rebind, same place. Everything that was pointed at the boss this method just destroyed
            // has to be handed the replacement here, or it keeps holding a corpse: the victory hook above,
            // and the intro cutscene's event plus its Boss binding below.
            if (_cutscenes != null)
                _cutscenes.RebindBoss(_enemies.Boss);
        }

        private void DestroyCurrentEnemies()
        {
            if (_enemies.Enemies == null)
                return;

            // The boss node is part of this list, so it needs no separate pass.
            foreach (Node2D enemy in _enemies.Enemies)
            {
                if (!IsInstanceValid(enemy))
                    continue;

                // QueueFree only lands at the end of the frame, so stop them acting - and colliding - in
                // the meantime. That is what Unity's SetActive(false) before Destroy bought.
                enemy.SetActive(false);
                enemy.QueueFree();
            }
        }
    }
}
