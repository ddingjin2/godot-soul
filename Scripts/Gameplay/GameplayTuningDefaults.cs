using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The last code copies of authored numbers: four constants that <c>GameplayPlayerSpawner</c>,
    /// <c>CheckpointZone</c> and <c>GameplaySoulDrop</c> still read when no tuning object reaches them.
    /// Stage K5 of <c>docs/migrations/scene-data/PLAN_CLOSEOUT.md</c> removes them.
    ///
    /// The four enemy factories that used to live beside them are gone (K1, decision D3). Anything that
    /// wants an archetype's numbers - the spawner, a test building a bare actor - loads the design file
    /// through that type's own <c>Load()</c>, which is also where the metres-to-pixels pass happens.
    ///
    /// UNITS: the three reach constants are in <b>pixels</b>, because they are handed straight to
    /// runtime components that work in pixels. They are written as <c>metres * World.Ppu</c> so the
    /// authored number is still readable next to <c>WorldTuning.json</c>, which holds the metres.
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
        /// tuning asset gets.
        /// Seconds, so it is not scaled.
        /// Mirrors <c>PlayerResources.json</c>'s <c>soulStainPickupDelay</c>; keep the two identical.
        /// </summary>
        public const float SoulStainPickupDelay = 0.35f;

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
    }
}
