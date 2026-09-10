#if DEBUG
using System.Collections.Generic;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// F1 to F9 jump straight to a scene, so a chapter can be reached without playing to it. F1 is the
    /// first scene in the list, F2 the second, and so on - today that is TitleScene, GameplayScene,
    /// Chapter02_Orange. The mapping is logged on start rather than written down anywhere, because the
    /// order is whatever the scene folder holds.
    ///
    /// The list is derived rather than authored: Unity read Build Settings, Godot reads
    /// <c>res://Scenes/</c> through <see cref="ChapterRoute.Scenes"/> with the title screen put back on
    /// the front, since that is the only scene the road itself does not count.
    /// </summary>
    /// <remarks>
    /// The whole file is compiled out of a release build. It is not a cheat the game ships and gates at
    /// runtime - a switch that exists can be found, and this one would let a player skip the game.
    ///
    /// Unity installed it from a <see cref="RuntimeInitializeOnLoadMethod"/> so it would exist in every
    /// scene without being wired into one. Godot has no such hook, so
    /// <see cref="GameplayBootstrap"/> calls <see cref="Install"/> and the node is parented to the tree
    /// root, which is this port's <c>DontDestroyOnLoad</c>: it survives every scene change after the
    /// first gameplay scene. The one behaviour difference is that a session that never leaves the title
    /// screen has no jump keys.
    /// </remarks>
    public sealed partial class GameplayDebugSceneJump : Node
    {
        /// <summary>
        /// Number of jump slots. Twelve, because that is how many function keys the keyboard has - and
        /// because nine was exactly the number of scenes that existed when it was written, so the tenth
        /// chapter would have had no key and nothing to say so.
        /// </summary>
        public const int SlotCount = 12;

        private const string NodeName = "GameplayDebugSceneJump";

        public static void Install(Node context)
        {
            SceneTree tree = context?.GetTree() ?? GameplayBuildShim.SceneRoot?.GetTree();
            if (tree == null)
                return;

            // A headless run is the test runner, which drives scenes itself; a stray key reader there
            // would be one more thing able to change the scene under a running suite.
            if (DisplayServer.GetName() == "headless")
                return;

            if (tree.Root.GetNodeOrNull(NodeName) != null)
                return;

            // Parented to the tree root rather than the current scene, so it outlives the scene it
            // jumps out of - Unity's DontDestroyOnLoad.
            tree.Root.CallDeferred(Node.MethodName.AddChild, new GameplayDebugSceneJump { Name = NodeName });

            GD.Print("GameplayDebugSceneJump: " + DescribeSlots());
        }

        /// <summary>The scene each slot would load, as one line. Slots past the end of the list are skipped.</summary>
        public static string DescribeSlots()
        {
            var description = "";

            for (var slot = 0; slot < SlotCount; slot++)
            {
                if (!TryResolveSlot(slot, out string path))
                    continue;

                if (description.Length > 0)
                    description += ", ";

                description += $"F{slot + 1}={path.GetFile().GetBaseName()}";
            }

            return description.Length > 0 ? description : "no scenes found";
        }

        /// <summary>
        /// The scene at <paramref name="slot"/>, or false when there is no scene there. Separated from
        /// the key reading so the mapping can be asserted without pressing anything.
        /// </summary>
        public static bool TryResolveSlot(int slot, out string scenePath)
        {
            scenePath = null;

            string[] scenes = SlotScenes();
            if (slot < 0 || slot >= scenes.Length)
                return false;

            scenePath = ChapterRoute.ScenePath(scenes[slot]);
            return ResourceLoader.Exists(scenePath);
        }

        /// <summary>
        /// The title screen, then the road in order. ChapterRoute drops TitleScene because it is not a
        /// gate; a debug jump wants it, and it was slot one in Unity's build list.
        /// </summary>
        private static string[] SlotScenes()
        {
            var scenes = new List<string> { GameplayVictoryController.TitleSceneName };
            scenes.AddRange(ChapterRoute.Scenes());
            return scenes.ToArray();
        }

        /// <summary>
        /// Loads the scene at <paramref name="slot"/> and returns whether it went. Public so a jump can
        /// be driven from a console or a test rather than only from the keyboard.
        /// </summary>
        public bool JumpTo(int slot)
        {
            if (!TryResolveSlot(slot, out string path))
            {
                GD.PushWarning($"GameplayDebugSceneJump: no scene at slot {slot + 1}.");
                return false;
            }

            // The victory panel and the pause menu both freeze time, and a scene loaded under a zero
            // timescale opens frozen with nothing left to unfreeze it.
            GameClock.TimeScale = 1f;

            GD.Print($"GameplayDebugSceneJump: jumping to {path}.");
            GetTree().ChangeSceneToFile(path);
            return true;
        }

        public override void _Ready()
        {
            // Reads keys through a frozen game, which is the whole point of a debug jump.
            ProcessMode = ProcessModeEnum.Always;
        }

        /// <summary>
        /// Unhandled key input rather than a per-frame poll: these are twelve keys nothing else binds,
        /// and adding twelve InputMap actions for a dev-only tool would put them in the shipped project
        /// file.
        /// </summary>
        public override void _UnhandledKeyInput(InputEvent @event)
        {
            if (@event is not InputEventKey key || !key.Pressed || key.Echo)
                return;

            var slot = (int)(key.Keycode - Key.F1);
            if (slot < 0 || slot >= SlotCount)
                return;

            if (JumpTo(slot))
                GetViewport().SetInputAsHandled();
        }
    }
}
#endif
