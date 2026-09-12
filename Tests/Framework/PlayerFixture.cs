using MyGame.Combat;
using MyGame.Core;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// What <c>GameplayPlayerSpawner.Spawn</c> does for a shipped player, for the players the suites
    /// build by hand instead.
    ///
    /// Needed since K7b: the eleven player components carry no <c>[Export]</c> initialisers any more,
    /// and one that reaches the tree without its configure call reports itself and stops
    /// (PLAN_CLOSEOUT D1). The same shape as K5b's <see cref="EnemyFixture"/> - the fixture reads the
    /// shipped file rather than numbers written here, so a suite can never assert a number the game
    /// does not ship.
    /// </summary>
    /// <remarks>
    /// UNITS: every distance here is already pixels. <c>PlayerMovementData.Load</c> and
    /// <c>PlayerCombatData.Load</c> scale the authored metres on the way in, and the hitbox reach comes
    /// through <c>GameplayReadabilityDefaults.Create</c>, which is the layout file's one conversion
    /// boundary. Nothing below re-scales.
    /// <para>
    /// Order: either side of tree entry works, exactly as it does on the spawn path. The spawner
    /// parents the rig first and tunes it afterwards; <c>Health._Ready</c>, <c>StaminaSystem._Ready</c>
    /// and <c>Poise._Ready</c> refill from a ceiling that is zero until the configure call has run, and
    /// every configure call below refills too - so a fixture that calls these after <c>Spawn</c> gets
    /// the same numbers as one that calls them before.
    /// </para>
    /// </remarks>
    public static class PlayerFixture
    {
        /// <summary>Spawner line 114-115: the ceiling and the starting value, both from the resource file.</summary>
        public static Health Configure(Health health)
        {
            PlayerResourceData resources = Resources();
            health.SetMaxHealth(resources.maxHealth);
            health.SetHealth(resources.startingHealth);
            return health;
        }

        /// <summary>
        /// An actor whose health is not the player's - an enemy, a boss, a target that only has to
        /// survive a known number of hits. The number is the caller's, because it is the archetype's
        /// own <c>maxHealth</c> or the shape of the test; only the pair of calls is shared.
        /// </summary>
        public static Health Configure(Health health, float maxHealth)
        {
            health.SetMaxHealth(maxHealth);
            health.SetHealth(maxHealth);
            return health;
        }

        /// <summary>Spawner line 119-120.</summary>
        public static StaminaSystem Configure(StaminaSystem stamina)
        {
            stamina.ApplyTuning(Resources());
            stamina.SetStamina(stamina.MaxStamina);
            return stamina;
        }

        /// <summary>Spawner line 128 - the reach and the offset are the layout file's, in pixels.</summary>
        public static DamageHitbox2D Configure(DamageHitbox2D hitbox)
        {
            GameplayReadabilityDefaults readability = GameplayReadabilityDefaults.Create();
            Assert.NotNull(readability, "Resources/Art/Readability.json and Resources/Design/ReadabilityLayout.json both have to load; the player's hitbox reach is in the layout file.");

            hitbox.Configure(readability.PlayerHitboxRadius, readability.PlayerHitboxOffset, World.Layer.Enemy);
            return hitbox;
        }

        /// <summary>Spawner line 129.</summary>
        public static PlayerMotor2D Configure(PlayerMotor2D motor)
        {
            PlayerMovementData movement = PlayerMovementData.Load();
            Assert.NotNull(movement, "Resources/Design/PlayerMovement.json has to load; the player's whole movement is in it.");

            motor.ApplyTuning(movement);
            return motor;
        }

        /// <summary>Spawner lines 131-132 - two files, because the heal numbers are resources and the rest is combat.</summary>
        public static PlayerActionController Configure(PlayerActionController actions)
        {
            PlayerCombatData combat = PlayerCombatData.Load();
            Assert.NotNull(combat, "Resources/Design/PlayerCombat.json has to load; every attack, dodge and parry number is in it.");

            actions.ApplyTuning(combat);
            actions.ApplyResourceTuning(Resources());
            return actions;
        }

        /// <summary>Spawner lines 150-154.</summary>
        public static HumanityController Configure(HumanityController humanity)
        {
            PlayerResourceData resources = Resources();
            humanity.Configure(resources.maxHumanity, resources.lowHumanityThreshold,
                resources.humanityLossOnHit, resources.humanityRegenRate, resources.humanityRegenDelay);
            humanity.SetHumanity(resources.startingHumanity);
            return humanity;
        }

        /// <summary>Spawner line 158.</summary>
        public static SinResonanceController Configure(SinResonanceController sin)
        {
            SinTuningData tuning = SinTuningData.Load();
            Assert.NotNull(tuning, "Resources/Design/SinTuning.json has to load; the resonance ceiling and every sin row is in it.");

            sin.ApplyTuning(tuning);
            return sin;
        }

        /// <summary>Spawner line 228 - the player's poise, not an enemy's.</summary>
        public static Poise Configure(Poise poise)
        {
            PlayerResourceData resources = Resources();
            poise.Configure(resources.maxPoise, resources.poiseHeavyMultiplier, resources.poiseRegenDelay, resources.poiseRegenRate);
            return poise;
        }

        /// <summary>Spawner line 169 - four numbers from the resource file and the tint from the palette.</summary>
        public static DeathStateController Configure(DeathStateController death)
        {
            GameplayReadabilityDefaults readability = GameplayReadabilityDefaults.Create();
            Assert.NotNull(readability, "Resources/Art/Readability.json has to load; the spirit tint is in it.");

            death.ApplyTuning(Resources(), readability.SpiritTint);
            return death;
        }

        /// <summary>Spawner line 220 - both reaches are WorldTuning.json's, already in pixels.</summary>
        public static PlayerLockOn Configure(PlayerLockOn lockOn)
        {
            WorldTuningData world = WorldTuningData.Load();
            Assert.NotNull(world, "Resources/Design/WorldTuning.json has to load; both lock-on reaches are in it.");

            lockOn.Configure(world.lockOnRange, world.lockOnBreakRange);
            return lockOn;
        }

        private static PlayerResourceData Resources()
        {
            PlayerResourceData resources = PlayerResourceData.Load();
            Assert.NotNull(resources, "Resources/Design/PlayerResources.json has to load; health, stamina, humanity, poise and the death numbers are all in it.");
            return resources;
        }
    }
}
