using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// What one save slot holds. Position is deliberately absent: the player always resumes at the
    /// checkpoint, so the slice has nothing to store about where you stood.
    /// </summary>
    public sealed class GameSaveData
    {
        public int version = 1;
        public float health = -1f;
        public float humanity = -1f;

        /// <summary>
        /// Souls carried. Negative means unset, the same way health and humanity do it - zero is a
        /// legitimate purse, so it cannot double as "no record". A slot that lost this field must not be
        /// able to empty a wallet on load.
        /// </summary>
        public int souls = -1;

        /// <summary>
        /// The chapter scene a Continue resumes at. Empty on a slot written before the campaign had
        /// more than one chapter, which <see cref="ChapterRoute.Resume"/> reads as the start of the road.
        /// </summary>
        public string chapterScene = "";

        /// <summary>
        /// The furthest gate ever opened, as an index into <see cref="ChapterRoute.Scenes"/>. Separate
        /// from <see cref="chapterScene"/>, which is the *last* gate recorded and moves backwards when the
        /// player travels back through a portal. Read it through <see cref="ChapterRoute.FurthestIndex"/>,
        /// never directly: a slot written before this field existed defaults to 0 and would lock a
        /// finished campaign back down to chapter one.
        /// </summary>
        public int furthestChapter;

        /// <summary>The difficulty this run is played on, as <see cref="Difficulty"/>. Normal on any older slot.</summary>
        public int difficulty = (int)Difficulty.Normal;

        /// <summary>Set the first time the road is finished on Normal or Hard; Hard cannot be chosen before it.</summary>
        public bool hardUnlocked;

        /// <summary>How many times this save has walked the whole road. Zero on a first run.</summary>
        public int newGamePlus;

        /// <summary>
        /// Which bonfire in <see cref="chapterScene"/> was last rested at, as an index into that
        /// chapter's authored checkpoint list. Negative means no rest yet, so a Continue starts the
        /// chapter at its player spawn.
        /// </summary>
        /// <remarks>
        /// -1 rather than 0 for the reason <see cref="souls"/> is: zero is a legitimate value - it names
        /// the first checkpoint - so it cannot double as "no record". It belongs to the chapter recorded
        /// beside it and is reset when that chapter changes; an index carried across a gate would name a
        /// bonfire that means something else on the far side, or nothing at all.
        /// </remarks>
        public int checkpointIndex = -1;

        /// <summary>
        /// Whether this chapter's shortcut has been opened. Chapter-scoped like
        /// <see cref="checkpointIndex"/>, and reset with it: a door stays open for the chapter it is in,
        /// not for the road.
        /// </summary>
        public bool shortcutOpened;

        /// <summary>
        /// Souls spent on stats, as levels bought rather than as the resulting numbers. Storing the level
        /// keeps the cost curve a design file rather than a thing baked into old slots: retuning it
        /// re-derives every stat instead of stranding a save at numbers nothing can explain.
        /// </summary>
        public int levelVitality;
        public int levelEndurance;
        public int levelStrength;
        public int levelResolve;
    }

    /// <summary>
    /// A single save slot in PlayerPrefs, the same store <see cref="GraphicsOptions"/> already uses, so
    /// there is no file IO or path handling to get wrong across platforms. The payload is JSON, which
    /// means a new field can be added without a migration: a field missing from an older payload is
    /// left at its default.
    ///
    /// Lives in Combat rather than Gameplay because the title menu (UI) has to read it, and UI is not
    /// allowed to reference Gameplay.
    /// </summary>
    public static class GameSave
    {
        private const string Key = "MyGame.Save";

        public static bool Exists => PlayerPrefs.HasKey(Key);

        /// <summary>
        /// Set by the title menu's Continue so GameplayScene knows to restore rather than start fresh.
        /// A static flag rather than a scene parameter because <c>SceneTree.ChangeSceneToFile</c> has
        /// nowhere to put one.
        /// </summary>
        public static bool LoadOnNextGameplayStart { get; set; }

        public static void Write(GameSaveData data)
        {
            if (data == null)
                return;

            PlayerPrefs.SetString(Key, JsonData.ToJson(data));
            PlayerPrefs.Save();
        }

        public static GameSaveData Read()
        {
            if (!Exists)
                return null;

            string json = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            // PlayerPrefs is editable outside the game, so a malformed payload is a possible input, not
            // an impossible one. Continue reads this during scene bootstrap, where a throw takes the whole
            // scene down; JsonData swallows the parse error and hands back null, so a broken slot reads as
            // no slot instead. (Unity's FromJsonOverwrite threw here and was caught by hand.)
            GameSaveData data = JsonData.FromJson<GameSaveData>(json);
            if (data == null)
                GD.PushWarning("[GameSave] Ignoring a save slot that will not parse.");

            return data;
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            LoadOnNextGameplayStart = false;
        }
    }
}
