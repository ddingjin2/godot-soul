using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// The P0 combat-stability suite, ported from the Unity edit-mode runner
    /// <c>Assets/_Project/Tests/Editor/P0CombatStabilityTestRunner.cs</c>.
    ///
    /// Two things about the Unity original shaped every test in it, and only one of them survives:
    ///
    /// 1. It reached into private members <b>by name</b> through reflection, and it still does. Those
    ///    names are invisible to the compiler, so a rename breaks a test here instead of the build -
    ///    which is the point. See <see cref="RequireField"/> for why the hierarchy walk is hand-rolled.
    /// 2. It ran in edit mode, where Unity never calls <c>Awake</c>/<c>OnEnable</c>. That is why the
    ///    Unity source built every actor by hand and invoked <c>Awake</c> through reflection.
    ///    <b>In Godot <c>_Ready</c> runs the moment a node enters the tree</b>, so that scaffolding is
    ///    gone: each fixture builds the node graph, adds the root to the tree, and lets <c>_Ready</c>
    ///    wire it. No frames pass inside a synchronous test, so nothing auto-ticks between the explicit
    ///    <c>Tick</c> calls the timing tests depend on.
    ///
    /// Distances and speeds are Unity metres in the source and pixels here: every converted number is
    /// marked <c>// PPU</c>, and every vertical one that changed sign is marked <c>// Y FLIP</c>.
    /// </summary>
    [TestFixture]
    public partial class P0CombatStabilityTests
    {
        private readonly List<Node> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            // A key the agent driver pressed stays pressed - Input.ActionPress writes global state that
            // outlives the fixture that set it, so an unreleased move_right would walk the *next* test's
            // player. See the UnityTestAgent block in the Gameplay partial for who sets this.
            _agentInput?.ReleaseAll();
            _agentInput = null;

            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (GodotObject.IsInstanceValid(_spawned[i]))
                {
                    _spawned[i].QueueFree();
                }
            }

            _spawned.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Attacks, dodges and parries
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AttackRemainsActiveForDurationAndCooldownDoesNotEndItEarly()
        {
            PlayerActionController actions = CreateActions(out _);

            actions.RequestAttack();
            actions.Tick(0.01f, 1f);
            Assert.IsTrue(actions.IsAttacking, "Attack should start.");

            actions.Tick(0.15f, 1f);
            Assert.IsTrue(actions.IsAttacking, "Attack should still be active before attackDuration elapses.");

            actions.Tick(0.15f, 1f);
            Assert.IsFalse(actions.IsAttacking, "Attack should end after attackDuration elapses.");
        }

        [Test]
        public void AttackHitboxUsesActiveWindowOnly()
        {
            PlayerActionController actions = CreateActions(out Node2D root);
            DamageHitbox2D hitbox = root.GetComponent<DamageHitbox2D>();

            actions.RequestAttack();
            actions.Tick(0.01f, 1f);
            Assert.IsTrue(GetHitboxActive(hitbox), "Hitbox should be active at attack start.");

            actions.Tick(0.11f, 1f);
            Assert.IsFalse(GetHitboxActive(hitbox), "Hitbox should turn off after attackActiveWindow.");
            Assert.IsTrue(actions.IsAttacking, "Attack should still be in recovery after hitbox active window.");
        }

        [Test]
        public void AttackExposesExplicitPhaseTransitions()
        {
            PlayerActionController actions = CreateActions(out _);
            PropertyInfo phaseProperty = typeof(PlayerActionController).GetProperty("CurrentAttackPhase");
            Assert.NotNull(phaseProperty, "PlayerActionController should expose CurrentAttackPhase.");

            actions.RequestAttack();
            actions.Tick(0.01f, 1f);
            Assert.AreEqual("Active", phaseProperty.GetValue(actions)?.ToString(), "Attack should enter Active while hitbox is live.");

            actions.Tick(0.11f, 1f);
            Assert.AreEqual("Recovery", phaseProperty.GetValue(actions)?.ToString(), "Attack should enter Recovery after active window.");

            actions.Tick(0.20f, 1f);
            Assert.AreEqual("Cooldown", phaseProperty.GetValue(actions)?.ToString(), "Attack should enter Cooldown after recovery.");

            actions.Tick(0.25f, 1f);
            Assert.AreEqual("None", phaseProperty.GetValue(actions)?.ToString(), "Attack phase should reset after cooldown.");
        }

        [Test]
        public void HeavyAttackUsesHeavyDamageProfile()
        {
            PlayerActionController actions = CreateActions(out Node2D root);
            DamageHitbox2D hitbox = root.GetComponent<DamageHitbox2D>();

            actions.RequestHeavyAttack();
            actions.Tick(0.01f, 1f);

            Assert.IsTrue(actions.IsAttacking, "Heavy attack should start.");
            // Damage is not a distance - no PPU scaling.
            Assert.Greater(GetHitboxDamage(hitbox), 20f, "Heavy attack should deal more than the base light attack damage.");
            Assert.AreEqual(DamageType.Heavy, GetHitboxDamageType(hitbox), "Heavy attack should mark its hitbox as Heavy damage.");
        }

        [Test]
        public void BufferedAttackFiresWhenCooldownEnds()
        {
            PlayerActionController actions = CreateActions(out _);

            actions.RequestAttack();
            actions.Tick(0.01f, 1f);
            Assert.IsTrue(actions.IsAttacking, "Attack should start.");

            actions.Tick(0.39f, 1f);
            Assert.IsFalse(actions.IsAttacking, "Attack should be over while its cooldown still runs.");

            actions.RequestAttack();
            actions.Tick(0.07f, 1f);
            Assert.IsFalse(actions.IsAttacking, "Attack requested during cooldown should wait, not fire early.");

            actions.Tick(0.05f, 1f);
            actions.Tick(0.01f, 1f);
            Assert.IsTrue(actions.IsAttacking, "Buffered attack should fire once the cooldown ends instead of being dropped.");
        }

        [Test]
        public void LightAttackChainsIntoComboStepInsideCancelWindow()
        {
            PlayerActionController actions = CreateActions(out Node2D root);
            DamageHitbox2D hitbox = root.GetComponent<DamageHitbox2D>();

            actions.RequestAttack();
            actions.Tick(0.01f, 1f);
            Assert.AreEqual(0, actions.ComboStep, "First attack should start at combo step 0.");

            actions.Tick(0.11f, 1f);
            Assert.IsTrue(actions.IsInAttackCancelWindow, "Attack should open its cancel window when the active window ends.");

            actions.RequestAttack();
            actions.Tick(0.01f, 1f);
            Assert.IsTrue(actions.IsAttacking, "Chained attack should keep the player attacking.");
            Assert.AreEqual(1, actions.ComboStep, "Light attack inside the cancel window should chain to the next combo step.");
            Assert.Greater(GetHitboxDamage(hitbox), 20f, "A later combo step should hit harder than the opener.");
        }

        [Test]
        public void DodgeCancelsAttackInsideCancelWindow()
        {
            PlayerActionController actions = CreateActions(out Node2D root);
            DamageHitbox2D hitbox = root.GetComponent<DamageHitbox2D>();

            actions.RequestAttack();
            actions.Tick(0.01f, 1f);
            actions.Tick(0.11f, 1f);
            Assert.IsTrue(actions.IsInAttackCancelWindow, "Attack should open its cancel window when the active window ends.");

            actions.RequestDodge();
            actions.Tick(0.01f, 1f);
            Assert.IsTrue(actions.IsDodging, "Dodge should be allowed inside the attack cancel window.");
            Assert.IsFalse(actions.IsAttacking, "Dodging out of an attack should cancel that attack.");
            Assert.IsFalse(GetHitboxActive(hitbox), "A cancelled attack must not leave its hitbox live.");
        }

        [Test]
        public void ParryWindowClosesOnItsOwnTimer()
        {
            PlayerActionController actions = CreateActions(out _);

            actions.RequestParry();
            actions.Tick(0.01f, 1f);
            Assert.IsTrue(actions.IsParrying, "Parry should open on request.");

            actions.Tick(0.25f, 1f);
            Assert.IsFalse(actions.IsParrying, "Parry window should close on the action tick timer, not on a scheduled Invoke.");
        }

        [Test]
        public void TakingDamageCancelsPlayerAttack()
        {
            Node2D attacker = Spawn(new Node2D { Name = "Attacker" });
            PlayerMotor2D target = CreatePlayerTarget();
            PlayerController2D player = target.GetComponent<PlayerController2D>();
            PlayerActionController actions = target.GetComponent<PlayerActionController>();
            DamageHitbox2D hitbox = target.GetComponent<DamageHitbox2D>();

            // THE TRAP, preserved from the Unity source and from the Godot port: PlayerController2D's
            // _Ready re-runs actions.Initialize with its own (null) damageHitbox, clobbering anything
            // wired before the player entered the tree. SetDamageHitbox after the fact is the only wiring
            // that survives - see INTEGRATION_NOTES, MyGame.Player.
            player.SetDamageHitbox(hitbox);

            player.RequestAttack();
            actions.Tick(0.01f, 1f);
            Assert.IsTrue(actions.IsAttacking, "Attack should start.");
            Assert.IsTrue(GetHitboxActive(hitbox), "Attack should open its hitbox.");

            DamageResult result = CombatResolver.Resolve(CreateRequest(attacker, target));

            Assert.IsTrue(result.Applied, "Unguarded damage should apply to the player.");
            Assert.IsFalse(actions.IsAttacking, "Taking a hit should cancel the player's attack.");
            Assert.IsFalse(GetHitboxActive(hitbox), "An interrupted attack must not leave its hitbox live.");
        }

        [Test]
        public void ResolveReportsParryWithoutApplyingDamage()
        {
            Node2D attacker = Spawn(new Node2D { Name = "Attacker" });
            PlayerMotor2D target = CreatePlayerTarget();
            PlayerController2D player = target.GetComponent<PlayerController2D>();
            PlayerActionController actions = target.GetComponent<PlayerActionController>();
            Health health = target.GetComponent<Health>();
            float startHealth = health.CurrentHealth;

            player.RequestParry();
            actions.Tick(0.01f, 1f);

            DamageResult result = CombatResolver.Resolve(CreateRequest(attacker, target));

            Assert.IsTrue(result.WasParried, "Resolver should report parry.");
            Assert.IsFalse(result.Applied, "Parried damage should not apply.");
            Assert.IsTrue(Mathf.IsEqualApprox(health.CurrentHealth, startHealth), "Parried damage should not change health.");
        }

        [Test]
        public void ResolveReportsPerfectParryDuringOpeningWindow()
        {
            Node2D attacker = Spawn(new Node2D { Name = "Attacker" });
            PlayerMotor2D target = CreatePlayerTarget();
            PlayerController2D player = target.GetComponent<PlayerController2D>();
            PlayerActionController actions = target.GetComponent<PlayerActionController>();

            player.RequestParry();
            actions.Tick(0.01f, 1f);

            DamageResult result = CombatResolver.Resolve(CreateRequest(attacker, target));

            Assert.IsTrue(result.WasParried, "Resolver should report parry.");
            Assert.IsTrue(result.WasPerfectParried, "Resolver should report perfect parry during the opening parry window.");
        }

        [Test]
        public void ResolveBlocksInvulnerablePlayerDamage()
        {
            Node2D attacker = Spawn(new Node2D { Name = "Attacker" });
            PlayerMotor2D target = CreatePlayerTarget();
            PlayerController2D player = target.GetComponent<PlayerController2D>();
            PlayerActionController actions = target.GetComponent<PlayerActionController>();
            Health health = target.GetComponent<Health>();
            float startHealth = health.CurrentHealth;

            player.RequestDodge();
            actions.Tick(0.01f, 1f);

            DamageResult result = CombatResolver.Resolve(CreateRequest(attacker, target));

            Assert.IsFalse(result.WasParried, "Invulnerable dodge should not report parry.");
            Assert.IsFalse(result.Applied, "Invulnerable player damage should not apply.");
            Assert.IsTrue(Mathf.IsEqualApprox(health.CurrentHealth, startHealth), "Invulnerable damage should not change health.");
        }

        [Test]
        public void ResolveUsesDamageReceiverWhenTargetHasNoHealth()
        {
            Node2D attacker = Spawn(new Node2D { Name = "Attacker" });

            var root = new Node2D { Name = "EnemyRoot" };
            var health = new Health { Name = nameof(Health) };
            root.AddChild(health);

            // Unity put the hurtbox on a child GameObject; here it is a child node with the receiver
            // hanging under it, which is the shape NodeExt.GetComponent reads back.
            var hurtbox = new Node2D { Name = "EnemyHurtbox" };
            root.AddChild(hurtbox);

            Type receiverType = ResolveType("MyGame.Combat.DamageReceiver");
            Assert.NotNull(receiverType, "DamageReceiver type should exist.");
            var receiver = (Node)Activator.CreateInstance(receiverType);
            receiver.Name = receiverType.Name;
            hurtbox.AddChild(receiver);

            Spawn(root);

            health.SetMaxHealth(100f);
            health.SetHealth(100f);

            MethodInfo initialize = receiverType.GetMethod("Initialize");
            Assert.NotNull(initialize, "DamageReceiver should expose Initialize(Health).");
            initialize.Invoke(receiver, new object[] { health });

            DamageResult result = CombatResolver.Resolve(CreateRequest(attacker, hurtbox));

            Assert.IsTrue(result.Applied, "Resolver should apply damage through DamageReceiver.");
            Assert.AreSame(health, result.TargetHealth, "Resolver result should expose receiver health.");
            Assert.IsTrue(Mathf.IsEqualApprox(health.CurrentHealth, 90f), "Receiver-routed damage should reduce assigned health.");
        }

        /// <summary>
        /// <see cref="PlayerStateMachine"/> is a plain class composed into <see cref="PlayerController2D"/>
        /// in this port, not a component, so the Unity <c>GetComponent&lt;PlayerStateMachine&gt;()</c>
        /// becomes a read of the controller's private <c>stateMachine</c> field - the same by-name
        /// reflection the rest of this file uses.
        /// </summary>
        [Test]
        public void PlayerStateMachineReportsActionStates()
        {
            PlayerMotor2D target = CreatePlayerTarget();
            PlayerController2D player = target.GetComponent<PlayerController2D>();
            PlayerActionController actions = target.GetComponent<PlayerActionController>();
            var stateMachine = (PlayerStateMachine)GetPrivateField(player, "stateMachine");

            Assert.NotNull(stateMachine, "Player target should include PlayerStateMachine.");
            stateMachine.UpdateState(player);
            Assert.AreEqual(PlayerState.Airborne, stateMachine.CurrentState, "Synthetic player without ground contact should report Airborne.");

            player.RequestParry();
            actions.Tick(0.01f, 1f);
            stateMachine.UpdateState(player);
            Assert.AreEqual(PlayerState.Parry, stateMachine.CurrentState, "PlayerStateMachine should report Parry during parry window.");

            actions.ResetActionState();
            player.RequestDodge();
            actions.Tick(0.01f, 1f);
            stateMachine.UpdateState(player);
            Assert.AreEqual(PlayerState.Dash, stateMachine.CurrentState, "PlayerStateMachine should report Dash during dodge/dash.");
        }

        /// <summary>
        /// Unity's <c>rb.gravityScale == 0</c> assertion has no counterpart: enemies and the player are
        /// <see cref="CharacterBody2D"/> here and a CharacterBody2D applies no gravity of its own, which
        /// is why <see cref="PlayerMotor2D"/> has no gravityScale call to make. The invariant the test
        /// was protecting - exactly one gravity source, and starting or resetting an action must not
        /// introduce a second - is retargeted onto the two things that could reintroduce one: the project
        /// default gravity, and the velocity the dodge authored surviving the action controller untouched.
        /// </summary>
        [Test]
        public void PlayerMotorUsesSingleGravitySource()
        {
            var motor = new PlayerMotor2D { Name = "PlayerMotorGravityTest" };
            AddBoxShape(motor);
            var stamina = new StaminaSystem { Name = nameof(StaminaSystem) };
            motor.AddChild(stamina);
            var hitbox = new DamageHitbox2D { Name = nameof(DamageHitbox2D) };
            motor.AddChild(hitbox);
            var actions = new PlayerActionController { Name = nameof(PlayerActionController) };
            motor.AddChild(actions);

            hitbox.Initialize(motor);
            // Initialize(Rigidbody2D, Collider2D) -> Initialize(), and the motor replaces the rigidbody
            // the dodge used to write velocity into. See INTEGRATION_NOTES, MyGame.Player.
            motor.Initialize();
            actions.Initialize(motor, stamina, hitbox);

            PlayerFixture.Configure(motor);
            PlayerFixture.Configure(stamina);
            PlayerFixture.Configure(hitbox);
            PlayerFixture.Configure(actions);

            Spawn(motor);
            stamina.SetStamina(stamina.MaxStamina);

            Assert.Zero(
                ProjectSettings.GetSetting("physics/2d/default_gravity").AsSingle(),
                "project.godot must leave default_gravity at 0: PlayerMotor2D applies its own, and an engine gravity on top is the second source this test exists to forbid.");

            actions.RequestDodge();
            actions.Tick(0.01f, 1f);
            // PPU: dodgeSpeed is World.U(15) px/s, so a flat dodge authors a purely horizontal velocity.
            // Y FLIP is not involved - a level dodge has no vertical component in either engine.
            Assert.IsTrue(actions.IsDodging, "Dodge should start.");
            Assert.Zero(motor.Velocity.Y, "Starting a dodge must not have any gravity folded into it - the motor's FixedTick is the only place gravity is applied.");

            actions.Tick(0.5f, 1f);
            actions.ResetActionState();
            Assert.Zero(motor.Velocity.Y, "PlayerActionController should not introduce vertical velocity after dodge or reset.");
        }

        // ---------------------------------------------------------------------------------------
        // Fixtures
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The Unity fixture was one GameObject carrying Rigidbody2D, StaminaSystem, DamageHitbox2D and
        /// PlayerActionController. Here the actor root <b>is</b> <see cref="PlayerMotor2D"/> (the player
        /// body is the motor) and the other three are child nodes, which is the layout
        /// <c>NodeExt.GetComponent</c> reads back.
        /// </summary>
        private PlayerActionController CreateActions(out Node2D root)
        {
            var motor = new PlayerMotor2D { Name = "PlayerActionControllerTest" };
            var stamina = new StaminaSystem { Name = nameof(StaminaSystem) };
            motor.AddChild(stamina);
            var hitbox = new DamageHitbox2D { Name = nameof(DamageHitbox2D) };
            motor.AddChild(hitbox);
            var actions = new PlayerActionController { Name = nameof(PlayerActionController) };
            motor.AddChild(actions);

            hitbox.Initialize(motor);
            actions.Initialize(motor, stamina, hitbox);

            // The shipped numbers, from the shipped files - the components carry none of their own
            // since K7b, and one that reaches the tree unconfigured reports itself and stops.
            PlayerFixture.Configure(motor);
            PlayerFixture.Configure(stamina);
            PlayerFixture.Configure(hitbox);
            PlayerFixture.Configure(actions);

            Spawn(motor);

            // StaminaSystem._Ready refills to max, so the authored value has to land after tree entry.
            stamina.SetStamina(stamina.MaxStamina);

            root = motor;
            return actions;
        }

        /// <summary>
        /// A player the resolver can hit: body, shape, health, stamina, hitbox, actions and the
        /// controller that answers <see cref="IDamageGuard"/>.
        /// </summary>
        /// <remarks>
        /// The Unity version left the damage hitbox unwired because <c>PlayerController2D.Awake</c>
        /// re-ran <c>actions.Initialize</c> with its own null hitbox and clobbered the one passed in.
        /// That trap is preserved in the Godot port on purpose (see INTEGRATION_NOTES), so callers that
        /// need a live hitbox still have to call <c>player.SetDamageHitbox(hitbox)</c> after the player
        /// is in the tree.
        /// </remarks>
        private PlayerMotor2D CreatePlayerTarget()
        {
            var motor = new PlayerMotor2D { Name = "Player" };
            // LayerMask.NameToLayer("Player") -> the body's own collision layer bit.
            motor.CollisionLayer = World.Layer.Player;
            motor.CollisionMask = World.Layer.World | World.Layer.Ground | World.Layer.Enemy;
            AddBoxShape(motor);

            var health = new Health { Name = nameof(Health) };
            motor.AddChild(health);
            var stamina = new StaminaSystem { Name = nameof(StaminaSystem) };
            motor.AddChild(stamina);
            var hitbox = new DamageHitbox2D { Name = nameof(DamageHitbox2D) };
            motor.AddChild(hitbox);
            var actions = new PlayerActionController { Name = nameof(PlayerActionController) };
            motor.AddChild(actions);
            var controller = new PlayerController2D { Name = nameof(PlayerController2D) };
            motor.AddChild(controller);

            hitbox.Initialize(motor);
            motor.Initialize();
            actions.Initialize(motor, stamina, hitbox);

            PlayerFixture.Configure(motor);
            PlayerFixture.Configure(hitbox);
            PlayerFixture.Configure(actions);

            Spawn(motor);

            // Health._Ready and StaminaSystem._Ready both refill to max, so the authored values land
            // after the node is in the tree rather than before it, as they did in Unity's edit mode.
            // PlayerResources.json is where the 100 / 100 used to be written by hand (K7b).
            PlayerFixture.Configure(health);
            PlayerFixture.Configure(stamina);

            return motor;
        }

        private static DamageRequest CreateRequest(Node attacker, Node target)
        {
            return new DamageRequest(
                attacker,
                target,
                Vector2.Zero,
                Vector2.Right,
                10f,
                // PPU: knockback reaching DamageRequest is already pixels/second in this port - the
                // Unity 1 metre/s is 100 px/s. INTEGRATION_NOTES, MyGame.Enemy.
                World.U(1f),
                DamageType.Standard);
        }

        private static bool GetHitboxActive(DamageHitbox2D hitbox)
        {
            FieldInfo field = typeof(DamageHitbox2D).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && (bool)field.GetValue(hitbox);
        }

        private static float GetHitboxDamage(DamageHitbox2D hitbox)
        {
            FieldInfo field = typeof(DamageHitbox2D).GetField("damage", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? (float)field.GetValue(hitbox) : 0f;
        }

        private static DamageType GetHitboxDamageType(DamageHitbox2D hitbox)
        {
            FieldInfo field = typeof(DamageHitbox2D).GetField("damageType", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? (DamageType)field.GetValue(hitbox) : DamageType.None;
        }

        /// <summary>Unity's <c>BoxCollider2D</c>. Sized to the shipped 0.6 x 1.0 metre actor body.</summary>
        private static CollisionShape2D AddBoxShape(Node2D body)
        {
            var shape = new CollisionShape2D
            {
                Name = nameof(CollisionShape2D),
                // PPU: 0.6 x 1.0 authored metres.
                Shape = new RectangleShape2D { Size = new Vector2(World.U(0.6f), World.U(1f)) },
            };

            body.AddChild(shape);
            return shape;
        }

        /// <summary>
        /// Adds an actor root to the running tree - which is what makes <c>_Ready</c> run - and queues it
        /// for release in <see cref="TearDown"/>. Unity's <c>new GameObject</c> plus <c>DestroyImmediate</c>.
        /// </summary>
        private T Spawn<T>(T node) where T : Node
        {
            TestContext.Tree.Root.AddChild(node);
            _spawned.Add(node);
            return node;
        }

        /// <summary>
        /// Kept as a local name because five call sites in this suite read better for it;
        /// <see cref="EnemyFixture.ConfigureFromDesign"/> is the shared one the PlayMode boss fixtures
        /// use as well.
        /// </summary>
        private static T ConfigureFromDesign<T>(T machine) where T : EnemyStateMachine
            => EnemyFixture.ConfigureFromDesign(machine);

        // ---------------------------------------------------------------------------------------
        // Reflection helpers - ported verbatim in intent from the Unity runner
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Resolves a type by full name across every loaded assembly. Names are not qualified with an
        /// assembly on purpose: the gameplay code was split into per-module assemblies in Unity and is
        /// one assembly here, and moving a type between them must not silently turn these lookups into
        /// null.
        /// </summary>
        private static Type ResolveType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Declared-only on purpose: these helpers walk the hierarchy one level at a time, so a member
        /// that hides a base member with `new` resolves to the derived one instead of going ambiguous.
        /// Still load-bearing in the port - <c>MeleeGrunt._sr</c>, <c>MeleeGrunt.MoveTowards</c> and
        /// <c>LeapingAttacker.MoveTowards</c> all shadow a base member.
        /// </summary>
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;

        private static FieldInfo RequireField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, InstanceMembers);
                if (field != null)
                {
                    return field;
                }
            }

            throw new AssertionException($"{type.Name} should keep a {name} field.");
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            RequireField(target.GetType(), name).SetValue(target, value);
        }

        private static object GetPrivateField(object target, string name)
        {
            return RequireField(target.GetType(), name).GetValue(target);
        }

        private static void InvokeNonPublic(object target, string methodName)
        {
            for (Type current = target.GetType(); current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(methodName, InstanceMembers, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    method.Invoke(target, null);
                    return;
                }
            }

            throw new AssertionException($"{target.GetType().Name} should keep a {methodName}() method.");
        }

        private static object ParsePrivateEnum(Type declaringType, string enumName, string valueName)
        {
            Type enumType = declaringType.GetNestedType(enumName, BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(enumType, $"{declaringType.Name} should keep its {enumName} enum.");
            return Enum.Parse(enumType, valueName);
        }

        /// <summary>
        /// Raises a field-like C# event from outside its declaring type. Unity's <c>UnityEvent</c> had a
        /// public <c>Invoke()</c>; the ported events are plain <c>event Action</c>s, so a test that has to
        /// fire one reaches the compiler-generated backing field by name - the same reflection idiom this
        /// file already uses for private state.
        /// </summary>
        private static void RaiseEvent(object target, string eventName)
        {
            var handler = RequireField(target.GetType(), eventName).GetValue(target) as Delegate;
            handler?.DynamicInvoke();
        }
    }

    /// <summary>
    /// <see cref="EnemyStateMachine"/> is abstract, and its <c>TransitionTo</c> is protected, so the
    /// terminal-Dead test needs a concrete subclass exactly as the Unity source did. Declared at
    /// namespace level rather than nested: Godot's C# source generator does not support nested node
    /// types. Unity's <c>InitializeForTest</c> (which invoked <c>Awake</c> by reflection) is gone -
    /// <c>_Ready</c> runs when the node enters the tree.
    /// </summary>
    public partial class TestEnemyStateMachine : EnemyStateMachine
    {
        /// <summary>
        /// Nothing to detect: the two tests that use this double put no player in the tree, and both
        /// drive the transitions they measure by hand. Zero rather than a number copied from an
        /// archetype's design file, which this double is not one of.
        /// </summary>
        protected override float GetDetectionRange() => 0f;

        public void TryTransitionForTest(EnemyState state)
        {
            TransitionTo(state);
        }
    }
}
