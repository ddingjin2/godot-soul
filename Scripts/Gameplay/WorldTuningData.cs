using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Gameplay reach and range numbers that belong to no single actor. These were constants on a
    /// <c>GameplayTuningDefaults</c> table, which put them out of a designer's hands; that class is gone
    /// (PLAN_CLOSEOUT A4) and this file is the only place they exist.
    ///
    /// UNITS: <c>WorldTuning.json</c> is authored in Unity metres. <see cref="ScaleToPixels"/> runs once
    /// at load and multiplies the spatial fields by <see cref="World.Ppu"/>: <c>checkpointZoneRadius</c>,
    /// <c>lockOnRange</c>, <c>lockOnBreakRange</c>, <c>enemyGravity</c> (m/s^2 -> px/s^2),
    /// <c>enemyDisengageDistance</c>, <c>enemyLedgeProbeForward</c>, <c>enemyLedgeProbeDepth</c> and
    /// <c>actorMoveAnimThreshold</c> (m/s -> px/s), <c>cameraDeadZone</c> (a half-extent, so both
    /// components scale and neither flips), <c>cameraMaxFollowSpeed</c> (m/s -> px/s),
    /// <c>worldEdgeWallThickness</c>, <c>worldEdgeWallHeight</c> and <c>gatePortalOffsetX</c> (an X
    /// offset, so no flip).
    /// <c>cameraLookAhead</c> is the one field that is scaled <b>and</b> Y-flipped, through
    /// <see cref="World.V"/>: it is an offset, and an authored +1.2 (up) has to arrive as -120.
    /// Left alone, because they are seconds: <c>enemyIdleToPatrolTime</c>,
    /// <c>enemyInvestigateDuration</c>, <c>enemyRecoveryDuration</c>, <c>fallDeathRespawnLockout</c>,
    /// <c>cameraSmoothTime</c> and <c>actorIdleBobPeriod</c>; and <c>actorIdleBobAmplitude</c>, a
    /// fraction of the sprite's rest height rather than a distance.
    ///
    /// The enemy block is here rather than on an archetype because every archetype shares it, and it is
    /// pushed into <c>EnemyStateMachine</c> by <c>GameplayEnemySpawner</c> rather than read there:
    /// <c>MyGame.Enemy</c> sits below <c>MyGame.Gameplay</c> and may not reach up for this file.
    /// </summary>
    public sealed partial class WorldTuningData : Resource
    {
        /// <summary>Reach of a checkpoint zone's trigger when the component builds its own collider. An authored collider is never resized by this.</summary>
        [Export] public float checkpointZoneRadius;

        /// <summary>How far the player can reach to grab a target. Beyond the ranged caster's own detection range, so a caster that can shoot can always be locked back.</summary>
        [Export] public float lockOnRange;

        /// <summary>How far a locked target may drift before the lock drops. Larger than lockOnRange so a target that steps one pixel past the grab range is not dropped mid-fight.</summary>
        [Export] public float lockOnBreakRange;

        // --- Shared enemy behaviour. Metres and seconds; see the UNITS block above. ---

        /// <summary>
        /// Downward acceleration every enemy body applies itself, in metres per second squared.
        /// project.godot leaves <c>default_gravity</c> at zero because each actor supplies its own, and
        /// the player's half of the same fall is authored in <c>PlayerMovement.json</c>. Unity's
        /// <c>Physics2D.gravity</c> default is what this shipped at.
        /// </summary>
        [Export] public float enemyGravity;

        /// <summary>How far a target may get before an engaged enemy gives up and drops to Recovery, in metres. Every archetype's re-engage range sits under this so there is hysteresis rather than flapping.</summary>
        [Export] public float enemyDisengageDistance;

        /// <summary>Seconds an idle enemy waits before it starts patrolling.</summary>
        [Export] public float enemyIdleToPatrolTime;

        /// <summary>Seconds an enemy spends looking at where it last heard something.</summary>
        [Export] public float enemyInvestigateDuration;

        /// <summary>Seconds an enemy stands in Recovery before it may act again.</summary>
        [Export] public float enemyRecoveryDuration;

        /// <summary>How far past its own edge the footing probe looks, in metres. A little over a body width, so the turn happens before the centre of mass is over the drop.</summary>
        [Export] public float enemyLedgeProbeForward;

        /// <summary>How far down the footing probe looks for a floor, in metres. Deliberately shallow: a step down is footing and a storey down is a ledge.</summary>
        [Export] public float enemyLedgeProbeDepth;

        /// <summary>Horizontal speed above which any actor - player or enemy - is running rather than standing, in metres per second.</summary>
        [Export] public float actorMoveAnimThreshold;

        /// <summary>Seconds the kill plane under the arena refuses to fire again after a pit death.</summary>
        [Export] public float fallDeathRespawnLockout;

        // --- Camera feel. The bounds stay on SceneLayout, per arena; this is how the camera moves
        // inside them, shared by every arena. Metres and seconds; see the UNITS block above.

        /// <summary>Half-extents of the box the target may move inside before the camera follows, in metres.</summary>
        [Export] public Vector2 cameraDeadZone;

        /// <summary>How far ahead of the target the camera aims, in metres: x in the facing direction, y upward.</summary>
        [Export] public Vector2 cameraLookAhead;

        /// <summary>Seconds the critically damped follow takes to settle. The single biggest knob on how the camera feels.</summary>
        [Export] public float cameraSmoothTime;

        /// <summary>Fastest the camera may move to catch up, in metres per second.</summary>
        [Export] public float cameraMaxFollowSpeed;

        // --- Arena furniture every chapter shares. Metres.

        /// <summary>The invisible slab at each end of the floor, so walking off the side is not a death.</summary>
        [Export] public float worldEdgeWallThickness;

        /// <summary>Tall on purpose: chapter eight's climbs reach 9 units, and a wall a player can clear at the top of a tower is the same bug one screen higher.</summary>
        [Export] public float worldEdgeWallHeight;

        /// <summary>How far to the right of a bonfire its gate portal stands.</summary>
        [Export] public float gatePortalOffsetX;

        // --- The greybox breathing on any actor without pixel frames. Seconds and a fraction of the
        // rest height - explicitly not a distance, so neither is scaled.
        [Export] public float actorIdleBobPeriod;
        [Export] public float actorIdleBobAmplitude;

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

            cameraDeadZone = new Vector2(World.U(cameraDeadZone.X), World.U(cameraDeadZone.Y));
            cameraLookAhead = World.V(cameraLookAhead);
            cameraMaxFollowSpeed = World.U(cameraMaxFollowSpeed);

            worldEdgeWallThickness = World.U(worldEdgeWallThickness);
            worldEdgeWallHeight = World.U(worldEdgeWallHeight);
            gatePortalOffsetX = World.U(gatePortalOffsetX);
        }

        /// <summary>
        /// The authored file with the unit conversion folded in, or null when it is missing.
        /// </summary>
        public static WorldTuningData Load(string designPath = "Design/WorldTuning")
        {
            WorldTuningData data = Res.LoadJson<WorldTuningData>(designPath);
            data?.ScaleToPixels();
            return data;
        }
    }
}
