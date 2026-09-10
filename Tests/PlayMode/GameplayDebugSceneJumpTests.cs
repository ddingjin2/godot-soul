#if DEBUG
using System.Collections.Generic;
using Godot;
using MyGame.Combat;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// The debug jump is only worth having if its slots point at the scenes someone expects, and the
    /// mapping is derived from the scene folder rather than authored - so the thing that can break it is
    /// a scene being added, removed or renamed, not a code edit.
    ///
    /// Nothing here presses a key or loads a scene: the suites drive scenes themselves, and a test that
    /// jumped would be changing the world out from under the runner.
    /// </summary>
    /// <remarks>
    /// PORT: Unity derived the slots from Build Settings and asked
    /// <c>SceneManager.sceneCountInBuildSettings</c> how many there were. Godot has no build scene list,
    /// so <see cref="GameplayDebugSceneJump"/> reads <c>res://Scenes/</c> through
    /// <see cref="ChapterRoute.Scenes"/> with <c>TitleScene</c> put back on the front - and "inside the
    /// build" becomes "inside that list". The whole fixture is compiled out of a release build with the
    /// file it exercises.
    /// </remarks>
    public sealed class GameplayDebugSceneJumpTests
    {
        /// <summary>The slot list the jump derives: the title screen, then the road in order.</summary>
        private static string[] SlotScenes()
        {
            var scenes = new List<string> { GameplayVictoryController.TitleSceneName };
            scenes.AddRange(ChapterRoute.Scenes());
            return scenes.ToArray();
        }

        [Test]
        public void EverySlotWithinTheBuild_ResolvesToARealScene()
        {
            string[] slotScenes = SlotScenes();

            Assert.Greater(slotScenes.Length, 0, "A project with no scenes has nothing to jump to.");

            for (var slot = 0; slot < GameplayDebugSceneJump.SlotCount; slot++)
            {
                bool resolved = GameplayDebugSceneJump.TryResolveSlot(slot, out string path);

                if (slot < slotScenes.Length)
                {
                    Assert.IsTrue(resolved, $"Slot {slot} is inside the scene list, so F{slot + 1} has to resolve.");
                    Assert.IsNotEmpty(path, $"Slot {slot} resolved to an empty path.");
                }
                else
                {
                    Assert.IsFalse(resolved, $"Slot {slot} is past the end of the list and must refuse rather than throw.");
                }
            }
        }

        /// <summary>
        /// The ordering contract the F-keys rest on.
        ///
        /// PORT - RETARGETED. Unity's version walked the build list and asserted no test scene sat ahead
        /// of a campaign scene, because a test scene injected into Build Settings shifted every chapter's
        /// key. That workaround - the <c>InitTestScene</c> build-settings exclusion - has nothing to
        /// exclude from here: there is no build scene list to inject into, and the slots come out of
        /// <see cref="ChapterRoute.Scenes"/>. So this asserts the enumeration that replaced it: the title
        /// screen is slot one, the road follows it in <see cref="ChapterRoute.Scenes"/> order, and every
        /// slot names a file that sits directly under <c>res://Scenes/</c> - which is the only place the
        /// enumeration looks, and therefore the reason a test scene in a subfolder can never take a key.
        /// </summary>
        [Test]
        public void CampaignScenes_AllComeBeforeTestScenes()
        {
            string[] slotScenes = SlotScenes();

            Assert.AreEqual(GameplayVictoryController.TitleSceneName, slotScenes[0],
                "F1 is the title screen; the road starts at F2. A slot list that opens elsewhere moves every chapter's key.");

            string[] road = ChapterRoute.Scenes();
            for (var i = 0; i < road.Length; i++)
            {
                Assert.AreEqual(road[i], slotScenes[i + 1],
                    "The slots after the title follow the road in order, or a reorder moves every chapter's key.");
            }

            for (var slot = 0; slot < GameplayDebugSceneJump.SlotCount; slot++)
            {
                if (!GameplayDebugSceneJump.TryResolveSlot(slot, out string path))
                    continue;

                Assert.AreEqual("res://Scenes/" + path.GetFile(), path,
                    $"F{slot + 1} resolves to '{path}', which is not directly under res://Scenes/. The slot list " +
                    "only enumerates that folder's top level, which is what keeps a test scene in a subfolder " +
                    "from ever taking a chapter's key.");

                Assert.IsTrue(ResourceLoader.Exists(path), $"F{slot + 1} names '{path}', which does not exist.");
            }
        }

        /// <summary>
        /// The one mapping worth pinning by name. Chapter two is unreachable through the shipped flow -
        /// the title menu and the victory panel both hardcode GameplayScene - so this jump is currently
        /// the only way a human reaches the arena at all, and a silent reorder would take it away.
        /// </summary>
        [Test]
        public void SomeSlot_ReachesChapterTwo()
        {
            var found = false;

            for (var slot = 0; slot < GameplayDebugSceneJump.SlotCount && !found; slot++)
            {
                if (GameplayDebugSceneJump.TryResolveSlot(slot, out string path))
                    found = path.GetFile().GetBaseName() == "Chapter02_Orange";
            }

            Assert.IsTrue(found,
                "No function key reaches Chapter02_Orange. Nothing in the shipped flow loads that scene, so " +
                $"without a slot for it the arena cannot be played at all. Slots: {GameplayDebugSceneJump.DescribeSlots()}");
        }
    }
}
#endif
