using Godot;
using MyGame.Core;
using MyGame.UI;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Stands the HUD up and hands the screen's three scripts the objects they read.
    /// </summary>
    /// <remarks>
    /// Unity built a Canvas, a CanvasScaler and a GraphicRaycaster around the HUD and made sure an
    /// EventSystem existed. None of that survives: <see cref="GameplayHud"/> is itself the
    /// <c>CanvasLayer</c>, Godot's viewport handles the scaling the CanvasScaler was configured for, and
    /// focus and picking are the viewport's job rather than an EventSystem singleton's.
    /// <para>
    /// It used to build the screen too - a <c>new GameplayHud</c>, a <c>CreateUi()</c> that made 45-odd
    /// nodes, and two controllers added by hand. <c>Scenes/UI/GameplayHud.tscn</c> is all three now:
    /// the layer, the hierarchy under it and the two controller nodes are authored, and this only
    /// instances the scene and binds.
    /// </para>
    /// </remarks>
    public static class GameplayHudSpawner
    {
        /// <summary>The authored screen. Its root is already the CanvasLayer, named HUD.</summary>
        private const string HudScenePath = "res://Scenes/UI/GameplayHud.tscn";

        public static GameplayHud Spawn(GameplayPlayerContext player, GameplayEnemyContext enemies)
        {
            var hud = GD.Load<PackedScene>(HudScenePath).Instantiate<GameplayHud>();

            // Renamed from the scene's own root name to the constant the rest of the bootstrap looks the
            // HUD up by, so the name stays owned by one place even though the scene also spells it.
            hud.Name = GameplayBootstrap.HudObjectName;
            GameplayBuildShim.SceneRoot?.AddChild(hud);

            // AddChild ran _Ready and bound already; this is for the headless case where SceneRoot is
            // null and nothing entered a tree. Binding twice resolves the same children - see
            // GameplayHud.Bind. The call is kept because it is still the entry point every caller uses.
            hud.CreateUi();
            hud.Initialize(player.Health, player.Humanity, player.Sin, player.DeathController, player.Stamina, player.Controller);
            hud.BindCombatResources(player.Poise, player.Wallet);

            var input = player.GameObject?.GetComponent<PlayerInputReceiver>();

            // Authored children of the HUD layer rather than nodes added here. They are plain Nodes with
            // no rectangle, so they sit on the layer beside HudRoot.
            var victoryController = hud.GetNode<GameplayVictoryController>(nameof(GameplayVictoryController));
            victoryController.Initialize(enemies.Boss, hud, input, player);

            var pauseController = hud.GetNode<GameplayPauseController>(nameof(GameplayPauseController));
            pauseController.Initialize(hud, input, victoryController, player);
            return hud;
        }
    }
}
