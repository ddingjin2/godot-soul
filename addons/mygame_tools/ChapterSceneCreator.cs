#if TOOLS
using System.Collections.Generic;
using System.IO;
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Makes the scene a chapter needs. A gameplay scene in this project is one object - the bootstrap
    /// root - and everything else, camera included, is built at runtime off
    /// <c>SceneLayout_&lt;SceneName&gt;.json</c>, so a new chapter's scene is the shipped shell under a
    /// new name.
    /// </summary>
    /// <remarks>
    /// Unity copied <c>GameplayScene.unity</c> through the AssetDatabase, because scene YAML does not
    /// merge and its <c>.meta</c> has to come out of a Unity run. A <c>.tscn</c> shell has neither
    /// problem and is five lines, so it is written directly - which also means this tool works before
    /// <c>GameplayScene.tscn</c> exists.
    ///
    /// The build-settings entry the Unity version added afterwards has no counterpart: Godot has no
    /// scene list, and a scene is addressed by its path.
    ///
    /// The root node keeps Unity's name, <c>GameplayRoot</c>. What picks a chapter's layout is the
    /// scene's file name, which is the file name here too.
    ///
    /// Missing-only, like the other generators here: a scene someone has since opened and edited is
    /// left exactly as it is.
    /// </remarks>
    public static class ChapterSceneCreator
    {
        public const string SceneFolder = "res://Scenes";

        /// <summary>
        /// The scene every chapter inherits - the bootstrap script and the system nodes on one file.
        /// Under <c>Scenes/World/</c> so <c>ChapterRoute.Scenes()</c>, which reads only the files
        /// directly in <c>Scenes/</c>, does not play it as a ninth chapter.
        /// </summary>
        public const string ShellScene = "res://Scenes/World/GameplayShell.tscn";

        /// <summary>
        /// Scene names a chapter layout expects, without folder or extension. A name here has to match
        /// the <c>SceneLayout_&lt;SceneName&gt;.json</c> beside it or the scene loads the shared arena.
        /// </summary>
        private static readonly string[] ChapterScenes =
        {
            "Chapter02_Orange",
            "Chapter03_Yellow",
            "Chapter04_Green",
            "Chapter05_Blue",
            "Chapter06_Indigo",
            "Chapter07_Violet",
            "Chapter08_White"
        };

        public const string SuccessMarker = "ChapterSceneCreator: done.";

        public static void CreateMissing()
        {
            Directory.CreateDirectory(DesignDataFiles.Abs(SceneFolder));

            var created = new List<string>();

            foreach (string sceneName in ChapterScenes)
            {
                string resPath = SceneFolder + "/" + sceneName + ".tscn";
                string path = DesignDataFiles.Abs(resPath);

                if (File.Exists(path))
                {
                    continue;
                }

                File.WriteAllText(path, Shell());
                created.Add(resPath);
            }

            GD.Print(created.Count > 0
                ? $"ChapterSceneCreator: created {string.Join(", ", created)}. {SuccessMarker}"
                : $"ChapterSceneCreator: every chapter scene already exists. {SuccessMarker}");
        }

        /// <summary>
        /// The shell, hand-written rather than built through <c>PackedScene</c>: this addon deliberately
        /// does not depend on <c>MyGame.Gameplay</c> being compiled yet.
        ///
        /// Since K7 a chapter is an inherited scene rather than a copy of the bootstrap node - the
        /// bootstrap script, the camera rig, the hit stop manager, the cutscene director and the enemy
        /// respawner all live on <c>Scenes/World/GameplayShell.tscn</c>, and this is the one line that
        /// inherits it. Keep the root's name: <c>GameplayBuildShim.SceneRoot</c> and every test that
        /// walks the scene read it back.
        /// </summary>
        private static string Shell()
        {
            return "[gd_scene load_steps=2 format=3]\n\n" +
                   $"[ext_resource type=\"PackedScene\" path=\"{ShellScene}\" id=\"1_shell\"]\n\n" +
                   "[node name=\"GameplayRoot\" instance=ExtResource(\"1_shell\")]\n";
        }
    }
}
#endif
