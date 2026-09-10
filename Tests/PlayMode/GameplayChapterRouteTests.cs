using Godot;
using MyGame.Combat;

namespace MyGame.Tests
{
    /// <summary>
    /// The road, as the game reads it. Every one of these breaks silently: a chapter missing from the
    /// order is a gate the player can never open, a test scene inside the order is a chapter that is not
    /// one, and a Resume that trusts a bad save name loads a scene that is not in the build.
    ///
    /// PORT: Unity read the order out of Build Settings and asked
    /// <c>SceneUtility.GetBuildIndexByScenePath</c> whether each entry was loadable. Godot has no build
    /// list - <see cref="ChapterRoute.Scenes"/> enumerates <c>res://Scenes/*.tscn</c>, drops
    /// <c>TitleScene</c>, sorts ordinal and pulls <c>GameplayScene</c> to the front - so "listed in the
    /// build" becomes "the file <see cref="ChapterRoute.ScenePath"/> names actually exists". Same risk,
    /// same failure: a name in the road that will not load is a gate that cannot be opened.
    /// </summary>
    public sealed class GameplayChapterRouteTests
    {
        [Test]
        public void Road_StartsAtChapterOne_AndHoldsEveryChapterScene()
        {
            string[] scenes = ChapterRoute.Scenes();

            Assert.Greater(scenes.Length, 1, "A road with one stop is not a campaign.");
            Assert.AreEqual(ChapterRoute.FirstChapterScene, scenes[0],
                "The road has to start at chapter one, or Continue on a fresh save opens the wrong arena.");

            Assert.DoesNotContain("TitleScene", scenes, "The title screen is not a gate.");

            foreach (string scene in scenes)
            {
                Assert.IsTrue(ResourceLoader.Exists(ChapterRoute.ScenePath(scene)),
                    $"'{scene}' is in the road but there is no {ChapterRoute.ScenePath(scene)} to load, " +
                    "so opening that gate would fail.");
            }
        }

        /// <summary>
        /// Sequence is the whole premise: each gate must be passed before the next opens. This pins that
        /// the order the victory panel walks is the order the scene folder yields, and that the last
        /// chapter reports no next - which is how the panel tells passing a gate from finishing.
        /// </summary>
        [Test]
        public void Next_WalksTheRoadInOrder_AndEndsAtTheLastChapter()
        {
            string[] scenes = ChapterRoute.Scenes();
            Assert.Greater(scenes.Length, 0, "There is no road to walk.");

            for (var i = 0; i < scenes.Length - 1; i++)
            {
                Assert.AreEqual(scenes[i + 1], ChapterRoute.Next(scenes[i]),
                    $"'{scenes[i]}' should open the gate after it.");
            }

            Assert.IsNull(ChapterRoute.Next(scenes[scenes.Length - 1]),
                "The last chapter opens nothing; the panel needs that null to stop offering another gate.");

            Assert.IsNull(ChapterRoute.Next("TitleScene"), "A scene that is not a chapter has no next chapter.");
        }

        /// <summary>
        /// The save slot is PlayerPrefs, which is editable from outside the game, so every value read
        /// out of it is untrusted input rather than something this code wrote.
        /// </summary>
        [Test]
        public void Resume_FallsBackToChapterOne_ForAnythingItCannotTrust()
        {
            Assert.AreEqual(ChapterRoute.FirstChapterScene, ChapterRoute.Resume(null),
                "No save at all resumes at the start of the road.");

            Assert.AreEqual(ChapterRoute.FirstChapterScene, ChapterRoute.Resume(new GameSaveData()),
                "A slot written before chapters existed has an empty chapter and resumes at the start.");

            Assert.AreEqual(ChapterRoute.FirstChapterScene,
                ChapterRoute.Resume(new GameSaveData { chapterScene = "Chapter99_NotReal" }),
                "A slot naming a scene this build does not have must not be loaded on trust.");

            string[] scenes = ChapterRoute.Scenes();
            Assert.Greater(scenes.Length, 0, "There is no road to resume onto.");

            string last = scenes[scenes.Length - 1];
            Assert.AreEqual(last, ChapterRoute.Resume(new GameSaveData { chapterScene = last }),
                "A slot naming a real chapter resumes there, or progress means nothing.");
        }
    }
}
