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
    /// at load and multiplies <c>checkpointZoneRadius</c>, <c>lockOnRange</c> and
    /// <c>lockOnBreakRange</c> by <see cref="World.Ppu"/>. There is nothing else in the file.
    /// </summary>
    public sealed partial class WorldTuningData : Resource
    {
        // The defaults below are in Unity metres, not pixels: they stand in for a field the JSON left
        // out, and ScaleToPixels runs over the whole object afterwards. The pixel-space twins of these
        // three numbers are the constants on GameplayTuningDefaults, which is what a caller with no
        // file at all uses.

        /// <summary>Reach of a checkpoint zone's trigger when the component builds its own collider. An authored collider is never resized by this.</summary>
        [Export] public float checkpointZoneRadius = 1.5f;

        /// <summary>How far the player can reach to grab a target. Beyond the ranged caster's own detection range, so a caster that can shoot can always be locked back.</summary>
        [Export] public float lockOnRange = 8f;

        /// <summary>How far a locked target may drift before the lock drops. Larger than lockOnRange so a target that steps one pixel past the grab range is not dropped mid-fight.</summary>
        [Export] public float lockOnBreakRange = 11f;

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
