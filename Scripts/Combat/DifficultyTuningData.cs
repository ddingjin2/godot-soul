using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// Designer-owned difficulty weights: what the player takes and what the enemies have on Easy
    /// and Hard, and how much each New Game+ lap adds. Loaded from
    /// <c>Resources/Design/DifficultyTuning.json</c>; a missing file is an error - <see cref="Load"/>
    /// returns null and the bootstrap refuses to build the arena. A key the file leaves out reads
    /// as zero.
    /// </summary>
    /// <remarks>
    /// UNITS: nothing here is spatial. Every field is a multiplier, so no field is scaled by
    /// <see cref="World.Ppu"/>. Normal is the implicit 1.0 and has no key - a Normal knob would be a
    /// second place to define what "unmodified" means.
    /// </remarks>
    public partial class DifficultyTuningData : Resource
    {
        /// <summary>Base name of the design file.</summary>
        public const string FileName = "DifficultyTuning";

        /// <summary>How much of an incoming hit the player takes.</summary>
        [Export] public float easyPlayerDamageTaken;
        [Export] public float hardPlayerDamageTaken;

        /// <summary>Enemy and boss health.</summary>
        [Export] public float easyEnemyHealth;
        [Export] public float hardEnemyHealth;

        /// <summary>Added to the enemy health multiplier per completed cycle of the road.</summary>
        [Export] public float newGamePlusEnemyHealthPerCycle;

        public static DifficultyTuningData Load(string path = "Design/" + FileName)
        {
            return Res.LoadJson<DifficultyTuningData>(path);
        }

        /// <summary>The file, read once per run. Null while it is missing.</summary>
        public static DifficultyTuningData Shared => _shared ??= Load();

        private static DifficultyTuningData _shared;
    }
}
