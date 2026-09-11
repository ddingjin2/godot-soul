using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;

namespace MyGame.Tests
{
    /// <summary>
    /// The three systems the later chapters are built on: a chant that heals unless it is stopped
    /// (chapter four), harmless copies that draw the eye (chapter six), and stances that change which
    /// attacks a boss can reach (chapters seven and eight).
    ///
    /// Each is off unless its number is authored, so the failure they share is silence: a chant nobody
    /// can interrupt, copies that can hurt, a stance list that never rotates.
    /// </summary>
    /// <remarks>
    /// PORT NOTE: every fixture JSON stays in Unity metres and is put through the
    /// <c>OnValidate</c> + <c>ScaleToPixels</c> pass <see cref="RainbowChapterBossData.Load"/> runs, so
    /// the one spatial number in here - <c>afterimageSpread</c> - reaches the boss in pixels. The chant
    /// and stance numbers are seconds and health per second, which the port does not scale.
    /// </remarks>
    public sealed class GameplayBossChapterSystemsTests
    {
        private RainbowChapterBossBehaviour _boss;

        private static Node FixtureRoot => GameplayBuildShim.SceneRoot;

        [TearDown]
        public void TearDown()
        {
            foreach (BossAfterimage image in SceneQuery.FindAll<BossAfterimage>())
            {
                if (GodotObject.IsInstanceValid(image))
                    image.QueueFree();
            }

            if (GodotObject.IsInstanceValid(_boss))
                _boss.QueueFree();

            _boss = null;
        }

        /// <summary>
        /// The chant has to actually restore health, or interrupting it is a ritual with no stake.
        /// </summary>
        [Test]
        public async Task Chant_HealsTheBoss_WhenNobodyStopsIt()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(ChantJson());
            var health = boss.GetComponent<Health>();

            // Health fractions are unscaled by the port.
            health.SetHealth(health.MaxHealth * 0.4f);
            float wounded = health.CurrentHealth;

            await WaitUntil(() => boss.IsChanting, 3f, "The chant should start once its interval has passed.");
            await WaitUntil(() => boss.HealthRestoredByChanting > 0f, 2f, "A chant that heals nothing is scenery.");

            Assert.Greater(health.CurrentHealth, wounded,
                "The chant has to put health back, or there is nothing for the player to be interrupting.");
        }

        /// <summary>
        /// The priority window itself. Damage during the chant ends it, and the boss pays the whole
        /// interval before trying again - resuming where it stopped would make the interrupt a delay
        /// rather than a decision.
        /// </summary>
        [Test]
        public async Task Chant_EndsWhenTheBossIsHit_AndDoesNotResume()
        {
            // Its own fixture, with an interval long enough that a chant which resumed would still be
            // waiting when the assertion below runs. Sharing the fast one would have let the next chant
            // start inside the check and read as "it resumed".
            RainbowChapterBossBehaviour boss = BuildBoss(SlowChantJson());
            var health = boss.GetComponent<Health>();

            health.SetHealth(health.MaxHealth * 0.5f);

            await WaitUntil(() => boss.IsChanting, 4f, "The chant should start once its interval has passed.");

            // Damage is unscaled; Vector2.right is Vector2.Right.
            health.ApplyDamage(5f, Vector2.Right);
            await TestContext.Runner.NextFrame();

            Assert.IsFalse(boss.IsChanting, "A hit during the chant stops it.");

            float restored = boss.HealthRestoredByChanting;

            // Well inside the 1.5s interval the interrupt costs, so a chant that resumed shows up here.
            await TestContext.Runner.Seconds(0.6f);

            Assert.AreEqual(restored, boss.HealthRestoredByChanting, 0.01f,
                "An interrupted chant costs the boss the whole interval; resuming makes the interrupt a delay.");
        }

        /// <summary>
        /// Copies are the spectacle, and the whole lesson is that the spectacle cannot hurt. One that
        /// carried a collider or a health bar would make the fight a guessing game with real stakes.
        /// </summary>
        [Test]
        public async Task Afterimages_AppearOnTheTelegraph_AndAreHarmless()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(AfterimageJson());

            boss.RequestAttack(boss.BossData.Attacks[0]);
            await TestContext.Runner.NextFrame();

            List<BossAfterimage> images = SceneQuery.FindAll<BossAfterimage>();

            Assert.AreEqual(2, images.Count, "The boss is authored with two copies, so the telegraph should raise two.");

            foreach (BossAfterimage image in images)
            {
                // Unity asked for a Collider2D. Godot splits that into the body/area that participates in
                // physics and the shape hung under it, so both halves are checked - a copy with either is
                // a copy something can touch.
                Assert.IsNull(image.GetComponent<CollisionObject2D>(), "A copy that can be touched is not harmless.");
                Assert.IsNull(image.GetComponent<CollisionShape2D>(), "A copy that can be touched is not harmless.");
                Assert.IsNull(image.GetComponent<Health>(), "A copy with health is a second boss.");
                Assert.IsNull(image.GetComponent<DamageReceiver>(), "A copy that takes damage steals hits from the real body.");
            }

