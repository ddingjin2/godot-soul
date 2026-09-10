using Godot;

namespace MyGame.Combat
{
    /// <summary>
    /// The three ways to walk the road. Easy and Normal are open from the start; Hard is what finishing
    /// the road earns, together with New Game+ ([[WorldSetting]] - rebirth is the end of the seventh gate,
    /// and starting over is the fiction's own idea).
    /// </summary>
    public enum Difficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    /// <summary>
    /// What a difficulty and a New Game+ cycle actually change, and the only place that answer lives.
    ///
    /// Two knobs, deliberately: what the player takes, and what the enemies have. Everything else - every
    /// telegraph, every recovery, every attack the bosses were authored with - is identical on all three,
    /// because a difficulty that retimes the fight is a different fight to learn rather than the same one
    /// at a different weight.
    /// </summary>
    /// <remarks>
    /// Static because the whole game runs one difficulty at a time and every reader (the resolver, the
    /// spawner) is somewhere an exported node reference cannot reach. <see cref="Apply"/> is called from
    /// the gameplay bootstrap, so a scene run straight from the editor with no save runs Normal.
    ///
    /// ponytail: numbers live here rather than in a designer JSON. They are three pairs that only mean
    /// anything against each other; move them to Resources/Design when a designer asks to tune them
    /// without a build.
    /// </remarks>
    public static class DifficultySettings
    {
        public static Difficulty Current { get; private set; } = Difficulty.Normal;

        /// <summary>Completed cycles of the road. 0 is a first run; every cycle makes the enemies harder.</summary>
        public static int NewGamePlus { get; private set; }

        /// <summary>How much of an incoming hit the player actually takes.</summary>
        public static float PlayerDamageTakenMultiplier => Current switch
        {
            Difficulty.Easy => 0.7f,
            Difficulty.Hard => 1.4f,
            _ => 1f
        };

        /// <summary>
        /// Enemy and boss health. New Game+ stacks on top of the difficulty rather than replacing it, so
        /// a second lap on Hard is harder than the first.
        /// </summary>
        public static float EnemyHealthMultiplier
        {
            get
            {
                float difficulty = Current switch
                {
                    Difficulty.Easy => 0.85f,
                    Difficulty.Hard => 1.25f,
                    _ => 1f
                };

                return difficulty * (1f + 0.25f * Mathf.Max(0, NewGamePlus));
            }
        }

        /// <summary>Hard is a reward, not a choice: it opens when a save has finished the road once.</summary>
        public static bool IsUnlocked(Difficulty difficulty, GameSaveData save)
        {
            return difficulty != Difficulty.Hard || (save != null && save.hardUnlocked);
        }

        /// <summary>
        /// Reads a slot into the live settings. A null slot is a fresh run, not an error - New Game writes
        /// the slot after this, and a scene opened straight from the editor has no slot at all.
        /// </summary>
        public static void Apply(GameSaveData save)
        {
            if (save == null)
            {
                Set(Difficulty.Normal, 0);
                return;
            }

            // Clamped rather than cast on trust: PlayerPrefs is editable from outside the game, and an
            // out-of-range integer would otherwise select a difficulty with no multipliers at all.
            var difficulty = (Difficulty)Mathf.Clamp(save.difficulty, (int)Difficulty.Easy, (int)Difficulty.Hard);

            // A slot that names Hard without having earned it is refused the same way, and drops to Normal.
            if (!IsUnlocked(difficulty, save))
                difficulty = Difficulty.Normal;

            Set(difficulty, save.newGamePlus);
        }

        public static void Set(Difficulty difficulty, int newGamePlus)
        {
            Current = difficulty;
            NewGamePlus = Mathf.Max(0, newGamePlus);
        }

        public static string DisplayName(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => "쉬움",
            Difficulty.Hard => "어려움",
            _ => "보통"
        };
    }
}
