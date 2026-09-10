using MyGame.Core;
using MyGame.UI;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Stands the HUD up and hangs the two controllers that own the screen's modal states off it.
    /// </summary>
    /// <remarks>
    /// Unity built a Canvas, a CanvasScaler and a GraphicRaycaster around the HUD and made sure an
    /// EventSystem existed. None of that survives: <see cref="GameplayHud"/> is itself the
    /// <c>CanvasLayer</c>, Godot's viewport handles the scaling the CanvasScaler was configured for, and
    /// focus and picking are the viewport's job rather than an EventSystem singleton's.
    /// </remarks>
    public static class GameplayHudSpawner
    {
        public static GameplayHud Spawn(GameplayPlayerContext player, GameplayEnemyContext enemies)
        {
            var hud = new GameplayHud { Name = GameplayBootstrap.HudObjectName };
            GameplayBuildShim.SceneRoot?.AddChild(hud);

            // CreateUi defaults to the HUD itself, which is the CanvasLayer everything is built into.
            hud.CreateUi();
            hud.Initialize(player.Health, player.Humanity, player.Sin, player.DeathController, player.Stamina, player.Controller);
            hud.BindCombatResources(player.Poise, player.Wallet);

            var input = player.GameObject?.GetComponent<PlayerInputReceiver>();

            var victoryController = new GameplayVictoryController { Name = nameof(GameplayVictoryController) };
            hud.AddChild(victoryController);
            victoryController.Initialize(enemies.Boss, hud, input, player);

            var pauseController = new GameplayPauseController { Name = nameof(GameplayPauseController) };
            hud.AddChild(pauseController);
            pauseController.Initialize(hud, input, victoryController, player);
            return hud;
        }
    }
}
