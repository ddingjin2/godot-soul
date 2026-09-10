using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.UI;

namespace MyGame.Tests
{
    /// <summary>
    /// The screen the player reads once per chapter. Both failures here are silent: a subtitle naming
    /// the wrong boss reads correct in exactly one arena, and a button label that survives from the last
    /// time the panel was shown offers to open a gate while the action behind it restarts.
    ///
    /// Driven through the real chapter-two HUD rather than a fixture, so nothing test-only has to be
    /// opened up on the product class to reach it.
    /// </summary>
    /// <remarks>
    /// PORT: uGUI <c>Text</c> is a Godot <c>Label</c> and the button carries its own <c>Text</c>, so
    /// Unity's "the Text under a named object" helper is now a plain lookup by node name.
    /// <c>Time.timeScale</c> is <see cref="GameClock.TimeScale"/>.
    /// <para>
    /// The save slot lives in <c>user://playerprefs.cfg</c> and survives the process, so unlike the Unity
    /// fixture - which put the developer's slot back afterwards - this clears the store outright at both
    /// ends. A leftover slot would hand the next headless run a Continue it did not ask for.
    /// </para>
    /// </remarks>
    public sealed class GameplayVictoryPanelTests
    {
        private const string ChapterTwoSceneName = "Chapter02_Orange";
        private const string GameplaySceneName = "GameplayScene";

        private float _startTimeScale;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;

            // PlayerPrefs is a file that outlives the run; a slot left by an earlier suite would decide
            // which difficulty this chapter builds on.
            PlayerPrefs.DeleteAll();
        }

        [TearDown]
        public void TearDown()
        {
            GameClock.TimeScale = _startTimeScale;
            GameSave.LoadOnNextGameplayStart = false;
            PlayerPrefs.DeleteAll();
        }

        /// <summary>
        /// The subtitle said "Wrath has fallen" in every chapter, because chapter one's boss was the only
        /// one that existed when the panel was written - correct in one arena and wrong in seven.
        ///
        /// The label check rides along on the same scene load: a panel re-shown with no label has to put
        /// 다시하기 back, or a rematch offers 다음 관문 over an action that restarts.
        /// </summary>
        [Test]
        public async Task VictoryPanel_NamesTheBossThatFell_AndResetsItsLabel()
        {
            GameSave.Clear();

            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(ChapterTwoSceneName));

            var boss = SceneQuery.FindFirst<RainbowChapterBossBehaviour>();
            Assert.NotNull(boss, "Chapter two has to stand its boss up first.");
            Assert.AreEqual("Ember Pilgrim", boss.BossName, "The fixture assumes chapter two's authored boss.");

            var hud = SceneQuery.FindFirst<GameplayHud>();
            Assert.NotNull(hud, "The panel lives on the HUD.");

            // Damage and knockback direction are unscaled and horizontal, so nothing converts here.
            boss.GetComponent<Health>().ApplyDamage(99999f, Vector2.Right);

            // Waits on the panel being up, not on its text: the subtitle ships reading "Wrath has fallen",
            // so a wait on the word would return before the panel had been shown at all and then assert
            // against the text the panel was built with.
            //
            // The runner's WaitUntil polls on the wall clock and on ProcessFrame, both of which keep
            // running at the zero timescale the victory path sets - Unity needed WaitForSecondsRealtime
            // for the same reason.
            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(
                    () => FindNamed<Label>(hud, "VictorySubtitle")?.IsVisibleInTree() == true, 8f),
                "The victory panel should have come up by now.");

            Assert.IsTrue(FindNamed<Label>(hud, "VictorySubtitle").Text.Contains("Ember Pilgrim"),
                "The panel has to name the boss that fell. Every chapter after the first read Wrath's name.");

            Button primary = FindNamed<Button>(hud, "RestartButton");
            Assert.NotNull(primary, "The primary button carries its own label.");
            Assert.AreEqual("다음 관문", primary.Text,
                "Chapter two has a gate after it, so the panel offers to open it rather than to restart.");

            // Re-shown the way a rematch would, with no label of its own.
            hud.ShowVictory(null, null);
            Assert.AreEqual("다시하기", primary.Text,
                "Showing the panel with no label has to put the default back, not keep the last one.");

            GameClock.TimeScale = 1f;
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));
        }

        /// <summary>The named node under the HUD. Unity searched for a Text on or under it; here it is the node itself.</summary>
        private static T FindNamed<T>(Node root, string nodeName) where T : Node
        {
            if (root is T match && root.Name == nodeName)
                return match;

            foreach (Node child in root.GetChildren())
            {
                T found = FindNamed<T>(child, nodeName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
