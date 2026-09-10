using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;
using MyGame.Testing;
using UnityTestAgent.Agent;
using UnityTestAgent.Observation;

namespace MyGame.Tests
{
    /// <summary>
    /// The fighting half of the [[PlaytimePlan]] 40-minute budget, measured instead of calculated.
    /// <c>GameplayChapterTraversalTests</c> did the walking half and found the hand arithmetic wrong
    /// every time; the trash slice (8 min) and the boss slice (12 min) are still nothing but arithmetic,
    /// and this file is the stopwatch on them.
    ///
    /// Three questions, in the order they can be answered honestly:
    ///
    /// 1. <b>What does one trash enemy cost.</b> A 1v1 against each archetype in the shipped chapter-one
    ///    arena, from the moment that archetype's own <c>detectionRange</c> would notice the player to the
    ///    moment it dies - seconds, player health spent, souls paid.
    /// 2. <b>Could a boss attempt be 75 seconds.</b> Not by fighting one - the agent would lose - but from
    ///    both ends. The player's measured damage-per-second against a grunt, divided into every chapter
    ///    boss's authored health, is the floor an attempt could ever reach; and the seconds a player who
    ///    walks in and never defends survives is the ceiling on how long they get to try.
    /// 3. <b>What a chapter pays.</b> Every placement in every chapter against its archetype's
    ///    <c>soulReward</c>, which is what the level-up curve in <c>ProgressionTuning.json</c> was sized
    ///    against.
    ///
    /// <b>Read the numbers as an upper bound on a bad player, not a lower bound on a good one.</b>
    /// <see cref="SimpleCombatAgent"/> walks at the nearest live enemy, swings whenever it is in range, and
    /// rolls away from anything that telegraphs. It never heals, never parries, never uses a heavy attack,
    /// and it is built believing an attack costs 10 stamina when the authored cost is 20 - so it asks for
    /// swings the player cannot pay for and eats the gap. A human doing better makes every kill here
    /// shorter, so a trash budget that fits these seconds fits a real player's.
    ///
    /// <b>The output is the log, not the green.</b> Everything is logged under <c>[QA/Combat]</c> and
    /// <c>[QA/Souls]</c>. The assertions are only the fixture guards - the enemy under measurement is the
    /// one the design file describes, a fight actually happened - because a bound on a tuning number is a
    /// suite that reddens every time a designer edits a JSON.
    ///
    /// Written during the QA measurement pass of 2026-08-17 and NOT verified - nothing here has been run.
    /// </summary>
    /// <remarks>
    /// PORT NOTES.
    /// <list type="bullet">
    /// <item><b>Units.</b> Distances are pixels here. <c>DetectionMargin</c>, <c>BossStandOff</c> and the
    /// cluster's start line go through <see cref="World.U"/>; the archetype <c>detectionRange</c> read off
    /// the catalog is already scaled by <c>EnemyTuningData.ScaleToPixels</c>, so the two are in the same
    /// space. Health, damage, souls, stamina, seconds, dps and multipliers are unscaled by the port and are
    /// copied unchanged - which is why the logged dps is still "health per second" and comparable to the
    /// Unity figures quoted in the messages.</item>
    /// <item><b>Vertical.</b> Nothing here has a vertical expectation: the start lines and the stand-off are
    /// all on X, and the spawn Y comes straight off the layout, which is already Godot-space (+Y down).</item>
    /// <item><b>The input receiver is left ON.</b> Unity's driver bypassed input and called the controller
    /// directly, so every fixture had to switch <c>PlayerInputReceiver</c> off or a dead keyboard
    /// overwrote the agent's move input. <see cref="MyGameAgentInputDriver"/> presses real InputMap
    /// actions, so the receiver is the thing the agent talks to and switching it off would silence the
    /// agent completely. <c>TakeTheControls</c> is inverted for that reason.</item>
    /// <item><b>The physics step.</b> Unity's teardown restored <c>Time.fixedDeltaTime</c>. Godot's step is
    /// a project setting with no setter, and nothing in the port writes it, so there is nothing to restore.
    /// </item>
    /// <item><b><see cref="SimpleCombatAgent"/>'s attack range is passed explicitly.</b> Its default is
    /// still Unity's <c>1.5f</c> metres while the observation it compares against is in pixels - see
    /// <c>Scripts/Testing/Package/Agent/SimpleCombatAgent.cs:13</c>, where the sibling
    /// <c>SurvivingCombatAgent</c> was converted to <c>150f</c> and this one was not. Left at the default
    /// the agent never gets inside its own range and never swings, so this file hands it
    /// <c>World.U(1.5f)</c> rather than measuring a fight that cannot happen. The stamina defaults are
    /// left alone: believing a swing costs 10 against an authored 20 is the clumsiness the numbers are
    /// deliberately measured with.</item>
    /// </list>
    /// </remarks>
    public sealed class GameplayCombatCostTests
    {
        private const string ChapterOne = "GameplayScene";
        private const string ChapterEight = "Chapter08_White";

        /// <summary>Loaded back at the end of any test that opened a chapter, so the next fixture gets a clean arena.</summary>
        private const string FallbackSceneName = ChapterOne;

        /// <summary>
        /// How far inside its own detection range a fight starts. The encounter clock starts where the
        /// enemy would first notice the player, which is the only start line that means the same thing for
        /// a grunt that sees 5 units and a caster that sees 7.
        /// Unity metres -> pixels; the catalog's detectionRange is already scaled the same way.
        /// </summary>
        private static readonly float DetectionMargin = World.U(0.5f);

        /// <summary>
        /// The grunt's authored detection range, in pixels, used as the cluster's start line. Unity wrote
        /// the 5 inline; it is the same 5 metres MeleeGrunt.json carries.
        /// </summary>
        private static readonly float GruntDetectionRange = World.U(5f);

