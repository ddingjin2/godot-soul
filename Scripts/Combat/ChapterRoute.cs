using System.Collections.Generic;
using Godot;

namespace MyGame.Combat
{
    /// <summary>
    /// The road, in order. Seven gates in sequence and each must be passed before the next opens
    /// ([[WorldSetting]]), plus the chapter that stands past the seventh - so "which chapter comes
    /// next" is a fact about the campaign, not a choice a menu makes.
    ///
    /// The order used to be read out of Unity's Build Settings, which was the list that decided which
    /// scenes existed and in what order. Godot has no such list, so the road is read off the scene
    /// folder instead: every <c>res://Scenes/*.tscn</c> that is not the title screen, sorted by name,
    /// with <see cref="DefaultFirstChapterScene"/> pulled to the front because it is chapter one and
    /// would otherwise sort after <c>Chapter02_Orange</c>. Still derived rather than written down, so
    /// adding a chapter file adds a gate and nothing else has to be edited.
    /// </summary>
    /// <remarks>
    /// Lives in Combat for the same reason <see cref="GameSave"/> does: the title menu is UI, the
    /// victory panel is Gameplay, and UI is not allowed to reference Gameplay.
    /// </remarks>
    public static class ChapterRoute
    {
        /// <summary>
        /// The scene the road starts at: the first chapter in the folder, falling back to the shipped
        /// arena's name when there are no chapters at all. Derived rather than fixed, so deleting
        /// GameplayScene cannot leave Resume handing back a scene that will not load.
        /// </summary>
        public static string FirstChapterScene
        {
            get
            {
                string[] scenes = Scenes();
                return scenes.Length > 0 ? scenes[0] : DefaultFirstChapterScene;
            }
        }

        public const string DefaultFirstChapterScene = "GameplayScene";

        private const string TitleScene = "TitleScene";

        /// <summary>Where the chapter scenes live. Test scenes sit in a subfolder and are not listed.</summary>
        private const string SceneFolder = "res://Scenes/";

        /// <summary>The file a chapter name names. The one place the folder and extension are spelled out.</summary>
        public static string ScenePath(string sceneName) => SceneFolder + sceneName + ".tscn";

        /// <summary>
        /// Every chapter scene, in the order they are played. The title screen is not a chapter, and
        /// neither is anything under <c>Scenes/Test/</c> - only files directly in the folder are read.
        /// </summary>
        public static string[] Scenes()
        {
            var scenes = new List<string>();

            using DirAccess dir = DirAccess.Open(SceneFolder);
            if (dir == null)
                return scenes.ToArray();

            foreach (string file in dir.GetFiles())
            {
                // An exported build serves "X.tscn.remap"; the scene is still called X.
                string name = file.EndsWith(".remap") ? file[..^".remap".Length] : file;
                if (!name.EndsWith(".tscn"))
                    continue;

                name = name[..^".tscn".Length];
                if (name == TitleScene || scenes.Contains(name))
                    continue;

                scenes.Add(name);
            }

            scenes.Sort(string.CompareOrdinal);

            // Chapter one is called GameplayScene, which sorts after every Chapter0N file. The road is an
            // order, not an alphabet, so it goes back to the front.
            int first = scenes.IndexOf(DefaultFirstChapterScene);
            if (first > 0)
            {
                scenes.RemoveAt(first);
                scenes.Insert(0, DefaultFirstChapterScene);
            }

            return scenes.ToArray();
        }

        /// <summary>Where in the road a scene sits, or -1 for a scene that is not a chapter.</summary>
        public static int IndexOf(string sceneName)
        {
            string[] scenes = Scenes();
            for (var i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] == sceneName)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// The chapter after this one, or null at the end of the road. Null is the answer the victory
        /// panel needs to tell "you passed a gate" from "you finished".
        /// </summary>
        public static string Next(string sceneName)
        {
            string[] scenes = Scenes();
            int index = IndexOf(sceneName);

            if (index < 0 || index + 1 >= scenes.Length)
                return null;

            return scenes[index + 1];
        }

        /// <summary>
        /// Whether <paramref name="sceneName"/> is a chapter the player has reached. An unknown scene is
        /// refused rather than trusted: the name comes out of a save slot, and PlayerPrefs is editable
        /// from outside the game.
        /// </summary>
        public static bool IsChapter(string sceneName) => IndexOf(sceneName) >= 0;

