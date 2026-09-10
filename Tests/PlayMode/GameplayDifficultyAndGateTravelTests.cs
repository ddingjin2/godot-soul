using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// The three things the difficulty and gate-travel pass added that fail silently: a gate that closes
    /// behind the player, a rest that erases the road, and a difficulty that changes no number at all.
    /// Every one of them looks like a working game from the inside.
    ///
    /// PlayerPrefs is the save store, so these write the real slot and clear it again - a leftover slot
    /// would hand the next test a Continue it did not ask for.
    /// </summary>
    /// <remarks>
    /// PORT: the two synthetic actors are node graphs rather than <c>GameObject</c>s - the actor root is
    /// a <see cref="Node2D"/> and every "component" is a child node added with
    /// <c>AddComponent&lt;T&gt;()</c>, which is what <c>GetComponent&lt;T&gt;()</c> reads back.
    /// <c>DestroyImmediate</c> is <c>Free()</c>, the immediate half of Godot's pair.
    /// <para>
    /// UNITS: nothing here converts. Chapter indices, gate counts and difficulty multipliers are
    /// unscaled, and the one direction used - <c>Vector2.Right</c> - is horizontal, so no vertical sign
    /// flips.
    /// </para>
    /// <para>
    /// <c>PlayerPrefs</c> is a file under <c>user://</c> that outlives the process, so unlike Unity this
    /// clears it in <c>[SetUp]</c> as well: a slot left behind by an earlier headless run would decide
    /// what <see cref="GameSave.Read"/> answers below.
    /// </para>
    /// </remarks>
    public sealed class GameplayDifficultyAndGateTravelTests
    {
        private Node2D _target;
        private Node2D _player;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteAll();
            DifficultySettings.Set(Difficulty.Normal, 0);
        }

        [TearDown]
        public void TearDown()
        {
            if (GodotObject.IsInstanceValid(_target))
                _target.Free();
            if (GodotObject.IsInstanceValid(_player))
                _player.Free();

            _target = null;
            _player = null;

            GameSave.Clear();
            DifficultySettings.Set(Difficulty.Normal, 0);
            PlayerPrefs.DeleteAll();
        }

        /// <summary>
        /// A slot written before <c>furthestChapter</c> existed reads 0 for it. Trusting that number would
        /// lock a player who had walked to chapter five back down to the first gate, with no error and no
        /// way to tell it from a fresh save.
        /// </summary>
        [Test]
        public void ASlotWithNoFurthestField_StillUnlocksEveryGateUpToTheOneItNames()
        {
            string[] scenes = ChapterRoute.Scenes();
            if (scenes.Length < 3)
                Assert.Ignore("The project carries fewer than three chapters, so there is no road to walk back down.");

            var save = new GameSaveData { chapterScene = scenes[2] };

            Assert.AreEqual(2, ChapterRoute.FurthestIndex(save),
                "The recorded scene is the only evidence an older slot has of how far it got.");
            Assert.AreEqual(3, ChapterRoute.Unlocked(save).Length,
                "Three gates have been reached, so three are open.");
        }

        /// <summary>
        /// The point of the portal: travelling back moves <c>chapterScene</c> backwards, and the gates
        /// ahead have to stay open. Without the separate furthest field, one trip home would undo the run.
        /// </summary>
        [Test]
        public void TravellingBackDownTheRoad_LeavesTheGatesAheadOpen()
        {
            string[] scenes = ChapterRoute.Scenes();
            if (scenes.Length < 4)
                Assert.Ignore("The project carries fewer than four chapters.");

            var save = new GameSaveData { chapterScene = scenes[0], furthestChapter = 3 };

            Assert.AreEqual(3, ChapterRoute.FurthestIndex(save));
            Assert.AreEqual(4, ChapterRoute.Unlocked(save).Length,
                "Standing at the first gate does not close the three that were already passed.");
        }

        /// <summary>
        /// Finishing the road is what earns Hard, and Easy is not finishing it. The rule lives in
        /// ChapterRoute so the victory panel, a portal and anything else that ends a run cannot disagree.
        /// </summary>
        [Test]
        public void FinishingTheRoad_UnlocksHardOnNormal_AndNotOnEasy()
        {
            string[] scenes = ChapterRoute.Scenes();
            if (scenes.Length == 0)
                Assert.Ignore("The project carries no chapters.");

            string last = scenes[scenes.Length - 1];

            var normal = new GameSaveData { difficulty = (int)Difficulty.Normal };
            ChapterRoute.RecordGatePassed(normal, last);
            Assert.IsTrue(normal.hardUnlocked, "Finishing the road on Normal is exactly what opens Hard.");
            Assert.AreEqual(last, normal.chapterScene, "The end of the road records itself; there is no gate after it.");

            var easy = new GameSaveData { difficulty = (int)Difficulty.Easy };
            ChapterRoute.RecordGatePassed(easy, last);
            Assert.IsFalse(easy.hardUnlocked, "Easy finishes the story and earns nothing.");
        }

        /// <summary>
        /// A capture builds its record from the live player, and the road is not something the player
        /// carries. Before the merge, resting at a bonfire wrote a slot with furthestChapter 0 and
        /// hardUnlocked false - the whole campaign's progress, gone to a rest.
        /// </summary>
        [Test]
        public void Capture_KeepsTheProgressThePlayerIsNotCarrying()
        {
            GameSave.Write(new GameSaveData { furthestChapter = 3, hardUnlocked = true, souls = 10 });

            _player = new Node2D { Name = "CaptureTestPlayer" };
            TestContext.Runner.AddChild(_player);

            Health health = _player.AddComponent<Health>();
            health.SetMaxHealth(100f);
            health.SetHealth(80f);

            var context = new GameplayPlayerContext(_player, null, health, null, null, null, null, null);

            GameSaveData captured = GameplaySaveBridge.Capture(context);

            Assert.AreEqual(3, captured.furthestChapter, "A rest must not walk the road back to the first gate.");
            Assert.IsTrue(captured.hardUnlocked, "A rest must not take Hard back off the title menu.");
            Assert.AreEqual(80f, captured.health, 0.01f, "The run's own state still comes from the live player.");
        }

        /// <summary>
        /// The two multipliers that decide what a hit costs. Both are the player's: an enemy has no sin
        /// and no difficulty, and scaling its incoming damage would make every fight easier on Easy in
        /// both directions at once.
        /// </summary>
        [Test]
        public void DifficultyScalesWhatThePlayerTakes_AndNotWhatTheEnemyTakes()
        {
            DifficultySettings.Set(Difficulty.Easy, 0);

            // Health and damage are unscaled counts, so 100 - 10 * 0.7 = 93 ports across verbatim.
            Health player = BuildDamageable("DifficultyTestPlayer", withSins: true);
            CombatResolver.Resolve(Hit(player.GetParent<Node2D>(), 10f));
            Assert.AreEqual(93f, player.CurrentHealth, 0.01f,
                "Easy takes 70% of an incoming hit; the resolver is the only place that can apply it.");

            _target.Free();
            _target = null;

            Health enemy = BuildDamageable("DifficultyTestEnemy", withSins: false);
            CombatResolver.Resolve(Hit(enemy.GetParent<Node2D>(), 10f));
            Assert.AreEqual(90f, enemy.CurrentHealth, 0.01f,
                "An actor with no sin controller is not the player and takes its hit unscaled.");
        }

        /// <summary>
        /// The weights live in DifficultyTuning.json, and a file nothing reads is worse than a literal -
        /// it looks like tuning. So this reads the file directly and checks the settings answer with
        /// its numbers, not with a fallback that happens to match.
        /// </summary>
        [Test]
        public void DifficultyTuningJson_LoadsAndDrivesTheMultipliers()
        {
            DifficultyTuningData file = Res.LoadJson<DifficultyTuningData>("Design/" + DifficultyTuningData.FileName);
            Assert.NotNull(file, "DifficultyTuning.json should exist and parse.");

            DifficultySettings.Set(Difficulty.Hard, 2);
            Assert.AreEqual(file.hardPlayerDamageTaken, DifficultySettings.PlayerDamageTakenMultiplier, 0.0001f,
                "Hard's damage weight should be the file's, not a literal.");
            Assert.AreEqual(file.hardEnemyHealth * (1f + 2f * file.newGamePlusEnemyHealthPerCycle),
                DifficultySettings.EnemyHealthMultiplier, 0.0001f,
                "New Game+ stacks the file's per-cycle bonus on top of Hard's health weight.");
        }

        /// <summary>
        /// The travel panel names chapters, not scenes. The resolution is a chain of string references -
        /// scene name to SceneLayout to bossDataFile to chapterName - and every link fails the same soft
        /// way: the row falls back to the scene name and the panel silently reads like a debug menu.
        /// </summary>
        [Test]
        public void EveryGate_ShowsItsChapterNameRatherThanItsSceneName()
        {
            string[] gates = ChapterRoute.Scenes();
            if (gates.Length == 0)
                Assert.Ignore("The project carries no chapters.");

            string[] titles = GateTravelZone.TitlesFor(gates);
            Assert.AreEqual(gates.Length, titles.Length, "One title per gate, in the same order.");

            for (var i = 0; i < gates.Length; i++)
            {
                Assert.IsNotEmpty(titles[i], $"{gates[i]} resolved to an empty row.");
                Assert.AreNotEqual(gates[i], titles[i],
                    $"{gates[i]} fell back to its own scene name, so either its layout names no bossDataFile " +
                    "or that file carries no chapterName.");
            }
        }

        /// <summary>
        /// Hard is a reward. A slot that names it without having earned it - PlayerPrefs is editable from
        /// outside the game - is refused rather than honoured.
        /// </summary>
        [Test]
        public void ApplyingASlot_RefusesAHardItHasNotEarned()
        {
            DifficultySettings.Apply(new GameSaveData { difficulty = (int)Difficulty.Hard, hardUnlocked = false });
            Assert.AreEqual(Difficulty.Normal, DifficultySettings.Current);

            DifficultySettings.Apply(new GameSaveData { difficulty = (int)Difficulty.Hard, hardUnlocked = true });
            Assert.AreEqual(Difficulty.Hard, DifficultySettings.Current);
        }

        private Health BuildDamageable(string name, bool withSins)
        {
            // In the tree, because Health reads its max in _Ready and a detached node never runs one.
            _target = new Node2D { Name = name };
            TestContext.Runner.AddChild(_target);

            Health health = _target.AddComponent<Health>();
            health.SetMaxHealth(100f);
            health.SetHealth(100f);

            _target.AddComponent<DamageReceiver>().Initialize(health);

            if (withSins)
                _target.AddComponent<SinResonanceController>();

            return health;
        }

        private static DamageRequest Hit(Node2D target, float damage)
        {
            // Unity's Transform.position is GlobalPosition; Vector2.right is Vector2.Right and needs no
            // sign flip, being horizontal.
            return new DamageRequest(null, target, target.GlobalPosition, Vector2.Right, damage, 0f, DamageType.Standard);
        }
    }
}