        /// <summary>What <see cref="SimpleCombatAgent"/>'s Unity default 1.5 metres is worth in pixels.</summary>
        private static readonly float AgentAttackRange = World.U(1.5f);

        /// <summary>
        /// Real seconds, not fast-forwarded: hit stop drives <see cref="GameClock.TimeScale"/> itself, so a
        /// fast-forwarded fight silently drops to real speed on the first hit that lands. Trash has 30-40
        /// health against a 20-damage swing, so a fight that reaches this was lost.
        /// </summary>
        private const float TrashFightBudgetSeconds = 60f;

        /// <summary>
        /// How many bodies count as a cluster, and how long one gets. Three is the middle of the two-to-
        /// four the chapters author; the budget is roughly four times the 4.82s a lone grunt costs, wide
        /// enough that fighting them together can be several times more expensive than fighting them
        /// one after another and still be measured rather than truncated.
        /// </summary>
        private const int ClusterSize = 3;
        private const float ClusterFightBudgetSeconds = 60f;

        /// <summary>Same clock, sized for a boss chewing through a 100-health player at ~20 a hit.</summary>
        private const float BossFightBudgetSeconds = 45f;

        /// <summary>
        /// Where the passive player stands when the boss's intro ends. Inside every authored attack range
        /// (chapter one's slash reaches 1.6, chapter eight's shortest row 1.8) so the fight starts at once,
        /// and the driver walks back into contact after every knockback. Unity metres -> pixels.
        /// </summary>
        private static readonly float BossStandOff = World.U(1.5f);

        /// <summary>How far off the cluster the player is parked before it notices them. Metres -> pixels.</summary>
        private static readonly float ClusterStepBack = World.U(4f);

        /// <summary>
        /// Long enough for both intro sequences: chapter one's is 0.5 + 0.8 s of beats plus a cutscene
        /// hold capped at 2.7. With <see cref="CutsceneDirector.SkipAll"/> set the hold lifts in a frame.
        /// </summary>
        private const float IntroWaitSeconds = 8f;

        private GameSaveData _existingSave;
        private bool _startLoadFlag;
        private bool _startSkipAll;
        private float _startTimeScale;
        private Difficulty _startDifficulty;
        private int _startNewGamePlus;
        private bool _openedAChapter;

        [SetUp]
        public void SetUp()
        {
            _existingSave = GameSave.Read();
            _startLoadFlag = GameSave.LoadOnNextGameplayStart;
            _startSkipAll = CutsceneDirector.SkipAll;
            _startTimeScale = GameClock.TimeScale;
            _startDifficulty = DifficultySettings.Current;
            _startNewGamePlus = DifficultySettings.NewGamePlus;
            _openedAChapter = false;

            // The entry shot takes PlayerInputReceiver for its length and hands it back when it ends, one
            // frame ahead of every physics step this file writes an input for. Without this the driven
            // player stands still and swings, which reads as a fight nobody can win.
            CutsceneDirector.SkipAll = true;

            // Every second below is a Normal-difficulty second at a clock of 1. Left to whatever the
            // previous fixture set, an enemy would carry 0.85x or 1.25x the health this file quotes off
            // the design file and the dps would be a fiction.
            DifficultySettings.Set(Difficulty.Normal, 0);
            GameClock.TimeScale = 1f;

            GameSave.Clear();
        }

        [TearDown]
        public async Task TearDownAndReleaseTheScene()
        {
            GameClock.TimeScale = _startTimeScale;
            CutsceneDirector.SkipAll = _startSkipAll;
            DifficultySettings.Set(_startDifficulty, _startNewGamePlus);

            if (_existingSave != null)
            {
                GameSave.Write(_existingSave);
            }
            else
            {
                GameSave.Clear();
            }

            // Off across the fallback load, whatever it was before: restoring the flag first would make
            // that bootstrap resume the slot that was just put back.
            GameSave.LoadOnNextGameplayStart = false;

            // Only the tests that opened one pay for the reload. The souls audit reads design files and
            // never loads a scene, and a scene load costs this suite seconds it does not have to spend.
            if (_openedAChapter)
            {
                await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(FallbackSceneName));
            }

            GameSave.LoadOnNextGameplayStart = _startLoadFlag;
        }

        // ------------------------------------------------------------------------------------------
        // A. What one trash enemy costs, per archetype
        // ------------------------------------------------------------------------------------------

        [Test]
        public Task MeleeGrunt_CostsThisMuchToKill() =>
            MeasureOneKill<MeleeGrunt>("MeleeGrunt", ArchetypeTuning.Grunt);

        [Test]
        public Task LeapingAttacker_CostsThisMuchToKill() =>
            MeasureOneKill<LeapingAttacker>("LeapingAttacker", ArchetypeTuning.Leaper);

        [Test]
        public Task RangedCaster_CostsThisMuchToKill() =>
            MeasureOneKill<RangedCaster>("RangedCaster", ArchetypeTuning.Caster);

        // ------------------------------------------------------------------------------------------
        // B. What a boss costs the player, from the other end
        // ------------------------------------------------------------------------------------------

        [Test]
        public Task ChapterOneBoss_KillsAnUndefendedPlayerThisFast() => MeasureBossIncoming(ChapterOne);

        [Test]
        public Task ChapterEightBoss_KillsAnUndefendedPlayerThisFast() => MeasureBossIncoming(ChapterEight);