        /// <summary>
        /// The scene a Continue should open. Falls back to the start of the road for a save written
        /// before chapters existed, or one naming a scene this build does not have.
        /// </summary>
        public static string Resume(GameSaveData save)
        {
            if (save != null && IsChapter(save.chapterScene))
                return save.chapterScene;

            return FirstChapterScene;
        }

        /// <summary>
        /// How far down the road this save has ever been, as an index. Not the same question as
        /// <see cref="Resume"/>: travelling back through a gate portal moves <c>chapterScene</c>
        /// backwards, and a gate that has been opened does not close again.
        ///
        /// Takes the larger of the recorded furthest and the index of the recorded scene, so a slot
        /// written before <see cref="GameSaveData.furthestChapter"/> existed - where the field reads 0 -
        /// still unlocks everything up to where it left off.
        /// </summary>
        public static int FurthestIndex(GameSaveData save)
        {
            if (save == null)
                return 0;

            int last = Scenes().Length - 1;
            if (last < 0)
                return 0;

            int recorded = Mathf.Clamp(save.furthestChapter, 0, last);
            return Mathf.Max(recorded, IndexOf(save.chapterScene));
        }

        /// <summary>
        /// Every gate this save may travel to: the road up to and including the furthest one reached.
        /// Empty only when there are no chapter scenes at all.
        /// </summary>
        public static string[] Unlocked(GameSaveData save)
        {
            string[] scenes = Scenes();
            if (scenes.Length == 0)
                return scenes;

            int furthest = Mathf.Clamp(FurthestIndex(save), 0, scenes.Length - 1);
            var unlocked = new string[furthest + 1];
            System.Array.Copy(scenes, unlocked, furthest + 1);
            return unlocked;
        }

        /// <summary>
        /// Points a slot at a chapter, dropping what belonged to the one it is leaving. The bonfire index
        /// and the shortcut door are both chapter-scoped: index 2 in the chapter being entered is a
        /// different bonfire from index 2 in the one being left, or no bonfire at all, and a door opened
        /// back there was never opened here.
        /// </summary>
        /// <remarks>
        /// The single owner of that rule, and the reason it is a method rather than three lines at each
        /// call site: passing a gate, travelling through a portal and opening a shortcut all move or stamp
        /// the recorded chapter, and a copy of this in each of them is a copy that drifts. Moving to the
        /// chapter already recorded is a no-op, which is what keeps the end of the road - where the gate
        /// passed records itself - from clearing a rest the player has not left.
        /// </remarks>
        public static void SetChapter(GameSaveData save, string sceneName)
        {
            if (save == null || string.IsNullOrEmpty(sceneName) || save.chapterScene == sceneName)
                return;

            save.chapterScene = sceneName;
            save.checkpointIndex = -1;
            save.shortcutOpened = false;
        }

        /// <summary>
        /// Writes a passed gate into a slot: the gate that is now open, the furthest ever reached, and -
        /// when the road ends here - the Hard difficulty that finishing it earns.
        ///
        /// Here rather than in the victory controller because a portal and a New Game+ both have to agree
        /// with it about what a passed gate means, and Gameplay is not a place UI can read.
        /// </summary>
        public static void RecordGatePassed(GameSaveData save, string clearedScene)
        {
            if (save == null || !IsChapter(clearedScene))
                return;

            string next = Next(clearedScene);

            // A slot may not name a chapter that does not exist, so the end of the road records itself.
            // Through SetChapter, because this is the moment the slot stops describing the chapter the
            // capture was taken in: without it the next chapter opens at the bonfire index and with the
            // shortcut of the one just beaten.
            SetChapter(save, next ?? clearedScene);

            // Read back after the write, because FurthestIndex already takes the recorded scene into
            // account - so this only ever moves forwards.
            save.furthestChapter = FurthestIndex(save);

            // Easy finishes the story and unlocks nothing: Hard is what Normal earns. Said here so the
            // rule cannot differ between the victory panel and anything else that ends a run.
            if (next == null && save.difficulty >= (int)Difficulty.Normal)
                save.hardUnlocked = true;
        }
    }
}
