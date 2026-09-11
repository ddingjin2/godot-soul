using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The HUD's animation timings. Three numbers that were <c>const</c> on
    /// <c>MyGame.UI.GameplayHud</c> - how long the ghost gauge holds after a hit, how long a whole bar
    /// takes to drain, and how long the fill flashes bone - so how a hit reads was behind a code change
    /// (PLAN_CLOSEOUT A8, decision D5). Colour is the other half of that move and went to
    /// <c>Resources/UI/MenuTheme.tres</c>, because a Theme is where Godot keeps a palette.
    ///
    /// UNITS: seconds, every one of them, and scaled seconds rather than unscaled - at TimeScale 0 the
    /// pause menu freezes both timers, which is what stops a hit taken on the last frame before Escape
    /// from draining away behind the menu. Nothing here is spatial, so <see cref="World.Ppu"/> does not
    /// come into it and <see cref="Load"/> converts nothing.
    ///
    /// A missing file is an error, not a second set of numbers: <see cref="Load"/> returns null,
    /// <c>GameplayTuningCatalog.IsComplete</c> goes false and the bootstrap refuses to build the arena
    /// (D1). A key the file leaves out reads as zero, which
    /// <c>Tests/Unit/DesignFileCompletenessTests</c> is what catches.
    /// </summary>
    public partial class UiTuningData : Resource
    {
        /// <summary>Base name of the design file.</summary>
        public const string FileName = "UiTuning";

        /// <summary>Seconds the ghost gauge stays where the bar was before it starts chasing it down. The pause that makes the size of a hit readable.</summary>
        [Export] public float ghostHoldSeconds;

        /// <summary>Seconds a full bar of ghost takes to drain. A constant rate rather than a lerp, so a scratch is proportionally quicker.</summary>
        [Export] public float ghostDrainSeconds;

        /// <summary>Seconds the health fill flashes bone on taking damage.</summary>
        [Export] public float hitFlashSeconds;

        /// <summary>The authored file, or null when it is missing - which the loader has already reported.</summary>
        public static UiTuningData Load(string path = GameplayTuningCatalog.DesignResourceFolder + FileName)
        {
            return Res.LoadJson<UiTuningData>(path);
        }
    }
}