            await WaitUntil(
                () => SceneQuery.FindAll<BossAfterimage>().Count == 0,
                3f,
                "Copies have to expire, or the arena stops being legible for the rest of the fight.");
        }

        /// <summary>
        /// A stance is only a stance if it takes attacks away. This checks the boss starts in the first
        /// authored stance and walks to the next in order rather than rolling for it.
        /// </summary>
        [Test]
        public async Task Stances_StartAtTheFirst_AndRotateInAuthoredOrder()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(StanceJson());

            await WaitUntil(() => !string.IsNullOrEmpty(boss.CurrentStanceId), 2f,
                "A boss with stances has to be standing in one of them.");

            Assert.AreEqual("red", boss.CurrentStanceId, "The rotation starts at the first stance the rows name.");

            await WaitUntil(() => boss.CurrentStanceId == "violet", 3f,
                "The stance should rotate to the next one in authored order.");

            await WaitUntil(() => boss.CurrentStanceId == "red", 3f,
                "The rotation wraps rather than stopping at the last stance.");
        }

        // ------------------------------------------------------------------------------------------

        private static string ChantJson()
        {
            return BossJson(
                "\"chantInterval\":0.3,\"chantDuration\":2.0,\"chantHealPerSecond\":20,",
                "{\"attackId\":\"chant_swing\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":5,\"telegraphTime\":0.2,\"activeTime\":0.2," +
                "\"recoveryTime\":0.2,\"range\":1.5,\"phaseTwoWeight\":1}");
        }

        private static string SlowChantJson()
        {
            return BossJson(
                "\"chantInterval\":1.5,\"chantDuration\":3.0,\"chantHealPerSecond\":20,",
                "{\"attackId\":\"chant_swing\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":5,\"telegraphTime\":0.2,\"activeTime\":0.2," +
                "\"recoveryTime\":0.2,\"range\":1.5,\"phaseTwoWeight\":1}");
        }

        private static string AfterimageJson()
        {
            return BossJson(
                "\"afterimageCount\":2,\"afterimageSpread\":2,\"afterimageLifetime\":0.4,",
                "{\"attackId\":\"shadow_swing\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":5,\"telegraphTime\":0.3,\"activeTime\":0.2," +
                "\"recoveryTime\":0.2,\"range\":1.5,\"phaseTwoWeight\":1}");
        }

        private static string StanceJson()
        {
            return BossJson(
                "\"stanceRotationInterval\":0.4,",
                "{\"attackId\":\"red_swing\",\"damageType\":1,\"afterimageCountOverride\":-1,\"stanceId\":\"red\",\"damage\":5,\"telegraphTime\":0.2," +
                "\"activeTime\":0.2,\"recoveryTime\":0.2,\"range\":1.5,\"phaseTwoWeight\":1}," +
                "{\"attackId\":\"violet_swing\",\"damageType\":1,\"afterimageCountOverride\":-1,\"stanceId\":\"violet\",\"damage\":5,\"telegraphTime\":0.2," +
                "\"activeTime\":0.2,\"recoveryTime\":0.2,\"range\":1.5,\"phaseTwoWeight\":1}");
        }

        private static string BossJson(string extraFields, string attackRows)
        {
            return "{\"maxHealth\":200,\"moveSpeed\":2,\"detectionRange\":8,\"attackRange\":1.5," +
                   "\"phaseTwoHealthThreshold\":0.1,\"phaseTwoSpeedMultiplier\":1,\"phaseTwoCooldownMultiplier\":1," +
                   // Stun, poise, body and pulse were class defaults until K3; a boss with a zero body or a
                   // zero stun is not the one these tests were written against, so the fixture says them.
                   "\"maxPoise\":90,\"poiseHeavyMultiplier\":2,\"poiseRegenDelay\":3,\"poiseRegenRate\":30,\"stunDuration\":1,\"soulReward\":300," +
                   "\"bodySize\":{\"x\":1.6,\"y\":2.3},\"telegraphPulseSpeed\":8,\"telegraphPulseAmplitude\":0.2," +
                   "\"bossName\":\"ChapterSystemsFixture\",\"chapterName\":\"Test Chapter\"," +
                   extraFields +
                   "\"attacks\":[" + attackRows + "]}";
        }

        /// <summary>
        /// No player is built for these: none of the three systems needs one, and a fixture player with
        /// no floor falls out of the arena in about a second, which has already cost two tests a red run.
        /// The intro is skipped because a boss that never sees a player never leaves it.
        /// </summary>
        /// <remarks>
        /// PORT CHANGE: <c>ScriptableObject.CreateInstance</c> + <c>JsonUtility.FromJsonOverwrite</c> is
        /// one <c>JsonData.FromJson</c> plus the two passes <see cref="RainbowChapterBossData.Load"/>
        /// runs; the Kinematic <c>Rigidbody2D</c> is gone because the ported boss is the body itself; and
        /// the whole hierarchy is assembled detached, because the behaviour's <c>_Ready</c> expects
        /// <see cref="Health"/> to already be a sibling when it enters the tree.
        /// </remarks>
        private RainbowChapterBossBehaviour BuildBoss(string json)
        {
            RainbowChapterBossData data = JsonData.FromJson<RainbowChapterBossData>(json);
            Assert.NotNull(data, "The fixture JSON has to parse.");
            data.OnValidate();
            data.ScaleToPixels();

            _boss = new RainbowChapterBossBehaviour
            {
                Name = "ChapterSystemsFixture",
                Position = Vector2.Zero,
            };

            _boss.AddComponent<Health>();

            FixtureRoot.AddChild(_boss);

            // Required since K5 - see GameplayBossAttackGrammarTests for why chapter one's file.
            _boss.SetEncounterData(BossEncounterData.Load("Design/WrathEncounter"));
            _boss.SetBossData(data);
            _boss.SkipIntro();
            return _boss;
        }

        private static async Task WaitUntil(Func<bool> done, float seconds, string message)
        {
            Assert.IsTrue(await TestContext.Runner.WaitUntil(done, seconds), message);
        }
    }
}
