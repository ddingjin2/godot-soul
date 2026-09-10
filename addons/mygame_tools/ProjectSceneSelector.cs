#if TOOLS
using System;
using System.IO;
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Every scene in the project as a button.
    /// </summary>
    /// <remarks>
    /// Unity had two of these - <c>ProjectSceneSelector</c>, a fixed pair of <c>[MenuItem]</c> entries
    /// plus the code that kept Build Settings in order, and <c>ScenePickerWindow</c>, a window that
    /// listed the folder because a menu item's path is a compile-time constant and one per chapter
    /// drifts from the folder. They are one section here.
    ///
    /// Most of what the selector did is gone with Unity, not dropped: Godot has no Build Settings scene
    /// list to keep in sync, no build indices for <c>GameplayDebugSceneJump</c>'s F-keys to depend on
    /// (it addresses scenes by path), and no need to author <c>TitleScene</c> from code. What is left is
    /// the picker: read the folder, one button per scene.
    ///
    /// Rebuilt every time the dock is created rather than on a timer - a scene added on disk shows up
    /// when the plugin reloads, which is also when a newly added C# scene script becomes loadable.
    /// </remarks>
    public static class ProjectSceneSelector
    {
        /// <summary>Campaign scenes: the game itself. Top level only - subfolders are not campaign.</summary>
        public const string SceneFolder = "res://Scenes";

        /// <summary>
        /// Scenes that exist to be tested against rather than played. Kept apart so a fixture arena can
        /// be added without it becoming a chapter.
        /// </summary>
        public const string TestSceneFolder = SceneFolder + "/Test";

        public static void BuildSceneList(Container parent)
        {
            string[] campaign = ScenesIn(SceneFolder);
            string[] test = ScenesIn(TestSceneFolder);

            if (campaign.Length == 0)
            {
                parent.AddChild(new Label { Text = $"  (no scenes in {SceneFolder})" });
            }

            foreach (string path in campaign)
            {
                AddOpenButton(parent, path);
            }

            foreach (string path in test)
            {
                AddOpenButton(parent, path);
            }
        }

        private static void AddOpenButton(Container parent, string resPath)
        {
            var button = new Button
            {
                Text = "Open " + Path.GetFileNameWithoutExtension(resPath),
                TooltipText = resPath,
            };

            button.Pressed += () => EditorInterface.Singleton.OpenSceneFromPath(resPath);
            parent.AddChild(button);
        }

        /// <summary>
        /// Scenes directly in a folder, in name order. Title and gameplay sort ahead of the chapters by
        /// accident of the alphabet, which is close enough to the order Unity pinned by hand.
        /// </summary>
        private static string[] ScenesIn(string resFolder)
        {
            string folder = DesignDataFiles.Abs(resFolder);
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            string[] files = Directory.GetFiles(folder, "*.tscn", SearchOption.TopDirectoryOnly);
            for (var i = 0; i < files.Length; i++)
            {
                files[i] = resFolder + "/" + Path.GetFileName(files[i]);
            }

            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
    }
}
#endif
