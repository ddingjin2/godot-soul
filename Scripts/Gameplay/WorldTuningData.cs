using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Gameplay reach and range numbers that belong to no single actor. These were constants on
    /// <see cref="GameplayTuningDefaults"/>, which put them out of a designer's hands; the constants stay
    /// as the fallback every synthetic test player gets, and this file is what the shipped game reads.
    ///
    /// UNITS: <c>WorldTuning.json</c> is authored in Unity metres. <see cref="ScaleToPixels"/> runs once
    /// at load and multiplies the spatial fields by <see cref="World.Ppu"/>: <c>checkpointZoneRadius</c>,
    /// <c>lockOnRange</c>, <c>lockOnBreakRange</c>, <c>enemyGravity</c> (m/s^2 -> px/s^2),
    /// <c>enemyDisengageDistance</c>, <c>enemyLedgeProbeForward</c>, <c>enemyLedgeProbeDepth</c> and
    /// <c>actorMoveAnimThreshold</c> (m/s -> px/s). Left alone, because they are seconds:
    /// <c>enemyIdleToPatrolTime</c>, <c>enemyInvestigateDuration</c>, <c>enemyRecoveryDuration</c> and
    /// <c>fallDeathRespawnLockout</c>.
    ///
    /// The enemy block is here rather than on an archetype because every archetype shares it, and it is
    /// pushed into <c>EnemyStateMachine</c> by <c>GameplayEnemySpawner</c> rather than read there:
    /// <c>MyGame.Enemy</c> sits below <c>MyGame.Gameplay</c> and may not reach up for this file.
    /// </summary>
    public sealed partial class WorldTuningData : Resource
    {
        // The defaults below are in Unity metres, not pixels: they stand in for a field the JSON left
        // out, and ScaleToPixels runs over the whole object afterwards. The pixel-space twins of the
        // first three are the constants on GameplayTuningDefaults, which is what a caller with no file
        // at all uses; the enemy block's twins are the initialisers on EnemyStateMachine, which is what
        // a synthetic enemy the spawner never touched keeps running.

        /// <summary>Reach of a checkpoint zone's trigger when the component builds its own collider. An authored collider is never resized by this.</summary>
        [Export] public float checkpointZoneRadius = 1.5f;

        /// <summary>How far the player can reach to grab a target. Beyond the ranged caster's own detection range, so a caster that can shoot can always be locked back.</summary>
        [Export] public float lockOnRange = 8f;

        /// <summary>How far a locked target may drift before the lock drops. Larger than lockOnRange so a target that steps one pixel past the grab range is not dropped mid-fight.</summary>
        [Export] public float lockOnBreakRange = 11f;

        // --- Shared enemy behaviour. Metres and seconds; see the UNITS block above. ---

        /// <summary>
        /// Downward acceleration every enemy body applies itself, in metres per second squared.
        /// project.godot leaves <c>default_gravity</c> at zero because each actor supplies its own, and
        /// the player's half of the same fall is authored in <c>PlayerMovement.json</c>. Unity's
        /// <c>Physics2D.gravity</c> default is what this shipped at.
        /// </summary>
        [Export] public float enemyGravity = 9.81f;

        /// <summary>How far a target may get before an engaged enemy gives up and drops to Recovery, in metres. Every archetype's re-engage range sits under this so there is hysteresis rather than flapping.</summary>
        [Export] public float enemyDisengageDistance = 8f;

        /// <summary>Seconds an idle enemy waits before it starts patrolling.</summary>
        [Export] public float enemyIdleToPatrolTime = 3f;

        /// <summary>Seconds an enemy spends looking at where it last heard something.</summary>
        [Export] public float enemyInvestigateDuration = 2f;

        /// <summary>Seconds an enemy stands in Recovery before it may act again.</summary>
        [Export] public float enemyRecoveryDuration = 1f;

        /// <summary>How far past its own edge the footing probe looks, in metres. A little over a body width, so the turn happens before the centre of mass is over the drop.</summary>
        [Export] public float enemyLedgeProbeForward = 0.35f;

        /// <summary>How far down the footing probe looks for a floor, in metres. Deliberately shallow: a step down is footing and a storey down is a ledge.</summary>
        [Export] public float enemyLedgeProbeDepth = 1.1f;

        /// <summary>Horizontal speed above which any actor - player or enemy - is running rather than standing, in metres per second.</summary>
        [Export] public float actorMoveAnimThreshold = 0.15f;

        /// <summary>Seconds the kill plane under the arena refuses to fire again after a pit death.</summary>
        [Export] public float fallDeathRespawnLockout = 1f;

        /// <summary>
        /// Guards against a second pass over the same instance - the scaling rewrites the authored
        /// fields in place, so running it twice would put every reach a hundred times too far out.
        /// </summary>
        private bool _scaledToPixels;

        /// <summary>Metres -> pixels, once.</summary>
        public void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            _scaledToPixels = true;

            checkpointZoneRadius = World.U(checkpointZoneRadius);
            lockOnRange = World.U(lockOnRange);
            lockOnBreakRange = World.U(lockOnBreakRange);

            enemyGravity = World.U(enemyGravity);
            enemyDisengageDistance = World.U(enemyDisengageDistance);
            enemyLedgeProbeForward = World.U(enemyLedgeProbeForward);
            enemyLedgeProbeDepth = World.U(enemyLedgeProbeDepth);
            actorMoveAnimThreshold = World.U(actorMoveAnimThreshold);
        }

        /// <summary>
        /// The authored file with the unit conversion folded in, or null when it is missing - which is
        /// what lets every caller keep falling back to <see cref="GameplayTuningDefaults"/>.
        /// </summary>
        public static WorldTuningData Load(string designPath = "Design/WorldTuning")
        {
            WorldTuningData data = Res.LoadJson<WorldTuningData>(designPath);
            data?.ScaleToPixels();
            return data;
        }
    }
}
