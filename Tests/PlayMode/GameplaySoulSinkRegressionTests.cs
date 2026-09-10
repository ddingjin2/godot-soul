using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// The soul sink, at the seams where a purchase disappears without anything saying so: the save round
    /// trip, the load that runs over it, and the cap.
    ///
    /// No number from <c>ProgressionTuning.json</c> is written down here. That file is the designer's and
    /// is due another pass after the Phase 2 stopwatch, so a fixture that hard-coded 12 health a level
    /// would go red on a tuning edit that broke nothing. Where a curve is needed the fixture injects its
    /// own through <see cref="PlayerProgression.Bind"/>; where the shipped one is what matters the test
    /// reads it back off <see cref="PlayerProgression.Tuning"/> and asserts relative to it.
    ///
    /// Written during the QA review of the [[PlaytimePlan]] Phase 1 pass and NOT verified - nothing here
    /// has been run.
    /// </summary>
    /// <remarks>
    /// PORT NOTES.
    /// <list type="bullet">
    /// <item>UNITS: health, souls and the level curve are unscaled by the port, so every number below is
    /// the Unity number. Nothing here is a distance.</item>
    /// <item><c>PlayerProgression</c> is a plain C# object hanging off <see cref="PlayerController2D"/>
    /// here, not a component, so the synthetic players are rooted on a controller when they need one and
    /// on a bare <see cref="Node2D"/> when the test is about not having one. "No progression component"
    /// becomes "EnsureOn hands back null".</item>
    /// <item><c>ScriptableObject.CreateInstance</c> is <c>new</c> - <see cref="ProgressionTuningData"/> is
    /// a <see cref="Resource"/>, which is reference-counted, so there is nothing to destroy in teardown.
    /// </item>
    /// <item>The synthetic players are deliberately never added to the tree, exactly as the Unity fixture
    /// never gave its <c>GameObject</c> a scene: nothing here needs a frame, and a bare controller with no
    /// motor under it would run its <c>_Ready</c> against half a player.</item>
    /// <item><c>SceneManager.GetActiveScene().name</c> is <c>GameplayBuildShim.ActiveSceneName</c>, which
    /// is the same string <see cref="GameplaySaveBridge.Capture"/> reads - they have to agree or the
    /// "same chapter" test inside Capture stops matching.</item>
    /// </list>
    /// </remarks>
    public sealed class GameplaySoulSinkRegressionTests
    {
        private const string GameplaySceneName = "GameplayScene";

        private Node2D _player;
        private Node2D _reloaded;
        private ProgressionTuningData _tuning;
        private GameSaveData _existingSave;
        private Difficulty _startDifficulty;
        private int _startNewGamePlus;

        [SetUp]
        public void SetUp()
        {
            // Capture and Apply both read the real PlayerPrefs slot, so put back whatever was in it.
            _existingSave = GameSave.Read();

            // Loading a slot applies its difficulty to a process-wide static.
            _startDifficulty = DifficultySettings.Current;
            _startNewGamePlus = DifficultySettings.NewGamePlus;

            // A curve this fixture owns: a cap it can reach in three purchases, a price it can afford,
            // and growth steep enough that "the next level costs more" is a real assertion.
            _tuning = new ProgressionTuningData
            {
                maxLevelPerStat = 3,
                vitalityPerLevel = 25f,
                endurancePerLevel = 10f,
                strengthPerLevel = 5f,
                resolvePerLevel = 5f,
                baseCost = 10,
                costGrowth = 1.5f,
            };
        }

        [TearDown]
        public void TearDown()
        {
            // Free, not QueueFree: these nodes are never in the tree, and the next test builds its own.
            if (_player != null && GodotObject.IsInstanceValid(_player))
            {
                _player.Free();
            }

            if (_reloaded != null && GodotObject.IsInstanceValid(_reloaded))
            {
                _reloaded.Free();
            }

            _player = null;
            _reloaded = null;
            _tuning = null;

            GameSave.LoadOnNextGameplayStart = false;
            DifficultySettings.Set(_startDifficulty, _startNewGamePlus);

            if (_existingSave != null)
            {
                GameSave.Write(_existingSave);
            }
            else
            {
                GameSave.Clear();
            }
        }

        /// <summary>
        /// A level bought lives on the progression and nowhere else until something writes it down. If
        /// <see cref="GameplaySaveBridge.Capture"/> carries the four level fields forward from the slot
        /// instead of reading the live player, the next write puts back the levels the player had before
        /// the purchase - the souls are spent, the level is gone, and nothing errors.
        ///
        /// The return leg is the same failure mirrored: <see cref="GameplaySaveBridge.Apply"/> has to
        /// raise the maximums before it restores health, because <c>Health.SetMaxHealth</c> only clamps
        /// current health downwards. Restore first and the health the player paid for is trimmed to the
        /// unlevelled ceiling and never grows back.
        /// </summary>
        [Test]
        public void LevelsBought_SurviveTheSaveRoundTrip_AndAreAppliedBeforeHealthIsRestored()
        {
            const float BaseMaxHealth = 100f;

            // A slot that knows about no levels at all, which is what every slot written before the
            // purchase says. A carry-forward capture would hand these zeros straight back.
            GameSave.Write(new GameSaveData
            {
                chapterScene = GameplayBuildShim.ActiveSceneName,
                souls = 999,
                levelVitality = 0
            });

            _player = BuildPlayer("SoulSinkPlayer", BaseMaxHealth, 500, withProgression: true);
            var health = _player.GetComponent<Health>();
            var wallet = _player.GetComponent<SoulsWallet>();

            PlayerProgression progression = PlayerProgression.EnsureOn(_player);
            Assert.NotNull(progression, "A player rooted on the controller has to own a progression.");

            // Bound after the components hold their authored numbers, which is the order the spawner
            // uses. Binding earlier captures the wrong floor and every level is measured from it.
            progression.Bind(_tuning);

            Assert.IsTrue(progression.TryPurchase(PlayerStat.Vitality), "The purse covers the first level.");
            Assert.IsTrue(progression.TryPurchase(PlayerStat.Vitality), "The purse covers the second level.");
            Assert.AreEqual(2, progression.VitalityLevel, "Fixture guard: two levels were actually bought.");
            Assert.AreEqual(BaseMaxHealth + _tuning.vitalityPerLevel * 2f, health.MaxHealth, 0.01f,
                "Fixture guard: the injected curve has to move the ceiling, or the round trip below proves nothing.");

            GameSaveData captured = GameplaySaveBridge.Capture(ContextFor(_player));

            Assert.AreEqual(2, captured.levelVitality,
                "Capture has to read the levels off the live player rather than copy the slot's. Carried " +
                "forward, every level bought since the last write is paid for and then thrown away by the " +
                "write that follows it - the one place in the game where souls buy nothing at all.");
            Assert.AreEqual(wallet.Souls, captured.souls, "The spent purse is what the slot should hold.");

            // The far side of a Continue: a freshly spawned player, unlevelled, hurt, with the slot as the
            // only record that any of it happened.
            _reloaded = BuildPlayer("SoulSinkReloadedPlayer", BaseMaxHealth, 0, withProgression: true);
            var reloadedHealth = _reloaded.GetComponent<Health>();
            reloadedHealth.SetHealth(40f);

            GameplaySaveBridge.Apply(captured, ContextFor(_reloaded));

            PlayerProgression reloadedProgression = PlayerProgression.EnsureOn(_reloaded);
            Assert.AreEqual(2, reloadedProgression.VitalityLevel,
                "Apply is the only thing that puts a slot's levels back on a player. Without it they read " +
                "as zero and the next write puts those zeros over the record.");

            // Relative to whatever the shipped curve says, because the reloaded player binds the authored
            // file rather than this fixture's. A curve that gives nothing per level makes the ceiling
            // assertion vacuous, so it is only asked when there is something to see.
            if (reloadedProgression.Tuning.vitalityPerLevel > 0f)
            {
                Assert.Greater(reloadedHealth.MaxHealth, BaseMaxHealth,
                    "The levels were stored and never applied: the player owns two Vitality levels and still " +
                    "fights with the unlevelled maximum.");
            }

            Assert.AreEqual(Mathf.Min(captured.health, reloadedHealth.MaxHealth), reloadedHealth.CurrentHealth, 0.01f,
                "Health was restored against the unraised ceiling. SetMaxHealth only clamps downwards, so a " +
                "restore that runs before the levels are applied loses exactly what the souls were spent on.");
        }

        /// <summary>
        /// The other half of the same rule. A player object with no <see cref="PlayerProgression"/> behind
        /// it - a test fixture, an extractor run, anything built by hand - knows nothing about levels, so
        /// a capture from one must leave the slot's levels alone instead of reading four zeros off
        /// something that is not there.
        /// </summary>
        [Test]
        public void Capture_FromAPlayerWithNoProgression_KeepsTheLevelsTheSlotAlreadyHolds()
        {
            GameSave.Write(new GameSaveData
            {
                chapterScene = GameplayBuildShim.ActiveSceneName,
                souls = 40,
                levelVitality = 4,
                levelEndurance = 3,
                levelStrength = 2,
                levelResolve = 1
            });

            _player = BuildPlayer("NoProgressionPlayer", 100f, 40, withProgression: false);

            // Unity asked for the component. Progression is a plain object on the controller here, so the
            // same question is "does EnsureOn find one" - and a player with no controller has none.
            Assert.IsNull(PlayerProgression.EnsureOn(_player),
                "Fixture guard: this player must not carry a progression, or the fallback is never asked about.");

            GameSaveData captured = GameplaySaveBridge.Capture(ContextFor(_player));

            Assert.AreEqual(4, captured.levelVitality, "A player that cannot answer must not be believed to have zero levels.");
            Assert.AreEqual(3, captured.levelEndurance, "A player that cannot answer must not be believed to have zero levels.");
            Assert.AreEqual(2, captured.levelStrength, "A player that cannot answer must not be believed to have zero levels.");
            Assert.AreEqual(1, captured.levelResolve, "A player that cannot answer must not be believed to have zero levels.");
        }

        /// <summary>
        /// Loading a slot must not write over the slot it is loading.
        /// <see cref="PlayerProgression.SetLevels"/> raises <c>OnLevelsChanged</c>, the bootstrap answers
        /// that event by capturing the whole run into the save, and mid-restore the player is a mixture:
        /// the levels are in, the purse is still the spawner's fresh zero. A capture taken there writes
        /// souls 0 into the record - and zero is a legitimate purse, so the next load hands it back
        /// without a word.
        ///
        /// It cannot be seen in the session it happens in: <c>Apply</c> keeps restoring from the record it
        /// already read, so the player walks around with the right souls above a slot that says otherwise.
        /// The bill arrives one Continue later. Two things stop it - the listener being subscribed after
        /// the restore, and <see cref="GameplaySaveBridge.IsApplying"/> - and this drives the real
        /// bootstrap so that removing either one is red.
        /// </summary>
        [Test]
        public async Task LoadingASlot_DoesNotWriteAnEmptyPurseOverIt()
        {
            const int BankedSouls = 777;

            GameSave.Write(new GameSaveData
            {
                chapterScene = GameplaySceneName,
                health = 55f,
                humanity = 60f,
                souls = BankedSouls,
                checkpointIndex = 0,
                levelVitality = 2
            });

            // The flag the title menu's Continue sets. It is what makes the bootstrap restore at all.
            GameSave.LoadOnNextGameplayStart = true;

            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(GameplaySceneName));

            // Through the player, not the first SoulsWallet in the tree: every enemy carries one too,
            // seeded with what killing it is worth, and the first one found is whichever the scene built
            // first.
            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, "The resumed scene has to spawn a player.");

            var wallet = player.GetComponentInParent<SoulsWallet>();
            Assert.NotNull(wallet, "The player carries the wallet the slot is restored into.");
            Assert.AreEqual(BankedSouls, wallet.Souls, "The restore has to hand the banked souls back to the player.");

            GameSaveData afterLoad = GameSave.Read();
            Assert.NotNull(afterLoad, "Loading a slot must not delete it.");
            Assert.AreEqual(BankedSouls, afterLoad.souls,
                "The load wrote over the slot it was restoring. Applying the levels raises OnLevelsChanged " +
                "while the purse is still the spawner's zero, and a capture taken from that state banks an " +
                "empty wallet - which the next Continue reads as a legitimately empty one.");
            Assert.AreEqual(2, afterLoad.levelVitality, "The levels the slot was holding have to survive their own restore.");
        }

        /// <summary>
        /// The two rules of the price that are the design rather than the numbers: it is driven by every
        /// level bought rather than by the one stat, so spreading points is never cheaper; and a capped
        /// stat has no price at all, so a purchase against it is refused before the wallet is touched.
        /// Both hold whatever the designer does to the curve.
        /// </summary>
        [Test]
        public void ThePrice_RisesWithEveryLevelBought_AndACappedStatCannotBeBought()
        {
            _player = BuildPlayer("SoulSinkCapPlayer", 100f, 100000, withProgression: true);
            var health = _player.GetComponent<Health>();
            var wallet = _player.GetComponent<SoulsWallet>();

            PlayerProgression progression = PlayerProgression.EnsureOn(_player);
            progression.Bind(_tuning);

            int firstLevelCost = progression.CostOf(PlayerStat.Vitality);
            Assert.AreEqual(firstLevelCost, progression.CostOf(PlayerStat.Endurance),
                "The first level costs the same whichever stat it goes into.");

            Assert.IsTrue(progression.TryPurchase(PlayerStat.Vitality), "The purse covers the first level.");
            Assert.Greater(progression.CostOf(PlayerStat.Endurance), firstLevelCost,
                "A level bought in one stat has to raise the price of the next one in every stat. Priced per " +
                "stat instead, four shallow stats are bought at the opening price forever.");

            for (int level = progression.VitalityLevel; level < _tuning.maxLevelPerStat; level++)
            {
                Assert.IsTrue(progression.TryPurchase(PlayerStat.Vitality), $"Level {level + 1} is still inside the cap.");
            }

            Assert.IsTrue(progression.IsAtCap(PlayerStat.Vitality), "Fixture guard: the stat is capped now.");
            Assert.AreEqual(0, progression.CostOf(PlayerStat.Vitality),
                "A capped stat has no price - zero is what tells the panel to show a cap instead of a number.");
            Assert.IsFalse(progression.CanPurchase(PlayerStat.Vitality), "A capped stat is not purchasable at any purse.");

            int soulsAtCap = wallet.Souls;
            float maxHealthAtCap = health.MaxHealth;

            Assert.IsFalse(progression.TryPurchase(PlayerStat.Vitality), "A purchase past the cap has to be refused.");
            Assert.AreEqual(soulsAtCap, wallet.Souls, "A refused purchase must not spend souls.");
            Assert.AreEqual(maxHealthAtCap, health.MaxHealth, 0.01f, "A refused purchase must not raise the stat.");
            Assert.AreEqual(_tuning.maxLevelPerStat, progression.VitalityLevel, "A refused purchase must not raise the level.");
        }

        /// <summary>
        /// The two components the sink actually reads: what it raises, and what it spends. Deliberately
        /// not a whole player - <c>PlayerProgression</c> skips every component it does not find, and the
        /// fewer parts this fixture carries the fewer ways it can fail for a reason that is not the test.
        /// </summary>
        /// <param name="withProgression">
        /// Roots the actor on a <see cref="PlayerController2D"/>, which is where a progression lives in
        /// this port. False builds the Unity fixture's "player that cannot answer": a bare node with the
        /// same two components and nothing that owns levels.
        /// </param>
        private static Node2D BuildPlayer(string name, float maxHealth, int souls, bool withProgression)
        {
            Node2D root = withProgression ? new PlayerController2D() : new Node2D();
            root.Name = name;

            var health = new Health();
            root.AddChild(health);
            health.SetMaxHealth(maxHealth);
            health.SetHealth(maxHealth);

            var wallet = new SoulsWallet();
            root.AddChild(wallet);
            wallet.SetSouls(souls);

            return root;
        }

        /// <summary>
        /// Unity's context took a <c>GameObject</c>; it takes the actor <see cref="Node"/> here. The
        /// nulls in the middle are the components this fixture deliberately does not build.
        /// </summary>
        private static GameplayPlayerContext ContextFor(Node2D player)
        {
            return new GameplayPlayerContext(
                player,
                null,
                player.GetComponent<Health>(),
                null,
                null,
                null,
                null,
                null,
                player.GetComponent<SoulsWallet>());
        }
    }
}
