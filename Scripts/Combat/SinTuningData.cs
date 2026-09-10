using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// The designer-owned half of sin resonance: what each sin does, and what a sin costs to run.
    /// Every number here was a serialized field on <see cref="SinResonanceController"/> before, which
    /// meant tuning a sin took a prefab edit and could not be reviewed as a diff. The component keeps
    /// those fields as its fallback, so a controller built without a catalog behaves exactly as it did.
    ///
    /// <c>sin</c> is stored as its enum integer, not its name: None 0, Wrath 1, Sloth 2, Pride 3,
    /// Gluttony 4, Greed 5, Envy 6, Lust 7. Chapter number and enum integer are not the same number -
    /// see <see cref="SinState"/>.
    /// </summary>
    /// <remarks>
    /// Unit scaling: nothing here is spatial. Resonance amounts, durations, costs and the per-sin
    /// multipliers are all unitless or seconds, so no field is scaled by <see cref="World.Ppu"/>.
    /// </remarks>
    public partial class SinTuningData : Resource
    {
        // Resonance
        [Export] public float maxResonance = 100f;
        [Export] public float resonancePerHit = 5f;
        [Export] public float resonancePerParry = 25f;
        [Export] public float resonancePerDamage = 3f;

        // Activation
        [Export] public float activeDuration = 5f;
        [Export] public float cooldownDuration = 10f;
        [Export] public float resonanceCost = 50f;
        [Export] public float humanityCostOnActivate = 5f;

        /// <summary>
        /// One row per sin, matched on its own sin field rather than on position. A sin with no row runs
        /// neutral. Not [Export]ed: <see cref="SinModifiers"/> stays a plain C# class (a JSON row, not an
        /// asset) and Godot only exports arrays of Resources. The JSON loader fills it either way.
        /// </summary>
        public SinModifiers[] sins;

        /// <summary>Reads <c>Resources/Design/SinTuning.json</c>. Null when the file is absent.</summary>
        public static SinTuningData Load(string path = "Design/SinTuning") => Res.LoadJson<SinTuningData>(path);
    }
}
