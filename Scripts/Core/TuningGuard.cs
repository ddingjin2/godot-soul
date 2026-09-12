using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// One shared report for the components that carry no <c>[Export]</c> initialisers any more (K7b):
    /// a node that reaches the tree without its configure call says so once and stops ticking, rather
    /// than running the game on zeroes nobody authored (PLAN_CLOSEOUT decision D1).
    /// </summary>
    /// <remarks>
    /// Called from a <c>CallDeferred</c> in each component's <c>_Ready</c>, never from <c>_Ready</c>
    /// itself. Both spawners parent the actor <em>first</em> and tune it afterwards - the player rig
    /// because every component's <c>_Ready</c> looks for its siblings
    /// (<c>GameplayPlayerSpawner.BuildPlayer</c>), the chapter boss because its health is decided inside
    /// <c>SetBossData</c> after <c>PlaceInWorld</c> - so a ready-time check would fire on every actor the
    /// game ships. The deferred call runs at the end of the frame, by which time the spawner's
    /// synchronous tuning has finished; only a hand-built fixture that advances a frame without
    /// configuring anything gets here. That is the same reasoning, and the same wording, as
    /// <c>RainbowChapterBossBehaviour.HasTheDataItNeeds</c> (K5).
    /// <para>
    /// <c>SetProcess</c>/<c>SetPhysicsProcess</c> are a no-op on a component with no process loop
    /// (<c>Health</c>, <c>DamageHitbox2D</c>); the error is the part that matters there.
    /// </para>
    /// </remarks>
    public static class TuningGuard
    {
        /// <summary>Reports and stops <paramref name="node"/> unless <paramref name="configured"/>.</summary>
        public static void Check(Node node, bool configured, string configureCall)
        {
            if (configured || node == null)
                return;

            GD.PushError($"{node.GetType().Name}: '{node.Name}' entered the tree without {configureCall}; it carries no authored numbers and does not run.");
            node.SetProcess(false);
            node.SetPhysicsProcess(false);
        }
    }
}