        // ------------------------------------------------------------------------------------------
        // C. What a chapter pays
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Every chapter's soul income on a no-death clear, off the placements and the archetype rewards
        /// rather than off the 445 the level-up curve was sized against in 2026-08-16. No scene is loaded:
        /// the spawner reads exactly these two files, so the arithmetic here is the arithmetic it does.
        /// </summary>
        /// <remarks>
        /// The one real assertion is the placement floor. The shipped 36-unit corridor is three grunts, one
        /// leaper and one caster; if an array-isation or a layout edit ever dropped a chapter back to those
        /// defaults the income would fall by three quarters, the curve would stop being reachable, and
        /// nothing else in the suite would notice - the chapter would still load, still be walkable, still
        /// be beatable.
        /// </remarks>
        [Test]
        public void EveryChapter_PaysThisManySouls()
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            Assert.NotNull(catalog, "No tuning catalog, so there are no soul rewards to count.");
            Assert.NotNull(catalog.MeleeGrunt, "Resources/Design/MeleeGrunt.json has to load for this count to mean anything.");
            Assert.NotNull(catalog.LeapingAttacker, "Resources/Design/LeapingAttacker.json has to load for this count to mean anything.");
            Assert.NotNull(catalog.RangedCaster, "Resources/Design/RangedCaster.json has to load for this count to mean anything.");

            string[] road = ChapterRoute.Scenes();
            Assert.Greater(road.Length, 0, "res://Scenes carries no chapters, so there is no campaign to price.");

            var trashTotal = 0;
            var bossTotal = 0;
            var placementTotal = 0;
            var variantSpawns = 0;
            var report = new StringBuilder();

            // The thinnest chapter, remembered rather than asserted on the spot: the numbers are the whole
            // point of this test, and asserting inside the loop would throw away the seven chapters after
            // the first offender before anything reached the log.
            var thinnestCount = int.MaxValue;
            string thinnestScene = null;

            for (var i = 0; i < road.Length; i++)
            {
                string scene = road[i];
                GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(scene, null);

                int grunts = Placed(layout, layout.MeleeGruntSpawns);
                int leapers = Placed(layout, layout.LeapingAttackerSpawns);
                int casters = Placed(layout, layout.RangedCasterSpawns);
                int placements = grunts + leapers + casters;

                variantSpawns += VariantSpawns(layout.MeleeGruntSpawns)
                                 + VariantSpawns(layout.LeapingAttackerSpawns)
                                 + VariantSpawns(layout.RangedCasterSpawns);

                int trash = grunts * catalog.MeleeGrunt.soulReward
                            + leapers * catalog.LeapingAttacker.soulReward
                            + casters * catalog.RangedCaster.soulReward;

                BossRow boss = BossRow.For(layout, catalog);

                trashTotal += trash;
                bossTotal += boss.SoulReward;
                placementTotal += placements;

                report.AppendLine(
                    $"[QA/Souls] Ch{i + 1:D2} {scene}: {grunts}g+{leapers}l+{casters}c = {placements} placements " +
                    $"worth {trash} souls, boss {boss.Name} pays {boss.SoulReward} -> {trash + boss.SoulReward} a clear " +
                    $"(boss health {boss.MaxHealth:F0}).");

                if (placements < thinnestCount)
                {
                    thinnestCount = placements;
                    thinnestScene = scene;
                }
            }

            report.AppendLine(
                $"[QA/Souls] Campaign on a no-death clear: {placementTotal} placements paying {trashTotal} souls, " +
                $"bosses paying {bossTotal}, total {trashTotal + bossTotal}. " +
                $"Per-spawn dataFile overrides in the layouts: {variantSpawns} " +
                "(any above zero and the archetype rewards quoted here are not what those spawns pay).");

            GD.Print(report.ToString());

