using System;
using System.Reflection;
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
    /// P0 combat stability, part two: the result broadcaster, the sin table, the shared feedback
    /// systems and the enemy state machines. Split from
    /// <see cref="P0CombatStabilityTests"/>'s first file only for length; it is one fixture.
    /// </summary>
    public partial class P0CombatStabilityTests
    {
        // ---------------------------------------------------------------------------------------
        // Combat result broadcasting
        // ---------------------------------------------------------------------------------------

        [Test]
        public void CombatResultBroadcasterPublishesResolvedResults()
        {
            var target = new Node2D { Name = "CombatResultTarget" };
            var broadcaster = new CombatResultBroadcaster { Name = nameof(CombatResultBroadcaster) };
            target.AddChild(broadcaster);
            Spawn(target);

            DamageResult received = default;
            bool invoked = false;

            // UnityEvent.AddListener -> a plain C# event. INTEGRATION_NOTES, MyGame.Combat.
            broadcaster.OnResult += result =>
            {
                received = result;
                invoked = true;
            };

            var published = new DamageResult(
                null,
                target,
                true,
                false,
                false,
                false,
                10f,
                Vector2.Zero,
                Vector2.Right,
                null,
                DamageType.Standard);

            broadcaster.Publish(published);

            Assert.IsTrue(invoked, "CombatResultBroadcaster should notify listeners.");
            Assert.IsTrue(received.Applied, "CombatResultBroadcaster should pass the published result.");
        }

        [Test]
        public void CombatResolverPublishesEachResolvedResultOnce()
        {
            Node2D attacker = Spawn(new Node2D { Name = "ResolverPublisherAttacker" });

            var target = new Node2D { Name = "ResolverPublisherTarget" };
            var health = new Health { Name = nameof(Health) };
            target.AddChild(health);
            var broadcaster = new CombatResultBroadcaster { Name = nameof(CombatResultBroadcaster) };
            target.AddChild(broadcaster);
            Spawn(target);

            health.SetMaxHealth(100f);
            health.SetHealth(100f);

            int publishCount = 0;
            broadcaster.OnResult += _ => publishCount++;

            DamageResult result = CombatResolver.Resolve(CreateRequest(attacker, target));

            Assert.IsTrue(result.Applied, "Resolver publisher test should apply damage.");
            Assert.AreEqual(1, publishCount, "CombatResolver should publish every resolved target result exactly once.");
        }

        [Test]
        public void CombatResultBroadcasterRoutesExistingResourceRules()
        {
            var player = new Node2D { Name = "ResourcePlayer" };
            var humanity = new HumanityController { Name = nameof(HumanityController) };
            player.AddChild(humanity);
            var resonance = new SinResonanceController { Name = nameof(SinResonanceController) };
            player.AddChild(resonance);
            PlayerFixture.Configure(humanity);
            PlayerFixture.Configure(resonance);
            var playerBroadcaster = new CombatResultBroadcaster { Name = nameof(CombatResultBroadcaster) };
            player.AddChild(playerBroadcaster);
            var attackerAnchor = new Node2D { Name = "PlayerAttackAnchor" };
            player.AddChild(attackerAnchor);
            Spawn(player);
            resonance.Initialize(humanity);

            var enemy = new Node2D { Name = "ResourceEnemy" };
            var enemyBroadcaster = new CombatResultBroadcaster { Name = nameof(CombatResultBroadcaster) };
            enemy.AddChild(enemyBroadcaster);
            Spawn(enemy);

            enemyBroadcaster.Publish(new DamageResult(
                attackerAnchor,
                enemy,
                true,
                false,
                false,
                false,
                10f,
                Vector2.Zero,
                Vector2.Right,
                null,
                DamageType.Standard));
            Assert.IsTrue(
                Mathf.IsEqualApprox(resonance.CurrentResonance, resonance.ResonancePerHit),
                "Applied player hits should add the existing per-hit resonance value.");

            float humanityBeforeHit = humanity.CurrentHumanity;
            float resonanceBeforeHit = resonance.CurrentResonance;
            playerBroadcaster.Publish(new DamageResult(
                enemy,
                player,
                true,
                false,
                false,
                false,
                20f,
                Vector2.Zero,
                Vector2.Left,
                null,
                DamageType.Standard));
            Assert.IsTrue(
                Mathf.IsEqualApprox(humanity.CurrentHumanity, humanityBeforeHit - 5f),
                "Applied incoming hits should use HumanityController's existing on-hit loss.");
            Assert.IsTrue(
                Mathf.IsEqualApprox(resonance.CurrentResonance, resonanceBeforeHit + (resonance.ResonancePerDamage * 2f)),
                "Applied incoming damage should add damage-scaled resonance.");

            float humanityBeforeParry = humanity.CurrentHumanity;
            float resonanceBeforeParry = resonance.CurrentResonance;
            playerBroadcaster.Publish(new DamageResult(
                enemy,
                player,
                false,
                false,
                true,
                false,
                0f,
                Vector2.Zero,
                Vector2.Left,
                null,
                DamageType.Standard));
            Assert.IsTrue(
                Mathf.IsEqualApprox(humanity.CurrentHumanity, humanityBeforeParry),
                "Successful parries should not spend humanity.");
            Assert.IsTrue(
                Mathf.IsEqualApprox(resonance.CurrentResonance, resonanceBeforeParry + resonance.ResonancePerParry),
                "Successful parries should add the existing per-parry resonance value.");
        }

        /// <summary>
        /// The seven-sin table replaced fifteen hardcoded per-sin fields, so the only thing this refactor
        /// can break is a number, and no compiler catches that. Wrath, Sloth and Pride are pinned to the
        /// values their fields held; Gluttony, Greed, Envy and Lust are pinned to the profiles the team
        /// lead called on 2026-08-10, which is what stops one of them drifting back to neutral - a sin
        /// that does nothing still activates, still spends resonance and humanity, and looks identical to
        /// a sin nobody pressed.
        ///
        /// The lookup is exercised on an empty table and on a shortened one as well. The empty case is not
        /// hypothetical: the shipped controller reaches the lookup with whatever the design JSON left it,
        /// and a file with no rows is a file that parsed.
        ///
        /// Reaches the table by the field name "sinModifiers", the way the rest of this file names private
        /// members - a rename breaks this test instead of the build.
        /// </summary>
        [Test]
        public void SinTableKeepsEveryShippedModifierValue()
        {
            // A serialized field stores an enum as its integer, so reordering the original four hands every
            // saved asset a different sin than the one it was authored with.
            Assert.IsTrue(
                (int)SinState.None == 0 && (int)SinState.Wrath == 1 && (int)SinState.Sloth == 2 && (int)SinState.Pride == 3,
                "SinState must keep None/Wrath/Sloth/Pride at 0-3; serialized assets store the integer, not the name.");

            //                    sin                  speed damage dodge parry taken heal-off perfect-parry
            RequireSinModifiers(SinState.Wrath, 1.3f, 1.5f, 0.7f, 0.8f, 1f, true, false);
            RequireSinModifiers(SinState.Sloth, 0.7f, 1f, 1f, 1.5f, 1f, false, false);
            RequireSinModifiers(SinState.Pride, 1f, 1.3f, 1f, 1f, 1.5f, false, true);
            RequireSinModifiers(SinState.Gluttony, 0.85f, 1.15f, 1.3f, 1f, 0.75f, false, false);
            RequireSinModifiers(SinState.Greed, 1f, 1.4f, 1f, 1f, 1.4f, true, false);
            RequireSinModifiers(SinState.Envy, 1.1f, 1.1f, 1f, 1.6f, 1.2f, false, false);
            RequireSinModifiers(SinState.Lust, 1.35f, 0.85f, 1.35f, 0.7f, 1f, false, false);

            // The table has no None row on purpose, and an idle controller has to read neutral - that is
            // what the old `if (_activeSin == SinState.Pride)` branch returned for everything else.
            var idle = new SinResonanceController { Name = "SinTableIdle" };
            PlayerFixture.Configure(idle);
            Spawn(idle);
            Assert.IsTrue(
                Mathf.IsEqualApprox(idle.GetDamageTakenMultiplier(), 1f),
                "With no sin active the damage-taken multiplier should be 1.");
            Assert.IsFalse(idle.HasPerfectParryBonus(), "With no sin active there should be no perfect-parry bonus.");

            // The empty-table case: no rows, so the defaults have to come back rather than running every
            // sin at neutral.
            RequireSinModifiers(SinState.Wrath, 1.3f, 1.5f, 0.7f, 0.8f, 1f, true, false, new SinModifiers[0]);

            // Lookup is by the row's own sin, not by array index. A designer may shorten or reorder the
            // table; a sin with no row has to read neutral rather than go out of bounds or inherit the
            // numbers of whichever row happens to sit at its index.
            RequireSinModifiers(SinState.Pride, 1f, 1.3f, 1f, 1f, 1.5f, false, true, PrideOnlyTable());
            RequireSinModifiers(SinState.Sloth, 1f, 1f, 1f, 1f, 1f, false, false, PrideOnlyTable());
        }

        private static SinModifiers[] PrideOnlyTable()
        {
            return new[]
            {
                new SinModifiers
                {
                    sin = SinState.Pride,
                    damageMultiplier = 1.3f,
                    damageTakenMultiplier = 1.5f,
                    perfectParryBonus = true,
                },
            };
        }

        private void RequireSinModifiers(SinState sin, float speed, float damage, float dodge, float parry,
            float damageTaken, bool disableHeal, bool perfectParry, SinModifiers[] table = null)
        {
            var resonance = new SinResonanceController { Name = $"SinTable{sin}" };

            // SinTuning.json's rows, which are the shipped table - the same one DefaultSinModifiers
            // rebuilds when a file has none. The per-case override below still replaces it.
            PlayerFixture.Configure(resonance);
            Spawn(resonance);

            if (table != null)
            {
                SetPrivateField(resonance, "sinModifiers", table);
            }

            SinState applied = SinState.None;
            float gotSpeed = 0f, gotDamage = 0f, gotDodge = 0f, gotParry = 0f;
            bool gotDisableHeal = false;
            int applyCount = 0;
            resonance.OnApplyModifiers += (s, sp, dm, dg, pr, dh) =>
            {
                applied = s;
                gotSpeed = sp;
                gotDamage = dm;
                gotDodge = dg;
                gotParry = pr;
                gotDisableHeal = dh;
                applyCount++;
            };

            // No humanity is wired, so activation only has to clear the resonance cost.
            resonance.AddResonance(1000f);
            resonance.RequestActivateSin(sin);

            Assert.AreEqual(1, applyCount, $"Activating {sin} should apply modifiers exactly once.");
            Assert.AreEqual(sin, applied, $"Activating {sin} should report {sin} to the actor applying the modifiers.");
            Assert.IsTrue(Mathf.IsEqualApprox(gotSpeed, speed), $"{sin} speed multiplier should be {speed}, was {gotSpeed}.");
            Assert.IsTrue(Mathf.IsEqualApprox(gotDamage, damage), $"{sin} damage multiplier should be {damage}, was {gotDamage}.");
            Assert.IsTrue(Mathf.IsEqualApprox(gotDodge, dodge), $"{sin} dodge duration multiplier should be {dodge}, was {gotDodge}.");
            Assert.IsTrue(Mathf.IsEqualApprox(gotParry, parry), $"{sin} parry window multiplier should be {parry}, was {gotParry}.");
            Assert.AreEqual(disableHeal, gotDisableHeal, $"{sin} should {(disableHeal ? "" : "not ")}disable healing.");
            Assert.IsTrue(
                Mathf.IsEqualApprox(resonance.GetDamageTakenMultiplier(), damageTaken),
                $"{sin} damage-taken multiplier should be {damageTaken}, was {resonance.GetDamageTakenMultiplier()}.");
            Assert.AreEqual(perfectParry, resonance.HasPerfectParryBonus(),
                $"{sin} should {(perfectParry ? "" : "not ")}grant the perfect-parry bonus.");
        }

        [Test]
        public void LethalCombatResultEntersSpiritState()
        {
            Node2D attacker = Spawn(new Node2D { Name = "LethalAttacker" });

            var player = new Node2D { Name = "SpiritPlayer" };
            // SpriteRenderer -> Sprite2D, and it is a child node here rather than a second component.
            player.AddChild(new Sprite2D { Name = nameof(Sprite2D) });
            var health = new Health { Name = nameof(Health) };
            player.AddChild(health);
            var humanity = new HumanityController { Name = nameof(HumanityController) };
            player.AddChild(humanity);
            var deathState = new DeathStateController { Name = nameof(DeathStateController) };
            player.AddChild(deathState);
            PlayerFixture.Configure(humanity);
            PlayerFixture.Configure(deathState);
            Spawn(player);

            health.SetMaxHealth(100f);
            health.SetHealth(100f);

            var spiritPlatform = Spawn(new Node2D { Name = "SpiritPlatform" });
            deathState.Initialize(health, humanity, null, spiritPlatform);

            var request = new DamageRequest(attacker, player, Vector2.Zero, Vector2.Right, 200f, 0f, DamageType.Heavy);
            DamageResult result = CombatResolver.Resolve(request);

            Assert.IsTrue(result.Killed, "Lethal combat result should report the kill.");
            Assert.IsTrue(deathState.IsInSpiritState, "Lethal player damage should enter the existing Spirit state through Health depletion.");

            // GameObject.SetActive has no single Godot switch: DeathStateController.SetNodeActive writes
            // visibility, ProcessMode and the descendant collision shapes. activeSelf is the first two.
            Assert.IsTrue(spiritPlatform.Visible, "Entering Spirit state should make the Spirit platform visible.");
            Assert.AreNotEqual(Node.ProcessModeEnum.Disabled, spiritPlatform.ProcessMode, "Entering Spirit state should let the Spirit platform process again.");
        }

        [Test]
        public void CombatResultBroadcasterOwnsEverySideEffect()
        {
            foreach (string merged in new[]
                     {
                         "CombatResultHitStopListener",
                         "CombatResultCameraShakeListener",
                         "CombatResultAudioListener",
                         "CombatResultFeedbackListener",
                         "CombatResultResourceListener",
                     })
            {
                Assert.IsNull(ResolveType("MyGame.Combat." + merged), merged + " should stay merged into CombatResultBroadcaster.");
            }

            var target = new Node2D { Name = "BroadcasterSideEffects" };
            target.AddChild(new Sprite2D { Name = nameof(Sprite2D) });
            var feedback = new CombatFeedback { Name = nameof(CombatFeedback) };
            target.AddChild(feedback);
            var broadcaster = new CombatResultBroadcaster { Name = nameof(CombatResultBroadcaster) };
            target.AddChild(broadcaster);
            Spawn(target);

            int feedbackCount = 0;
            // No `??= new UnityEvent()` any more: OnHitFeedback is a C# event and cannot be null-backed.
            feedback.OnHitFeedback += () => feedbackCount++;

            broadcaster.Publish(new DamageResult(
                null,
                target,
                true,
                false,
                false,
                false,
                10f,
                Vector2.Zero,
                Vector2.Right,
                null,
                DamageType.Standard));

            Assert.AreEqual(1, feedbackCount, "Publishing an applied result should drive hit feedback without a separate listener component.");
        }

        // ---------------------------------------------------------------------------------------
        // Enemies
        // ---------------------------------------------------------------------------------------

        [Test]
        public void EnemyStateMachineEntersTerminalDeadState()
        {
            var enemy = new TestEnemyStateMachine { Name = "StateMachineEnemy" };
            ConfigureFromDesign(enemy);
            // Rigidbody2D is gone: the enemy IS a CharacterBody2D. INTEGRATION_NOTES, MyGame.Enemy.
            AddBoxShape(enemy);
            var health = new Health { Name = nameof(Health) };
            enemy.AddChild(health);
            Spawn(enemy);

            health.SetMaxHealth(10f);
            health.SetHealth(10f);

            Assert.IsTrue(Enum.IsDefined(typeof(EnemyState), "Dead"), "EnemyState should include Dead.");

            health.ApplyDamage(20f, Vector2.Right);

            Assert.AreEqual("Dead", enemy.CurrentState.ToString(), "EnemyStateMachine should enter Dead when health is depleted.");
            enemy.TryTransitionForTest(EnemyState.Patrol);
            Assert.AreEqual("Dead", enemy.CurrentState.ToString(), "Dead state should be terminal.");
        }

        /// <summary>
        /// Async because switching a collision shape off is deferred in Godot - a shape cannot be
        /// disabled from inside the physics callback reporting the hit that killed the enemy - so the
        /// flag only reads back on the next frame.
        /// </summary>
        [Test]
        public async Task EnemyDeathCleanupNeutralizesDeadEnemy()
        {
            var enemy = new TestEnemyStateMachine { Name = "DeadEnemy" };
            ConfigureFromDesign(enemy);
            CollisionShape2D shape = AddBoxShape(enemy);
            var health = new Health { Name = nameof(Health) };
            enemy.AddChild(health);
            var cleanup = new EnemyDeathCleanup { Name = nameof(EnemyDeathCleanup) };
            enemy.AddChild(cleanup);
            Spawn(enemy);

            health.SetMaxHealth(10f);
            health.SetHealth(10f);

            // Unity's edit mode never ran the delayed Destroy; here CleanupNow always frees the actor
            // through a scene-tree timer, so the delay is pushed past the end of the test rather than
            // letting the body vanish before the assertions read it.
            SetPrivateField(cleanup, "destroyDelay", 60f);

            // PPU: the Unity (3, 4) m/s becomes (300, 400) px/s, and Y FLIPS - Unity's +4 was upward,
            // Godot's is downward. Neither sign matters to the assertion (it is zeroed) but the
            // conversion is spelled out so a later reader does not read this as metres.
            enemy.Velocity = new Vector2(World.U(3f), -World.U(4f));

            health.ApplyDamage(20f, Vector2.Right);
            cleanup.CleanupNow();

            Assert.AreEqual(Vector2.Zero, enemy.Velocity, "Dead enemy cleanup should stop the body's velocity.");

            await TestContext.Runner.NextPhysicsFrame();
            await TestContext.Runner.NextFrame();

            Assert.IsTrue(shape.Disabled, "Dead enemy cleanup should disable colliders.");
        }

        [Test]
        public void LeapingAttackerLandsOnlyAfterGroundContact()
        {
            // Enemies spawn on the Enemy layer, which the ground probe mask (World|Ground) excludes. On
            // the World layer the probe would start inside this body's own shape and self-hit, because
            // Godot shape queries, like Unity's, start inside colliders.
            var leaper = new LeapingAttacker
            {
                Name = "LandingLeaper",
                CollisionLayer = World.Layer.Enemy,
                // PPU + Y FLIP: Unity's (0, 20) metres up is (0, -2000) pixels in Godot.
                Position = World.V(new Vector2(0f, 20f)),
            };
            leaper.SetTuningData(LeapingAttackerData.Load());
            ConfigureFromDesign(leaper);
            AddBoxShape(leaper);
            var health = new Health { Name = nameof(Health) };
            // The archetype's own ceiling: MeleeGrunt and friends only ever call SetHealth, so a
            // hand-built one has to take the max from the same file the spawner reads (K7b).
            health.SetMaxHealth(LeapingAttackerData.Load().maxHealth);
            leaper.AddChild(health);
            Spawn(leaper);
            health.SetHealth(30f);

            SetPrivateField(leaper, "_isLeaping", true);

            // Mid-leap apex: vertical speed is near zero but there is no ground under the body.
            // PPU: Unity's 4 m/s becomes 400 px/s.
            leaper.Velocity = new Vector2(World.U(4f), 0f);
            InvokeNonPublic(leaper, "CheckLeapLanding");
            Assert.IsTrue(leaper.IsLeaping, "LeapingAttacker must not treat low apex velocity as ground contact.");

            // Falling, still nothing below. Y FLIP: Unity's -6 (downward, +Y up) is +600 here.
            leaper.Velocity = new Vector2(World.U(4f), World.U(6f));
            InvokeNonPublic(leaper, "CheckLeapLanding");
            Assert.IsTrue(leaper.IsLeaping, "LeapingAttacker should require a downward ground probe before landing.");
        }

        /// <summary>
        /// Unity called only the most-derived <c>Awake</c>, so hiding the base one with <c>new</c>
        /// silently skipped EnemyStateMachine's setup and the boss never left its initial state. Godot's
        /// <c>_Ready</c> has exactly the same hazard, and the ported boss spells the relationship as
        /// <c>public override void _Ready()</c> calling <c>base._Ready()</c> first.
        ///
        /// The Unity assertion compared <c>GetBaseDefinition().DeclaringType</c> against
        /// <c>EnemyStateMachine</c>. That no longer works: <c>_Ready</c> is virtual all the way up to
        /// <c>Godot.Node</c>, so the base definition is Node's. What is still checked by reflection is
        /// that the member is an override rather than a <c>new</c> (a <c>new</c> method is its own base
        /// definition), and the behavioural half is checked directly: only <c>base._Ready()</c> puts the
        /// boss in the Enemy group.
        /// </summary>
        [Test]
        public void WrathMiniBossInitializesItsBaseStateMachine()
        {
            MethodInfo ready = typeof(WrathMiniBoss).GetMethod("_Ready", InstanceMembers);
            Assert.NotNull(ready, "WrathMiniBoss should declare _Ready.");
            Assert.AreNotSame(ready, ready.GetBaseDefinition(),
                "WrathMiniBoss._Ready must override EnemyStateMachine._Ready, not hide it with `new`, or the base state machine never initializes.");
            Assert.NotNull(typeof(EnemyStateMachine).GetMethod("_Ready", InstanceMembers),
                "EnemyStateMachine should still declare the _Ready the boss overrides.");

            Assert.IsNull(typeof(WrathMiniBoss).GetField("_sr", InstanceMembers),
                "WrathMiniBoss must not shadow the inherited _sr field.");

            WrathMiniBoss behaviour = CreateWrathMiniBoss(WrathMiniBossData.Load());
            Assert.IsTrue(behaviour.IsInGroup(World.Group.Enemy),
                "base._Ready puts the enemy in the Enemy group; a boss outside it never ran the base state machine's setup.");
            Assert.AreEqual(EnemyState.Idle, behaviour.CurrentState, "A freshly built boss should start Idle.");

            InvokeNonPublic(behaviour, "EnterDeadState");
            Assert.AreEqual(EnemyState.Dead, behaviour.CurrentState, "Depleting boss health should drive the shared EnemyState.Dead transition.");
        }

        [Test]
        public void WrathMiniBossPreservesCompletedAttackCooldown()
        {
            WrathMiniBossData tuning = WrathMiniBossData.Load();
            // Distinct from every other cooldown and from the AttackType.None fallback of 2f. Seconds,
            // so no PPU scaling.
            tuning.slamCooldown = 3.75f;
            tuning.phaseAttackCooldownMultiplier = 1f;

            WrathMiniBoss behaviour = CreateWrathMiniBoss(tuning);
            SetPrivateField(behaviour, "_currentAttack", ParsePrivateEnum(typeof(WrathMiniBoss), "AttackType", "Slam"));
            SetPrivateField(behaviour, "_isAttacking", true);

            InvokeNonPublic(behaviour, "EndAttack");

            var cooldown = (float)GetPrivateField(behaviour, "_attackCooldownTimer");
            Assert.IsTrue(
                Mathf.IsEqualApprox(cooldown, 3.75f),
                "WrathMiniBoss must preserve the completed attack type until its cooldown is selected; got " + cooldown + " instead of the slam cooldown.");
        }

        /// <summary>
        /// Frame-driven telegraphs must advance by the render frame delta. Comparing against the very
        /// same <see cref="GameClock.DeltaTime"/> the boss reads this frame fails loudly if it ever
        /// switches to the fixed step, without depending on what the headless frame delta happens to be.
        /// Unity's <c>Time.deltaTime</c> is <c>GameClock.DeltaTime</c> here.
        /// </summary>
        [Test]
        public void WrathMiniBossTelegraphUsesRenderFrameDeltaTime()
        {
            WrathMiniBoss behaviour = CreateWrathMiniBoss(WrathMiniBossData.Load());
            SetPrivateField(behaviour, "_currentAttack", ParsePrivateEnum(typeof(WrathMiniBoss), "AttackType", "Slash"));
            SetPrivateField(behaviour, "_telegraphTimer", 10f);

            float expectedStep = GameClock.DeltaTime;
            InvokeNonPublic(behaviour, "UpdateAttackTelegraph");
            float consumed = 10f - (float)GetPrivateField(behaviour, "_telegraphTimer");

            Assert.IsTrue(
                Mathf.IsEqualApprox(consumed, expectedStep),
                "Frame-driven boss telegraphs should advance with GameClock.DeltaTime, not a fixed step per render frame; consumed " + consumed + " against a frame delta of " + expectedStep + ".");
        }

        /// <summary>
        /// Unity's <c>InvokeNonPublic(behaviour, "Awake")</c> is gone: adding the boss to the tree runs
        /// <c>_Ready</c>. <c>maxHealth</c> is written before that happens, because
        /// <see cref="Health"/>'s own <c>_Ready</c> refills to max and runs before its parent's.
        /// </summary>
        private WrathMiniBoss CreateWrathMiniBoss(WrathMiniBossData tuning)
        {
            var boss = new WrathMiniBoss { Name = "WrathMiniBossTest" };
            AddBoxShape(boss);
            var health = new Health { Name = nameof(Health) };
            boss.AddChild(health);
            health.SetMaxHealth(tuning.maxHealthBoss);
            boss.SetTuningData(tuning);

            // Both before Spawn: _Ready refuses to run the fight without either of them.
            boss.SetEncounterData(BossEncounterData.Load("Design/WrathEncounter"));
            ConfigureFromDesign(boss);
            Spawn(boss);
            return boss;
        }

        // ---------------------------------------------------------------------------------------
        // Shared feedback systems
        // ---------------------------------------------------------------------------------------

        [Test]
        public void PlayerAttackAnimatorOnlyExposesTimingSignals()
        {
            MethodInfo enable = typeof(PlayerAttackAnimator2D).GetMethod("AnimationEvent_EnableHitbox");
            MethodInfo disable = typeof(PlayerAttackAnimator2D).GetMethod("AnimationEvent_DisableHitbox");
            MethodInfo end = typeof(PlayerAttackAnimator2D).GetMethod("AnimationEvent_AttackEnd");

            Assert.NotNull(enable, "PlayerAttackAnimator2D should expose AnimationEvent_EnableHitbox.");
            Assert.NotNull(disable, "PlayerAttackAnimator2D should expose AnimationEvent_DisableHitbox.");
            Assert.NotNull(end, "PlayerAttackAnimator2D should expose AnimationEvent_AttackEnd.");
            Assert.IsNull(typeof(PlayerAttackAnimator2D).GetMethod("ApplyDamage"), "PlayerAttackAnimator2D must not expose damage application.");
        }

        [Test]
        public void CombatFeedbackCanConsumeDamageResult()
        {
            Node2D attacker = Spawn(new Node2D { Name = "Attacker" });

            var target = new Node2D { Name = "FeedbackTarget" };
            var health = new Health { Name = nameof(Health) };
            target.AddChild(health);
            var feedback = new CombatFeedback { Name = nameof(CombatFeedback) };
            target.AddChild(feedback);
            Spawn(target);
            PlayerFixture.Configure(health, 100f);

            bool invoked = false;
            feedback.OnHitFeedback += () => invoked = true;

            DamageResult result = CombatResolver.Resolve(CreateRequest(attacker, target));
            MethodInfo play = typeof(CombatFeedback).GetMethod("Play");
            Assert.NotNull(play, "CombatFeedback should expose Play(DamageResult).");
            play.Invoke(feedback, new object[] { result });

            Assert.IsTrue(invoked, "CombatFeedback.Play should invoke hit feedback for applied damage.");
        }

        [Test]
        public void CameraShakeExposesNamedPresets()
        {
            Type presetType = ResolveType("MyGame.Combat.CameraShakePreset");
            Assert.NotNull(presetType, "CameraShakePreset enum should exist.");
            Assert.IsTrue(Enum.IsDefined(presetType, "LightHit"), "CameraShakePreset should include LightHit.");
            Assert.IsTrue(Enum.IsDefined(presetType, "HeavyHit"), "CameraShakePreset should include HeavyHit.");
            Assert.IsTrue(Enum.IsDefined(presetType, "Invulnerable"), "CameraShakePreset should include Invulnerable.");
            Assert.IsTrue(Enum.IsDefined(presetType, "BossPhase"), "CameraShakePreset should include BossPhase.");
            Assert.IsTrue(Enum.IsDefined(presetType, "BossSlam"), "CameraShakePreset should include BossSlam.");

            MethodInfo method = typeof(CameraShake).GetMethod("TriggerShake", new[] { presetType });
            Assert.NotNull(method, "CameraShake should expose TriggerShake(CameraShakePreset).");
        }

        [Test]
        public void GraphicsOptionsPresetsAndCustomRoundTrip()
        {
            // Applying options writes the root viewport, the window vsync mode and PlayerPrefs, so the
            // suite puts all three back. Unity's QualitySettings.vSyncCount is the window's vsync mode here.
            bool originalShake = GraphicsOptions.ScreenShake;
            bool originalHitStop = GraphicsOptions.HitStop;
            bool originalHitFlash = GraphicsOptions.HitFlash;
            int originalMsaa = GraphicsOptions.Msaa;
            float originalRenderScale = GraphicsOptions.RenderScale;
            bool originalVSync = GraphicsOptions.VSync;
            DisplayServer.VSyncMode originalVSyncMode = DisplayServer.WindowGetVsyncMode();

            try
            {
                GraphicsOptions.ApplyPreset(GraphicsPreset.High);
                Assert.AreEqual(GraphicsPreset.High, GraphicsOptions.Preset, "High preset should read back as High.");
                Assert.IsTrue(
                    GraphicsOptions.ScreenShake && GraphicsOptions.HitStop && GraphicsOptions.Msaa == 4,
                    "High preset should turn effects on and use 4x MSAA.");

                GraphicsOptions.SetScreenShake(false);
                Assert.AreEqual(GraphicsPreset.Custom, GraphicsOptions.Preset, "Changing a single option should read back as Custom.");

                GraphicsOptions.ApplyPreset(GraphicsPreset.Low);
                Assert.AreEqual(GraphicsPreset.Low, GraphicsOptions.Preset, "Low preset should read back as Low.");
                Assert.IsTrue(
                    !GraphicsOptions.HitFlash && Mathf.IsEqualApprox(GraphicsOptions.RenderScale, 0.6f),
                    "Low preset should turn effects off and drop the render scale.");

                GraphicsOptions.SetMsaa(GraphicsOptions.NextStep(GraphicsOptions.MsaaSteps, GraphicsOptions.Msaa));
                Assert.AreEqual(2, GraphicsOptions.Msaa, "MSAA should cycle from off to 2x.");

                GraphicsOptions.LoadAndApply();
                Assert.AreEqual(2, GraphicsOptions.Msaa, "Options should survive a reload from PlayerPrefs.");
            }
            finally
            {
                GraphicsOptions.SetScreenShake(originalShake);
                GraphicsOptions.SetHitStop(originalHitStop);
                GraphicsOptions.SetHitFlash(originalHitFlash);
                GraphicsOptions.SetMsaa(originalMsaa);
                GraphicsOptions.SetRenderScale(originalRenderScale);
                GraphicsOptions.SetVSync(originalVSync);
                DisplayServer.WindowSetVsyncMode(originalVSyncMode);
            }
        }

        [Test]
        public void AudioFeedbackCanPlayWithoutConfiguredClips()
        {
            Type cueType = ResolveType("MyGame.Combat.AudioFeedbackCue");
            Type feedbackType = ResolveType("MyGame.Combat.AudioFeedback");
            Assert.NotNull(cueType, "AudioFeedbackCue enum should exist.");
            Assert.NotNull(feedbackType, "AudioFeedback component should exist.");
            Assert.IsTrue(Enum.IsDefined(cueType, "LightHit"), "AudioFeedbackCue should include LightHit.");
            Assert.IsTrue(Enum.IsDefined(cueType, "HeavyHit"), "AudioFeedbackCue should include HeavyHit.");
            Assert.IsTrue(Enum.IsDefined(cueType, "Dodge"), "AudioFeedbackCue should include Dodge.");
            Assert.IsTrue(Enum.IsDefined(cueType, "Parry"), "AudioFeedbackCue should include Parry.");
            Assert.IsTrue(Enum.IsDefined(cueType, "Respawn"), "AudioFeedbackCue should include Respawn.");

            var feedback = (Node)Activator.CreateInstance(feedbackType);
            feedback.Name = "AudioFeedback";
            Spawn(feedback);

            MethodInfo play = feedbackType.GetMethod("Play", new[] { cueType });
            Assert.NotNull(play, "AudioFeedback should expose Play(AudioFeedbackCue).");
            object cue = Enum.Parse(cueType, "LightHit");
            Assert.DoesNotThrow(() => play.Invoke(feedback, new[] { cue }), "AudioFeedback should play a cue with no clips configured.");
        }

        /// <summary>
        /// The Unity test compared <c>Player.prefab</c>'s serialized <c>spiritTint</c> against the C#
        /// field default, because the prefab was what shipped and the field default was what a prefab
        /// REBUILD wrote back. Prefabs are not ported (see PORTING_GUIDE: the spawners build actors in
        /// code), so there is only one source left - and the value it holds is what this pins, by the
        /// same private field name.
        /// </summary>
        [Test]
        public void PlayerSpiritTintMatchesShippedValue()
        {
            var deathState = new DeathStateController { Name = "SpiritTintProbe" };
            PlayerFixture.Configure(deathState);
            Spawn(deathState);

            // The yardstick is the artist's file, not a second copy of the colour in this test. Until
            // K7b the component carried the literal and this compared code against code; the tint now
            // lives in Readability.json beside spiritPlatformColor and the component is handed it on
            // spawn, so what is worth asserting is that the handover still happens.
            GameplayReadabilityThemeData theme = GameplayReadabilityThemeData.Load();
            Assert.NotNull(theme, "Resources/Art/Readability.json has to load; the spirit tint is in it.");

            var fromComponent = (Color)GetPrivateField(deathState, "spiritTint");

            Assert.IsTrue(
                fromComponent.IsEqualApprox(theme.spiritTint),
                $"DeathStateController spiritTint {fromComponent} is no longer Readability.json's spiritTint {theme.spiritTint}; the player's whole spirit-form look changes with it.");
        }
    }
}
