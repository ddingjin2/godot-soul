using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// The road, once it is walked rather than described. Three things in this stretch break without
    /// saying anything: the save the next gate is entered on, a stance that quietly holds a row it was
    /// never meant to reach, and a chain with no end.
    ///
    /// Written during the QA review of 87092cc..9a8bf83 and NOT verified - nothing here has been run.
    /// </summary>
    /// <remarks>
    /// PORT: <c>Time.timeScale</c> is <see cref="GameClock.TimeScale"/>; scene loads go through the test
    /// runner rather than <c>SceneManager</c>; the boss fixture is a node graph (the behaviour is the
    /// <c>CharacterBody2D</c>, Health is a child) instead of a GameObject with components.
    /// <para>
    /// Unity's <c>WaitUntilRealtime</c> is gone: <c>TestContext.Runner.WaitUntil</c> already polls on the
    /// wall clock and on <c>ProcessFrame</c>, both of which keep running at the zero timescale the
    /// victory path sets.
    /// </para>
    /// <para>
    /// UNITS: nothing converts. Chapter indices, checkpoint indices, soul counts, chain steps and
    /// durations are all unscaled, and the boss fixture's authored metres are scaled by
    /// <c>ScaleToPixels</c> exactly as <c>RainbowChapterBossData.Load</c> does it.
    /// </para>
    /// <para>
    /// <c>PlayerPrefs</c> is a file that outlives the process, so the slot is cleared in <c>[SetUp]</c>
    /// too - Unity could assume a fresh in-memory store and this cannot.
    /// </para>
    /// </remarks>
    public sealed class GameplayChapterProgressionRegressionTests
    {
        private const string ChapterTwoSceneName = "Chapter02_Orange";
        private const string ChapterThreeSceneName = "Chapter03_Yellow";
        private const string GameplaySceneName = "GameplayScene";

        private float _startTimeScale;
        private RainbowChapterBossBehaviour _boss;

        [SetUp]
        public void SetUp()
        {
            _startTimeScale = GameClock.TimeScale;

            // This fixture writes the real save slot, and that slot is a file that survives the run.
            PlayerPrefs.DeleteAll();
        }

        [TearDown]
        public void TearDown()
        {
            GameClock.TimeScale = _startTimeScale;
            GameSave.LoadOnNextGameplayStart = false;

            if (GodotObject.IsInstanceValid(_boss))
                _boss.QueueFree();
            _boss = null;

            GameSave.Clear();
            PlayerPrefs.DeleteAll();
        }

        /// <summary>
        /// Passing a gate must carry the run that passed it. The victory panel writes the slot and then
        /// arms <see cref="GameSave.LoadOnNextGameplayStart"/>, so whatever is in that slot is what the
        /// player owns on the other side of the gate - and
        /// <see cref="GameplaySaveBridge.Apply"/> sets souls unconditionally, with no "unset" sentinel of
        /// the kind health and humanity have.
        ///
        /// The failure is silent and it is on the ordinary path: a player who has not rested since the
        /// souls were earned walks into the next chapter with the balance of the last rest, which on a
        /// first playthrough is a slot that does not exist yet and therefore zero. Nothing on screen says
        /// the souls are gone.
        /// </summary>
        [Test]
        public async Task AdvancingAGate_WritesTheRunItWasFinishedWith_NotTheLastRest()
        {
            // No slot at all, which is where a first playthrough that has never rested stands. Clear()
            // disarms the load flag too, so the chapter-two bootstrap below starts fresh either way.
            GameSave.Clear();
            GameSave.LoadOnNextGameplayStart = false;

            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(ChapterTwoSceneName));

            // The entry shot is not waited out on purpose: nothing here reads state a cutscene touches,
            // and the victory controller and the wallet both exist the moment the bootstrap returns.
            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "Chapter two has to spawn a player before this can mean anything.");

            // The wallet is a sibling node under the actor root - the motor body - not a child of the
            // controller, so the lookup walks up one level. Unity's components were all on one object.
            var wallet = player.GetComponentInParent<SoulsWallet>();
            Assert.NotNull(wallet, "The player carries the wallet the save is captured from.");

            // What the chapter was finished on: a boss kill plus whatever the approach paid out.
            wallet.SetSouls(742);

            var victory = SceneQuery.FindFirst<GameplayVictoryController>();
            Assert.NotNull(victory, "The victory panel's buttons hang off this controller.");

            // The kill, not a button. The gate is recorded when the boss goes down, so a player who beats
            // it and walks to the title has still passed it - which is why this drives the boss's health
            // rather than calling the panel's action.
            var boss = SceneQuery.FindFirst<RainbowChapterBossBehaviour>();
            Assert.NotNull(boss, "Chapter two has to stand its boss up before it can be beaten.");
            boss.GetComponent<Health>().ApplyDamage(99999f, Vector2.Right);

            Assert.IsTrue(await TestContext.Runner.WaitUntil(() => GameSave.Read() != null, 6f),
                "Beating the boss has to leave a slot, or Continue has nothing to resume.");

            GameSaveData written = GameSave.Read();
            Assert.NotNull(written, "Beating the boss has to leave a slot, or Continue has nothing to resume.");
            Assert.AreEqual(ChapterThreeSceneName, written.chapterScene,
                "Beating chapter two opens chapter three; the slot is the only record that it was opened.");

            Assert.AreEqual(742, written.souls,
                "The gate has to be entered on the run that passed it. Re-reading the old slot and only " +
                "changing chapterScene hands the next chapter the balance of the last rest - zero on a " +
                "run that never rested - and GameplaySaveBridge.Apply then writes that over the wallet.");

            // The victory path froze time; put it back before loading, or the next scene opens frozen.
            GameClock.TimeScale = 1f;
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));
        }

        /// <summary>
        /// Passing a gate has to leave the next chapter at its own beginning.
        /// <see cref="GameSaveData.checkpointIndex"/> and <see cref="GameSaveData.shortcutOpened"/> are
        /// chapter-scoped - the field's own doc comment says so - and the victory path captures them from
        /// the chapter being left and then points <c>chapterScene</c> at the next one.
        /// <c>GateTravelZone.Travel</c> clears both when it moves the record; beating the boss, which is
        /// the ordinary way a chapter changes, does not.
        ///
        /// The failure is silent and it is the whole level: the next chapter is entered at bonfire 2 with
        /// its shortcut already open, so a player who beat chapter two walks into chapter three most of
        /// the way through it. Latent only because every shipped layout still names one checkpoint and no
        /// shortcut - which is exactly what Phase 2 is about to change.
        /// </summary>
        [Test]
        public async Task PassingAGate_LeavesTheNextChapterAtItsOwnStart()
        {
            // A run that rested at chapter two's third bonfire and opened its one shortcut.
            GameSave.Write(new GameSaveData
            {
                chapterScene = ChapterTwoSceneName,
                checkpointIndex = 2,
                shortcutOpened = true,
                souls = 120
            });

            // Off, so the arena builds at its own start rather than resuming at the recorded bonfire.
            // This test is about what the gate writes, not about where the chapter opens.
            GameSave.LoadOnNextGameplayStart = false;

            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(ChapterTwoSceneName));

            var boss = SceneQuery.FindFirst<RainbowChapterBossBehaviour>();
            Assert.NotNull(boss, "Chapter two has to stand its boss up before the gate can be passed.");

            // The kill records the gate, the same as the sibling test above: the panel's buttons are not
            // what opens the next chapter.
            boss.GetComponent<Health>().ApplyDamage(99999f, Vector2.Right);

            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => GameSave.Read()?.chapterScene == ChapterThreeSceneName, 6f),
                "Beating chapter two has to record chapter three as the open gate.");

            GameSaveData written = GameSave.Read();
            Assert.AreEqual(ChapterThreeSceneName, written.chapterScene, "Fixture guard: the record moved on to the next gate.");

            Assert.AreEqual(-1, written.checkpointIndex,
                "The bonfire index belongs to the chapter it was recorded in. Carried across the gate it names " +
                "a different place on the far side, and the next chapter resumes deep inside itself with the " +
                "walk to the boss already spent.");
            Assert.IsFalse(written.shortcutOpened,
                "The shortcut belongs to the chapter its door is in. Carried across the gate the next chapter " +
                "opens with a one-way door it has not been shown yet.");

            // The victory path froze time; put it back before loading, or the next scene opens frozen.
            GameClock.TimeScale = 1f;
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));
        }

        /// <summary>
        /// A stance is only a stance if it takes attacks away.
        /// <c>RainbowChapterBossBehaviour.AttacksInCurrentStance</c> treats a row with an empty
        /// <c>stanceId</c> as reachable in <em>every</em> stance, so one unstanced row on a rotating boss
        /// is a row that never leaves the rotation.
        ///
        /// Chapter eight is where this bites: <c>the_waking</c> is authored as the payoff of
        /// <c>weight_of_certainty</c>'s chain and carries no stance, so the ordinary picker also reaches
        /// it from all seven stances - the 32-damage finisher becomes every other swing from the first
        /// exchange. Nothing errors; the fight is simply not the one that was authored.
        /// </summary>
        /// <remarks>
        /// PORT: Unity walked Build Settings by index. There is no build scene list here, so the sweep
        /// runs over <see cref="ChapterRoute.Scenes"/> - the same set, derived from <c>res://Scenes/</c>.
        /// </remarks>
        [Test]
        public void EveryAttackOfAStanceRotatingBoss_NamesAStance()
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            var checkedBosses = 0;

            foreach (string sceneName in ChapterRoute.Scenes())
            {
                GameplaySceneDefaults scene = GameplaySceneDefaults.Create();
                catalog.SceneLayoutFor(sceneName)?.ApplyTo(scene);

                RainbowChapterBossData boss = catalog.ChapterBoss(scene.BossDataFile);
                if (boss == null || !boss.RotatesStances)
                    continue;

                checkedBosses++;

                foreach (BossAttackProfile attack in boss.Attacks)
                {
                    // A chain-only row is never picked by the stance picker at all, so it can belong to
                    // no stance without living in all of them. That is how chapter eight's finisher is
                    // the payoff of a combo rather than every other swing.
                    if (attack.ChainOnly)
                        continue;

                    Assert.IsNotEmpty(attack.StanceId,
                        $"{boss.BossName}'s '{attack.AttackId}' names no stance and is not chain-only. A stanceless " +
                        "row is reachable from every stance, so it stays in the rotation the whole fight instead " +
                        "of belonging to one shape - which is the opposite of what a stance rotation is for.");
                }
            }

            Assert.Greater(checkedBosses, 0,
                "No shipped boss rotates stances, so this checked nothing. If stances were removed, remove this too.");
        }

        /// <summary>
        /// The cap that stops a fight from having no punish window at all. A row that chains to itself is
        /// a one-word authoring mistake and it produces a boss that never stops swinging;
        /// <c>MaxChainSteps</c> is the only thing standing between that mistake and an unbeatable fight,
        /// and nothing else exercises it - the shipped chains all end on a row that names no successor,
        /// so they would stop with the cap deleted.
        /// </summary>
        [Test]
        public async Task AnAttackThatChainsToItself_StopsAtTheChainCap()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(SelfChainJson());

            var started = 0;
            // UnityEvent.AddListener is a C# event subscription here.
            boss.OnAttackRequested += () => started++;

            Assert.IsTrue(boss.RequestAttack(boss.BossData.Attacks[0]), "The opener has to run before it can loop.");

            // Five swings is the cap: four queued links on top of the one that started it. Long enough for
            // a sixth to have fired if the counter had stopped counting.
            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(
                    () => started >= 5 && !boss.IsAttackRunning && !boss.HasQueuedChain, 6f),
                "A self-chaining row should reach the cap and then stop; it never stopped.");

            await TestContext.Runner.Seconds(1f);

            Assert.AreEqual(5, started,
                "MaxChainSteps allows four links after the opener. More than that is a boss that chains " +
                "forever and a fight with no window to answer in.");
            Assert.IsFalse(boss.HasQueuedChain, "Nothing may still be queued once the cap has been hit.");
        }

        /// <summary>
        /// One row that names itself as its own next link. No player is built: the fixture drives the
        /// opener by hand, and a fixture player with no floor falls out of range inside a second.
        /// </summary>
        private static string SelfChainJson()
        {
            return "{\"maxHealth\":200,\"moveSpeed\":2,\"detectionRange\":8,\"attackRange\":1.5," +
                   "\"phaseTwoHealthThreshold\":0.5,\"phaseTwoSpeedMultiplier\":1,\"phaseTwoCooldownMultiplier\":1," +
                   "\"bossName\":\"SelfChainFixture\",\"chapterName\":\"Test Chapter\"," +
                   // Long recovery on purpose. Links skip the recovery - the chain has its own beat - but
                   // the fresh pick after the chain ends does not, so three seconds keeps the boss's next
                   // ordinary swing well outside the window this measures. Without it the cap looks
                   // broken: the counter resets when the chain ends and the same row is simply chosen
                   // again, which is a boss repeating itself, not a chain with no end.
                   "\"attacks\":[{\"attackId\":\"loop_swing\",\"damage\":5,\"knockback\":0," +
                   "\"telegraphTime\":0.1,\"activeTime\":0.1,\"recoveryTime\":3.0,\"range\":1.5," +
                   "\"forwardOffset\":0.5,\"phaseTwoWeight\":1,\"chainNextAttackId\":\"loop_swing\"," +
                   "\"chainDelay\":0.1,\"chainDelayPhaseTwoScale\":1}]}";
        }

        private RainbowChapterBossBehaviour BuildBoss(string json)
        {
            // Unity's JsonUtility.FromJsonOverwrite onto a ScriptableObject. The two calls after it are
            // what RainbowChapterBossData.Load does for an authored file: OnValidate stands in for the
            // editor hook, ScaleToPixels turns the authored metres (ranges, speeds) into pixels. The
            // times, damages and chain fields this fixture asserts on are left unscaled by both.
            var data = new RainbowChapterBossData();
            JsonData.FromJsonOverwrite(json, data);
            data.OnValidate();
            data.ScaleToPixels();

            // The behaviour IS the body in this port, so there is no Rigidbody2D to set kinematic - the
            // fixture simply has no floor, and gravity moving it costs nothing because every assertion
            // below is about attack bookkeeping rather than position.
            _boss = new RainbowChapterBossBehaviour { Name = "ChainCapFixture" };

            // Before it enters the tree: the behaviour's _Ready reads the health it is about to resize,
            // and a child is always ready before its parent.
            _boss.AddComponent<Health>();

            TestContext.Runner.AddChild(_boss);
            _boss.GlobalPosition = Vector2.Zero;
            _boss.SetBossData(data);

            // The chain only ticks past the intro gate, and a boss that never sees a player never leaves it.
            _boss.SkipIntro();
            return _boss;
        }
    }
}