            Assert.Greater(thinnestCount, 5,
                $"{thinnestScene} places only {thinnestCount} enemies. The shipped 36-unit corridor is exactly five " +
                "(three grunts, a leaper, a caster), so this layout has collapsed back to the defaults - its soul " +
                "income drops by roughly three quarters and the ProgressionTuning curve stops being reachable, " +
                "with nothing else in the suite going red.");
        }

        // ------------------------------------------------------------------------------------------
        // The trash run
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// One archetype, alone, in the shipped chapter-one arena. Every other enemy is switched off so the
        /// seconds belong to this fight and not to whatever wandered into it, and the player starts at the
        /// archetype's own detection range so "encounter" means the same thing across the three.
        /// </summary>
        private async Task MeasureOneKill<T>(string label, ArchetypeTuning which) where T : EnemyStateMachine
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(ChapterOne, null);
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            Assert.NotNull(catalog, "No tuning catalog, so there is nothing to check the fought enemy against.");

            EnemyTuningData tuning = Read(which, catalog);
            Assert.NotNull(tuning, $"{label}'s design file has to load; without it there is no start line and no expected health.");

            await LoadChapter(ChapterOne);

            PlayerController2D player = RequirePlayer(ChapterOne);
            TakeTheControls(player);

            T target = FirstOnTheRoad<T>();
            Assert.NotNull(target,
                $"{ChapterOne} has to place at least one {label}; with none there is no 1v1 to time. Check " +
                "SceneLayout.json's spawn arrays.");

            int switchedOff = DisableEveryEnemyExcept(target);
            Assert.Greater(switchedOff, 0,
                $"Fixture guard: {ChapterOne} spawned nothing but the {label}, so this arena is not the shipped one.");

            var enemyHealth = target.GetComponent<Health>();
            Assert.NotNull(enemyHealth, $"A {label} has to carry Health or nothing here can be measured.");

            // Divided by what was actually fought, not by what the design file says. A placement naming its
            // own dataFile, or a difficulty multiplier left on by another fixture, moves one and not the
            // other - and a dps divided by a number nobody fought is worse than no dps. The design number
            // rides along in the log so the coordinator can see when the two have parted company.
            float fought = enemyHealth.MaxHealth;
            string tuningNote = Mathf.Abs(fought - tuning.maxHealth) < 0.01f
                ? "matches its design file"
                : $"does NOT match its design file's {tuning.maxHealth:F0} - a per-spawn dataFile or a non-Normal difficulty";

            var playerHealth = player.GetComponentInParent<Health>();
            var purse = player.GetComponentInParent<SoulsWallet>();
            Assert.NotNull(playerHealth, "The shipped player has to carry Health.");

            // Both terms are pixels: detectionRange came off the catalog already scaled, and the margin is
            // World.U(0.5f).
            float startLine = target.GlobalPosition.X - (tuning.detectionRange - DetectionMargin);
            PlaceOn(player, new Vector2(startLine, layout.PlayerSpawnPosition.Y));

            int soulsBefore = purse != null ? purse.Souls : 0;
            var fight = new Fight();

            await DriveTheFight(player, target, enemyHealth, playerHealth, TrashFightBudgetSeconds, fight);

            int soulsAfter = purse != null ? purse.Souls : 0;
            float encounterDps = fought / Mathf.Max(fight.Seconds, 0.001f);
            float contactDps = fight.ContactSeconds > 0.05f ? fought / fight.ContactSeconds : float.NaN;

            // detectionRange is logged back in metres so the figure reads the same as the Unity run's.
            GD.Print(
                $"[QA/Combat] {label} 1v1 in {ChapterOne}: killed={fight.Killed} in {fight.Seconds:F2}s from " +
                $"{(tuning.detectionRange - DetectionMargin) / World.Ppu:F1} units out (its own detectionRange " +
                $"{tuning.detectionRange / World.Ppu:F1}), " +
                $"first hit landed at {fight.FirstContactAt:F2}s so {fight.ContactSeconds:F2}s of contact. " +
                $"{fought:F0} enemy health as fought, which {tuningNote} -> {encounterDps:F2} dps over the encounter, " +
                $"{contactDps:F2} dps in contact. " +
                $"Player spent {fight.PlayerHealthLost:F0} health over {fight.PlayerHitsTaken} hit(s) " +
                $"(archetype hits for {tuning.attackDamage:F0}), ended on {fight.PlayerHealthLeft:F0}, died={fight.PlayerDied}. " +
                $"Souls {soulsBefore}->{soulsAfter} (+{soulsAfter - soulsBefore}, design says {tuning.soulReward}). " +
                "Driven by SimpleCombatAgent with its shipped stamina defaults: no heal, no parry, no heavy attack, " +
                "and it believes a swing costs 10 stamina against an authored 20 - so these seconds are a clumsy " +
                "player's ceiling, not a good player's floor.");

            LogBossProjection(label, encounterDps, contactDps);

            // Not a bound on the seconds - those are the deliverable and a designer may move them. This only
            // catches the fixture being a lie: a fight that never happened logs a dps computed from a full
            // budget of nothing.
            Assert.Less(GodotObject.IsInstanceValid(enemyHealth) ? enemyHealth.CurrentHealth : 0f, fought,
                $"The {label} finished the run on full health, so no fight was measured. The player never reached it, " +
                "the input never landed (check PlayerInputReceiver is still enabled and CutsceneDirector.SkipAll), or " +
                "the start line put them somewhere the enemy never noticed them.");
        }

        /// <summary>
        /// What a cluster costs, which is the number the 2026-08-17 density pass was sized without.
        /// Every placement in the eight chapters now sits in a group of two to four, and every figure in
        /// this file until now was one enemy alone - so the campaign's whole trash budget rested on
        /// multiplying a 1v1 by sixty.
        /// </summary>
        /// <remarks>
        /// Driven by <see cref="SurvivingCombatAgent"/> rather than the simple one, because the simple
        /// agent cannot answer this: pointed at chapter one's front it spent 143.9 of 150 seconds in
        /// contact, killed 1 of 61 bodies and died four times with three flasks untouched - it never
        /// drinks, and it only ever watches the closest enemy for a telegraph.
        /// </remarks>
        [Test]
        public async Task MeleeCluster_ThreeAtOnce_CostsThisMuch()
        {
            await LoadChapter(ChapterOne);

            PlayerController2D player = RequirePlayer(ChapterOne);
            TakeTheControls(player);

            MeleeGrunt anchor = FirstOnTheRoad<MeleeGrunt>();
            Assert.NotNull(anchor, $"{ChapterOne} has to place a MeleeGrunt for a cluster to be built around.");

            Node2D[] cluster = NearestLiveTo(anchor.GlobalPosition, ClusterSize);
            Assert.AreEqual(ClusterSize, cluster.Length,
                $"{ChapterOne} has to stand at least {ClusterSize} enemies near its first grunt; the authored " +
                "clusters are two to four, so a shorter one here means the layout has collapsed to the defaults.");

            DisableEveryEnemyExcept(cluster);

            var playerHealth = player.GetComponentInParent<Health>();
            float startHealth = playerHealth != null ? playerHealth.CurrentHealth : 0f;
            float pooled = 0f;
            foreach (Node2D body in cluster)
            {
                var h = body.GetComponent<Health>();
                if (h != null)
                {
                    pooled += h.MaxHealth;
                }
            }

            // Just outside the grunt's detection, so the cluster notices the player rather than the other
            // way round - the same convention the 1v1 runs use. Both terms are pixels.
            PlaceOn(player, new Vector2(
                cluster[0].GlobalPosition.X - (GruntDetectionRange - DetectionMargin),
                player.GlobalPosition.Y));

            var deathState = player.GetComponentInParent<DeathStateController>();
            var respawned = false;

            // UnityEvent.AddListener is a C# event subscription in the port.
            Action onRespawn = () => respawned = true;
            if (deathState != null)
            {
                deathState.OnRespawn += onRespawn;
            }

            var probe = new MyGameStateProbe(player, cluster);
            var driver = new MyGameAgentInputDriver(player);
            var brain = new SurvivingCombatAgent();

            float start = GameClock.Time;
            float deadline = start + ClusterFightBudgetSeconds;
            var cleared = 0;

            try
            {
                while (GameClock.Time < deadline)
                {
                    cleared = 0;
                    foreach (Node2D body in cluster)
                    {
                        if (!GodotObject.IsInstanceValid(body))
                        {
                            cleared++;
                        }
                    }

                    if (cleared >= cluster.Length || respawned || (playerHealth != null && playerHealth.IsDead))
                    {
                        break;
                    }

                    driver.Apply(brain.Decide(probe.Capture()).Action);
                    await TestContext.Runner.NextPhysicsFrame();
                }
            }
            finally
            {
                driver.ReleaseAll();
                if (deathState != null)
                {
                    deathState.OnRespawn -= onRespawn;
                }
            }

            float seconds = GameClock.Time - start;

            float taken = respawned
                ? startHealth
                : startHealth - Mathf.Max(0f, playerHealth != null ? playerHealth.CurrentHealth : 0f);

            GD.Print(
                $"[QA/Combat] A cluster of {cluster.Length} in {ChapterOne}, driven by SurvivingCombatAgent: " +
                $"cleared {cleared} of {cluster.Length} in {seconds:F2}s, player died={respawned}, took {taken:F0} " +
                $"health. {pooled:F0} pooled enemy health -> {pooled / Mathf.Max(seconds, 0.001f):F2} dps over the " +
                $"encounter. The 1v1 grunt figure is 4.82s, so {cluster.Length} alone would be " +
                $"{4.82f * cluster.Length:F1}s - anything above that is what fighting them together costs, and it " +
                "is the multiplier [[PlaytimePlan]]'s eight-minute trash slice has never had.");

            // Not a bound on the seconds - a designer moves those. This catches the driven player being
            // unable to fight a group at all, which is what the simple agent turned out to be.
            Assert.Greater(cleared, 0,
                $"The driven player cleared nothing from a cluster of {cluster.Length} in {seconds:F0}s. Either the " +
                "agent never reached them, the input never landed, or group combat is unwinnable for a driven run - " +
                "and every trash number in this file is a 1v1 that assumes otherwise.");
        }

        /// <summary>
        /// The floor a boss attempt could ever reach: the player's measured damage-per-second divided into
        /// each boss's authored health, with nothing in the way. A real attempt is longer by every second
        /// spent dodging, and [[PlaytimePlan]] budgets 75 - so this is the number that says whether 75 is
        /// generous or impossible, and it is the only way to ask without a player who can win.
        /// </summary>
        private static void LogBossProjection(string source, float encounterDps, float contactDps)
        {
            string[] road = ChapterRoute.Scenes();
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            if (road.Length == 0 || catalog == null)
            {
                return;
            }

            // A kill that landed one blow has no contact window to divide by. Falling back to the encounter
            // rate keeps the range readable instead of printing a projection of several thousand seconds.
            float contact = float.IsNaN(contactDps) ? encounterDps : contactDps;

            var line = new StringBuilder();
            line.Append($"[QA/Combat] Boss attempt floor projected from the {source} run ")
                .Append($"(encounter {encounterDps:F2} dps, contact {contact:F2} dps), against 75s budgeted: ");

            for (var i = 0; i < road.Length; i++)
            {
                BossRow boss = BossRow.For(GameplaySceneDefaults.CreateForScene(road[i], null), catalog);
                float slow = boss.MaxHealth / Mathf.Max(encounterDps, 0.001f);
                float fast = boss.MaxHealth / Mathf.Max(contact, 0.001f);
                line.Append($"Ch{i + 1:D2} {boss.Name} {boss.MaxHealth:F0}hp -> {fast:F0}-{slow:F0}s; ");
            }

            line.Append("uninterrupted swinging only - no dodge, no run-back, no death, and chapters four and eight ")
                .Append("heal themselves mid-fight, so their real floor is higher than the number here.");

            GD.Print(line.ToString());
        }

        // ------------------------------------------------------------------------------------------
        // The boss run
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The other end of the boss slice: how long the fight lets a player stay in it. The player walks
        /// into the boss and does nothing else - no dodge, no parry, no flask - so this is the shortest an
        /// attempt can be, and the pool it burns through is the one a real attempt spends more slowly.
        /// </summary>
        /// <remarks>
        /// Walking in rather than standing still on purpose. Knockback throws the player past the boss's
        /// arena clamp, and a player parked outside it measures a boss that cannot reach them - which reads
        /// as a harmless fight rather than as a fixture that wandered off.
        /// </remarks>
        private async Task MeasureBossIncoming(string sceneName)
        {
            GameplaySceneDefaults layout = GameplaySceneDefaults.CreateForScene(sceneName, null);
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            Assert.NotNull(catalog, "No tuning catalog, so the player's pool and the boss's health are both unknown.");

            PlayerResourceData resources = catalog.PlayerResources;
            Assert.NotNull(resources, "Resources/Design/PlayerResources.json has to load: the flask maths below is its numbers.");

            await LoadChapter(sceneName);

            PlayerController2D player = RequirePlayer(sceneName);
            TakeTheControls(player);

            BossHandle boss = BossHandle.FindInScene();
            Assert.NotNull(boss, $"{sceneName} has to stand a boss in its arena; there is no fight to be hit by otherwise.");

            DisableEveryEnemyExcept(boss.Body);

            // Read while it is alive. A respawn frees the boss this handle holds, and a freed Godot object
            // reads back as gone - so asking afterwards names the fight '<none>'.
            string bossName = boss.Name;

            var playerHealth = player.GetComponentInParent<Health>();
            Assert.NotNull(playerHealth, "The shipped player has to carry Health.");

            var bossHealth = boss.Body.GetComponent<Health>();
            float bossMaxHealth = bossHealth != null ? bossHealth.MaxHealth : 0f;

            // X in pixels: the boss's own position minus a pixel stand-off. The spawn Y comes straight off
            // the layout, which is already Godot-space (+Y down).
            PlaceOn(player, new Vector2(boss.Body.GlobalPosition.X - BossStandOff, layout.PlayerSpawnPosition.Y));

            // The intro is presentation, not the fight. Timing from the placement instead would fold
            // chapter one's 1.3s of beats into the boss's damage rate and make it look gentler than it is.
            float introDeadline = GameClock.Time + IntroWaitSeconds;
            while (boss.IsInIntro && GameClock.Time < introDeadline)
            {
                player.SetMoveInput(Vector2.Zero);
                await TestContext.Runner.NextFrame();
            }

            Assert.IsFalse(boss.IsInIntro,
                $"{sceneName}'s boss never left its intro in {IntroWaitSeconds:F0}s with cutscenes skipped. Either it " +
                "never saw the player - check the stand-off against its detectionRange - or the intro hold never lifted.");

            float startHealth = playerHealth.CurrentHealth;
            float last = startHealth;
            var hits = 0;
            var died = false;
            float start = GameClock.Time;
            float deadline = start + BossFightBudgetSeconds;

            // A death and its respawn both land inside one physics step, so sampling IsDead once a tick
            // misses the death entirely - and the respawn refills the bar, rebuilds every enemy this
            // fixture switched off, and frees the boss this handle is holding. Chapter one's fight
            // read 1.11 dps and "cannot kill a stationary player" for exactly that reason: it killed the
            // player at 19.69s and the loop averaged 25 more seconds of an empty arena into the rate.
            // The event is the only thing that sees it, so the clock stops here rather than on IsDead.
            var respawned = false;
            var deathState = player.GetComponentInParent<DeathStateController>();
            Assert.NotNull(deathState, "The shipped player carries the death controller this run has to watch.");

            Action onRespawn = () => respawned = true;
            deathState.OnRespawn += onRespawn;

            try
            {
                while (GameClock.Time < deadline)
                {
                    if (playerHealth.IsDead || respawned)
                    {
                        died = true;
                        break;
                    }

                    // Back into contact after every knockback. Enemy bodies are walls in this game, so this
                    // settles against the boss rather than walking through it. X only - no vertical input,
                    // so there is no sign to flip.
                    float playerX = player.GlobalPosition.X;
                    float bossX = GodotObject.IsInstanceValid(boss.Body) ? boss.Body.GlobalPosition.X : playerX;
                    player.SetMoveInput(new Vector2(Mathf.Sign(bossX - playerX), 0f));

                    float now = playerHealth.CurrentHealth;
                    if (now < last - 0.01f)
                    {
                        hits++;
                    }

                    last = now;

                    await TestContext.Runner.NextPhysicsFrame();
                }
            }
            finally
            {
                deathState.OnRespawn -= onRespawn;
            }

            float seconds = GameClock.Time - start;
            player.SetMoveInput(Vector2.Zero);

            // A respawn has already refilled the bar by the time this reads it, so the difference would
            // report a fraction of what the fight actually dealt. A death is the whole pool, by definition.
            float taken = died
                ? startHealth
                : startHealth - Mathf.Max(0f, playerHealth.CurrentHealth);
            float dps = taken / Mathf.Max(seconds, 0.001f);
            float flaskPool = resources.maxHealth + resources.healAmount * resources.maxHealCharges;

            GD.Print(
                $"[QA/Combat] {sceneName} boss '{bossName}' incoming: a player who walks into the fight and never " +
                $"dodges, parries or drinks survived {seconds:F2}s (died={died}), taking {taken:F0} health across " +
                $"{hits} hit(s) - {(hits > 0 ? (taken / hits).ToString("F1") : "n/a")} a hit, {dps:F2} dps. " +
                $"Pool {resources.maxHealth:F0} + {resources.maxHealCharges} flasks x {resources.healAmount:F0} = " +
                $"{flaskPool:F0}, so at this rate the whole pool is {flaskPool / Mathf.Max(dps, 0.001f):F1}s of standing " +
                $"in it. Boss health {bossMaxHealth:F0}, reached phase two={boss.IsPhaseTwo}, still standing=" +
                $"{GodotObject.IsInstanceValid(boss.Body)}. [[PlaytimePlan]] budgets a 75s attempt: this is the ceiling " +
                "on how long the fight lets a player who never defends have, so a real attempt has to buy its seconds " +
                "by dodging.");

            // The only assertion: the boss actually fought. Everything above is a tuning number and none of
            // it belongs in a red. A zero here is a fixture that wandered out of reach, not a soft boss.
            Assert.Greater(taken, 0f,
                $"{sceneName}'s boss landed nothing in {seconds:F0}s on a player standing in its face. The player is " +
                "out of the arena clamp, the input never reached the controller, or the boss never engaged - none of " +
                "which is a number about the fight.");
        }

        // ------------------------------------------------------------------------------------------
        // Driving
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Runs <see cref="SimpleCombatAgent"/> against one enemy, one decision per physics tick, exactly
        /// the way <c>UnityTestAgentPlayModeSmokeTests</c> proves it can kill a grunt. Stops on the kill,
        /// on the player's death, or on the budget.
        /// </summary>
        private static async Task DriveTheFight(
            PlayerController2D player,
            Node2D target,
            Health enemyHealth,
            Health playerHealth,
            float budgetSeconds,
            Fight fight)
        {
            var probe = new MyGameStateProbe(player, new Node[] { target });
            var driver = new MyGameAgentInputDriver(player);

            // Pixels, not the metres the constructor still defaults to - see the class remarks.
            var agent = new SimpleCombatAgent(AgentAttackRange);

            float startHealth = playerHealth != null ? playerHealth.CurrentHealth : 0f;
            float lowest = startHealth;
            float last = startHealth;
            float enemyMax = enemyHealth != null ? enemyHealth.MaxHealth : 0f;

            float start = GameClock.Time;
            float deadline = start + budgetSeconds;
            fight.FirstContactAt = -1f;

            try
            {
                while (GameClock.Time < deadline)
                {
                    // target first, and through IsInstanceValid so a freed body reads as gone the way
                    // Unity's overloaded == did: EnemyDeathCleanup frees with no delay, so the node is gone
                    // by the tick after the killing blow and touching enemyHealth would throw rather than
                    // report a kill.
                    if (!GodotObject.IsInstanceValid(target) || !GodotObject.IsInstanceValid(enemyHealth) || enemyHealth.IsDead)
                    {
                        fight.Killed = true;
                        break;
                    }

                    if (fight.FirstContactAt < 0f && enemyHealth.CurrentHealth < enemyMax - 0.01f)
                    {
                        fight.FirstContactAt = GameClock.Time - start;
                    }

                    if (playerHealth != null)
                    {
                        float now = playerHealth.CurrentHealth;
                        if (now < last - 0.01f)
                        {
                            fight.PlayerHitsTaken++;
                        }

                        last = now;
                        lowest = Mathf.Min(lowest, now);

                        if (playerHealth.IsDead)
                        {
                            fight.PlayerDied = true;
                            break;
                        }
                    }

                    AgentObservation observation = probe.Capture();
                    driver.Apply(agent.Decide(observation).Action);

                    await TestContext.Runner.NextPhysicsFrame();
                }
            }
            finally
            {
                // The driver holds real InputMap actions down. Left pressed they would leak into the next
                // test in this fixture, which Unity never had to think about.
                driver.ReleaseAll();
            }

            fight.Seconds = GameClock.Time - start;
            fight.ContactSeconds = fight.FirstContactAt >= 0f ? fight.Seconds - fight.FirstContactAt : 0f;
            fight.PlayerHealthLost = startHealth - lowest;
            fight.PlayerHealthLeft = playerHealth != null ? playerHealth.CurrentHealth : 0f;
            player.SetMoveInput(Vector2.Zero);
        }

        // ------------------------------------------------------------------------------------------
        // Scene and fixture plumbing
        // ------------------------------------------------------------------------------------------

        private async Task LoadChapter(string sceneName)
        {
            _openedAChapter = true;
            GameSave.LoadOnNextGameplayStart = false;

            // LoadScene already waits the two frames the Unity version spelled out: the bootstrap builds
            // the arena and spawns the actors in _Ready, and the second frame is what lets their own
            // _Ready run before anything here looks for them.
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(sceneName));
        }

        private static PlayerController2D RequirePlayer(string sceneName)
        {
            var player = SceneQuery.FindFirst<PlayerController2D>();
            Assert.NotNull(player, $"{sceneName} has to spawn a player.");
            return player;
        }

        /// <summary>
        /// PORT INVERSION. Unity switched <c>PlayerInputReceiver</c> <b>off</b> here, because its driver
        /// called the controller directly and a dead keyboard would otherwise overwrite the agent's move
        /// input every frame. <see cref="MyGameAgentInputDriver"/> presses real InputMap actions instead,
        /// so the receiver is the thing that carries them to the player: switching it off is what would
        /// silence the agent now. The method stays, doing the opposite, so the intent is still stated at
        /// every call site.
        /// </summary>
        private static void TakeTheControls(PlayerController2D player)
        {
            var receiver = player.GetComponentInParent<PlayerInputReceiver>() ?? SceneQuery.FindFirst<PlayerInputReceiver>();
            Assert.NotNull(receiver, "The shipped player carries the input receiver these runs drive through.");
            receiver.Enabled = true;
        }

        /// <summary>The westmost live enemy of a type - the first one a player walking east would meet.</summary>
        private static T FirstOnTheRoad<T>() where T : EnemyStateMachine
        {
            T first = null;
            foreach (T candidate in SceneQuery.FindAll<T>())
            {
                if (!IsActive(candidate))
                {
                    continue;
                }

                if (first == null || candidate.GlobalPosition.X < first.GlobalPosition.X)
                {
                    first = candidate;
                }
            }

            return first;
        }

        private static int DisableEveryEnemyExcept(Node2D keep) => DisableEveryEnemyExcept(new[] { keep });

        private static int DisableEveryEnemyExcept(Node2D[] keep)
        {
            var switchedOff = 0;
            foreach (EnemyStateMachine machine in SceneQuery.FindAll<EnemyStateMachine>())
            {
                if (System.Array.IndexOf(keep, (Node2D)machine) >= 0 || !IsActive(machine))
                {
                    continue;
                }

                // Unity's gameObject.SetActive(false). The shim writes visibility, ProcessMode and the
                // descendant collision shapes, because a Godot node that is only hidden is still solid.
                machine.SetActive(false);
                switchedOff++;
            }

            return switchedOff;
        }

        /// <summary>The <paramref name="count"/> live enemies closest to a point, nearest first.</summary>
        private static Node2D[] NearestLiveTo(Vector2 point, int count)
        {
            var machines = new List<EnemyStateMachine>();
            foreach (EnemyStateMachine machine in SceneQuery.FindAll<EnemyStateMachine>())
            {
                if (IsActive(machine))
                {
                    machines.Add(machine);
                }
            }

            machines.Sort((a, b) =>
                point.DistanceTo(a.GlobalPosition).CompareTo(point.DistanceTo(b.GlobalPosition)));

            int take = Mathf.Min(count, machines.Count);
            var picked = new Node2D[take];
            for (var i = 0; i < take; i++)
            {
                picked[i] = machines[i];
            }

            return picked;
        }

        /// <summary>Unity's <c>FindObjectsInactive.Exclude</c>: a node parked by <c>SetActive(false)</c>.</summary>
        private static bool IsActive(Node node) =>
            GodotObject.IsInstanceValid(node) && node.ProcessMode != Node.ProcessModeEnum.Disabled;

        /// <summary>
        /// Unity teleported the transform with the rigidbody's simulation off, because a live continuous
        /// body sweeps the length of the 300-unit arena. A <c>CharacterBody2D</c> only moves inside
        /// <c>MoveAndSlide</c>, so the equivalent here is to clear the velocity and write the position -
        /// which is exactly what <c>DeathStateController</c>'s respawn does. The controller is a component
        /// node under the body, so the move has to go to the body.
        /// </summary>
        private static void PlaceOn(PlayerController2D player, Vector2 position)
        {
            var body = player.GetComponentInParent<PlayerMotor2D>();
            Assert.NotNull(body, "The player root is the motor body; without it there is nothing to move.");

            body.ResetMotion();
            body.GlobalPosition = position;
        }

        private static int Placed(GameplaySceneDefaults layout, GameplaySceneDefaults.EnemySpawn[] spawns)
        {
            if (!layout.SpawnApproachEnemies || spawns == null)
            {
                return 0;
            }

            return spawns.Length;
        }

        private static int VariantSpawns(GameplaySceneDefaults.EnemySpawn[] spawns)
        {
            if (spawns == null)
            {
                return 0;
            }

            var count = 0;
            foreach (GameplaySceneDefaults.EnemySpawn spawn in spawns)
            {
                if (!string.IsNullOrEmpty(spawn.DataFile))
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------------------------------
        // Small carriers
        // ------------------------------------------------------------------------------------------

        /// <summary>Which archetype's design file a run is measured against.</summary>
        private enum ArchetypeTuning
        {
            Grunt,
            Leaper,
            Caster
        }

        /// <summary>
        /// The shared tuning file behind an archetype - the same one the spawner reads, so the health a
        /// dps is divided by and the detection range a fight starts at both come from what was fought.
        /// A placement naming its own <c>dataFile</c> would break that, which is why the caller checks the
        /// live health against this before believing any of it.
        /// </summary>
        private static EnemyTuningData Read(ArchetypeTuning which, GameplayTuningCatalog catalog)
        {
            if (catalog == null)
            {
                return null;
            }

            switch (which)
            {
                case ArchetypeTuning.Leaper:
                    return catalog.LeapingAttacker;
                case ArchetypeTuning.Caster:
                    return catalog.RangedCaster;
                default:
                    return catalog.MeleeGrunt;
            }
        }

        private sealed class Fight
        {
            public bool Killed;
            public bool PlayerDied;
            public int PlayerHitsTaken;
            public float Seconds;
            public float FirstContactAt;
            public float ContactSeconds;
            public float PlayerHealthLost;
            public float PlayerHealthLeft;
        }

        /// <summary>
        /// A chapter's boss as two numbers and a name, whichever kind of boss stands in it. Chapter one is
        /// still <see cref="WrathMiniBoss"/> and names no data file, so its health lives on a different
        /// field from the other seven's - that difference belongs here rather than at three call sites.
        /// </summary>
        private sealed class BossRow
        {
            public string Name { get; private set; }
            public float MaxHealth { get; private set; }
            public int SoulReward { get; private set; }

            public static BossRow For(GameplaySceneDefaults layout, GameplayTuningCatalog catalog)
            {
                RainbowChapterBossData chapter = catalog != null ? catalog.ChapterBoss(layout.BossDataFile) : null;
                if (chapter != null)
                {
                    return new BossRow
                    {
                        Name = chapter.BossName,
                        MaxHealth = chapter.MaxHealth,
                        SoulReward = chapter.SoulReward
                    };
                }

                WrathMiniBossData wrath = catalog != null ? catalog.WrathMiniBoss : null;
                return new BossRow
                {
                    Name = "Crimson Warden",
                    // Health, not a distance - ScaleToPixels leaves it alone, so this is the authored number.
                    MaxHealth = wrath != null ? wrath.maxHealthBoss : 0f,
                    SoulReward = wrath != null ? wrath.soulReward : 0
                };
            }
        }

        /// <summary>
        /// Whichever boss the arena built, asked the two questions this file needs. Typed rather than
        /// reflected because both kinds are in the same assembly the tests already reference, and the two
        /// do not share an interface for either answer - <c>IBossEncounter</c> is about arrival and defeat.
        /// </summary>
        private sealed class BossHandle
        {
            private WrathMiniBoss _wrath;
            private RainbowChapterBossBehaviour _chapter;

            public Node2D Body => Alive(_wrath) ? _wrath : Alive(_chapter) ? _chapter : null;

            public bool IsInIntro => Alive(_wrath) ? _wrath.IsInIntro : Alive(_chapter) && _chapter.IsInIntro;

            public bool IsPhaseTwo => Alive(_wrath) ? _wrath.IsInPhaseTwo : Alive(_chapter) && _chapter.IsPhaseTwo;

            public string Name => Alive(_wrath) ? "Crimson Warden" : Alive(_chapter) ? _chapter.BossName : "<none>";

            public static BossHandle FindInScene()
            {
                var chapter = SceneQuery.FindFirst<RainbowChapterBossBehaviour>();
                if (chapter != null)
                {
                    return new BossHandle { _chapter = chapter };
                }

                var wrath = SceneQuery.FindFirst<WrathMiniBoss>();
                return wrath != null ? new BossHandle { _wrath = wrath } : null;
            }

            /// <summary>Unity's overloaded <c>== null</c> on a destroyed object, spelled out.</summary>
            private static bool Alive(Node node) => node != null && GodotObject.IsInstanceValid(node);
        }
    }
}
