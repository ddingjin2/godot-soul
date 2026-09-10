using Godot;
using MyGame.Core;
using MyGame.Enemy;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The fallbacks. Every number here is what a caller gets when the design JSON it wanted is not on
    /// disk - which is every synthetic actor the test runners build.
    ///
    /// UNITS: the three reach constants are in <b>pixels</b>, because they are handed straight to
    /// runtime components that work in pixels. They are written as <c>metres * World.Ppu</c> so the
    /// authored number is still readable next to <c>WorldTuning.json</c>, which holds the metres.
    /// The <c>Create*</c> factories below write Unity metres into the tuning object and then call
    /// <c>ScaleToPixels()</c>, exactly as <c>EnemyTuningData.LoadFrom</c> does for an authored file -
    /// so a defaulted enemy and a tuned one are in the same space.
    /// </summary>
    public static class GameplayTuningDefaults
    {
        /// <summary>
        /// How long a soul stain refuses the player after they are back on their feet. The checkpoint
        /// sits where the player spawns, so dying there puts the respawn straight back on top of the
        /// stain; without a beat in between, the death and the recovery are the same moment.
        /// Long enough that respawning is never mistaken for walking back, short enough that the walk
        /// back itself is never blocked.
        /// The fallback. <c>PlayerResources.json</c> owns the authored value through
        /// <c>PlayerResourceData.soulStainPickupDelay</c>; this is what a synthetic player built without a
        /// tuning asset gets, which is every player the test runners make.
        /// Seconds, so it is not scaled.
        /// </summary>
        public const float SoulStainPickupDelay = 0.2f;

        /// <summary>
        /// Reach of a checkpoint zone's trigger when the component has to build its own collider. Matches
        /// the 1.5 the test agent already treats as interaction range in <c>MyGameStateProbe</c>, so a zone
        /// lights up at the distance the agent thinks it should.
        /// An authored collider is never resized by this; the constant only covers the component being
        /// dropped on a bare object.
        /// The fallback. <c>WorldTuning.json</c> owns the authored value through
        /// <c>WorldTuningData.checkpointZoneRadius</c>; this is what a zone built with no catalog gets.
        /// </summary>
        public const float CheckpointZoneRadius = 1.5f * World.Ppu;

        /// <summary>
        /// How far the player can reach to grab a lock-on target. One unit past the ranged caster's 7
        /// detection range on purpose: an enemy that has decided it can shoot the player must always be
        /// one that the player can turn and answer, or lock-on teaches the wrong lesson about reach.
        /// The fallback for <c>WorldTuningData.lockOnRange</c>.
        /// </summary>
        public const float LockOnRange = 8f * World.Ppu;

        /// <summary>
        /// How far a held target may drift before the lock drops. Deliberately wider than
        /// <see cref="LockOnRange"/>: equal ranges would make a target hovering exactly at the edge
        /// flicker in and out of lock every frame it crosses the line.
        /// The fallback for <c>WorldTuningData.lockOnBreakRange</c>.
        /// </summary>
        public const float LockOnBreakRange = 11f * World.Ppu;

        public static MeleeGruntData CreateMeleeGrunt(Color enemyColor)
        {
            var tuning = new MeleeGruntData
            {
                maxHealth = 40f,
                moveSpeed = 2.5f,
                attackDamage = 12f,
                attackKnockback = 5f,
                detectionRange = 5f,
                attackRange = 1.2f,
                telegraphTime = 0.85f,
                attackDuration = 0.35f,
                attackCooldown = 1.35f,
                stunDuration = 0.8f,
                enemyColor = enemyColor,
                patrolDistance = 2.5f,
                patrolIdleTime = 0.8f,
                attackRadius = 0.7f,
                // Roughly one light combo, so grunts break to pressure but not to a single poke.
                maxPoise = 25f,
                soulReward = 20,
            };

            tuning.ScaleToPixels();
            return tuning;
        }

        public static LeapingAttackerData CreateLeapingAttacker()
        {
            var tuning = new LeapingAttackerData
            {
                maxHealth = 35f,
                moveSpeed = 2f,
                attackDamage = 15f,
                attackKnockback = 6f,
                detectionRange = 6f,
                attackRange = 1f,
                telegraphTime = 1f,
                attackDuration = 0.4f,
                attackCooldown = 2f,
                stunDuration = 0.7f,
                enemyColor = new Color(0.47843137f, 0.29411766f, 0.17254902f),
                leapSpeed = 10f,
                leapHeight = 2.5f,
                maintainDistance = 3.5f,
                landingVulnerabilityTime = 0.6f,
                leapTelegraphTime = 1.1f,
                maxPoise = 20f,
                soulReward = 25,
            };

            tuning.ScaleToPixels();
            return tuning;
        }

        public static RangedCasterData CreateRangedCaster()
        {
            var tuning = new RangedCasterData
            {
                maxHealth = 30f,
                moveSpeed = 1.5f,
                attackDamage = 10f,
                attackKnockback = 3f,
                detectionRange = 7f,
                attackRange = 5f,
                telegraphTime = 1f,
                attackDuration = 0.2f,
                attackCooldown = 2.5f,
                stunDuration = 0.6f,
                enemyColor = new Color(0.29411766f, 0.27058825f, 0.3764706f),
                projectileSpeed = 5f,
                projectileDamage = 8f,
                minDistance = 4f,
                repositionDistance = 2f,
                castTelegraphTime = 0.8f,
                strafeSpeed = 1.5f,
                // The squishiest of the three: one clean hit interrupts a cast.
                maxPoise = 15f,
                soulReward = 25,
            };

            tuning.ScaleToPixels();
            return tuning;
        }

        public static WrathMiniBossData CreateWrathMiniBoss(Color bossColor)
        {
            var tuning = new WrathMiniBossData
            {
                maxHealthBoss = 180f,
                moveSpeedBoss = 2.5f,
                attackDamageSlash = 18f,
                attackDamageSlam = 22f,
                attackDamageRage = 12f,
                phaseThreshold = 0.5f,
                phaseSpeedMultiplier = 1.4f,
                phaseAttackCooldownMultiplier = 0.6f,
                slashTelegraphTime = 0.85f,
                slashDuration = 0.35f,
                slashRange = 1.6f,
                slashCooldown = 1.8f,
                slamTelegraphTime = 1f,
                slamDuration = 0.45f,
                slamRange = 2.2f,
                slamShockwaveForce = 7f,
                slamCooldown = 2.5f,
                rushTelegraphTime = 0.8f,
                rushSpeed = 11f,
                rushDuration = 0.5f,
                rushCooldown = 3f,
                bossColor = bossColor,
                bossBodySize = new Vector2(1.2f, 2f),
                stunDuration = 1f,
                // Deep enough that chip damage never staggers it: breaking the boss takes committed heavy
                // attacks, which is the trade the poise gauge exists to force.
                maxPoise = 90f,
                poiseRegenDelay = 3f,
                soulReward = 300,
            };

            tuning.ScaleToPixels();
            return tuning;
        }
    }
}
