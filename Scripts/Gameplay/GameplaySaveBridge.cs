using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Translates between the live player and the flat <see cref="GameSaveData"/> record.
    /// Both directions live together so a field added to the save has one place to be read and written,
    /// instead of a capture in the pause menu drifting away from an apply in the bootstrap.
    /// </summary>
    public static class GameplaySaveBridge
    {
        /// <summary>
        /// Everything the slot holds, including which chapter the player is standing in. The chapter is
        /// read off the open scene rather than passed in: a save is always taken from inside the arena
        /// it belongs to, and threading the name through every caller would be one more thing to get
        /// wrong on the one screen where being wrong loses a run.
        /// </summary>
        public static GameSaveData Capture(GameplayPlayerContext player)
        {
            // Progress the player is not carrying in their hands: which gates have ever been opened, and
            // whether the road has been finished. It lives nowhere but the slot, so a capture that built a
            // record from the live player alone would erase it every time anyone rested at a bonfire.
            GameSaveData previous = GameSave.Read();

            string sceneName = GameplayBuildShim.ActiveSceneName;

            // Checkpoint progress and the shortcut door belong to the chapter they were made in, so they
            // are only carried forward when the slot is still describing this one. Without the test a
            // capture taken in chapter five would hand its bonfire index to whatever chapter the record
            // ends up naming, and index 2 there is a different place - or no place.
            bool sameChapter = previous != null && previous.chapterScene == sceneName;

            // Stat levels are read off the live player, not carried from the slot. A purchase mutates the
            // component and nothing else, so a carry-forward would quietly eat every level bought since
            // the last write - the souls gone and the level with them.
            //
            // Through PlayerProgression.EnsureOn rather than a component lookup: progression is a plain
            // C# object hanging off PlayerController2D in this port, not a node, so there is nothing for
            // GetComponent to find. EnsureOn hands back null for a synthetic player, which is the case
            // the fallbacks below exist for.
            PlayerProgression progression = PlayerProgression.EnsureOn(player.GameObject);

            // The slot is the fallback, never zero. A synthetic player - a test fixture, an extractor run -
            // carries no progression, and writing four zeros from one would spend the levels for free.
            int vitality = previous != null ? previous.levelVitality : 0;
            int endurance = previous != null ? previous.levelEndurance : 0;
            int strength = previous != null ? previous.levelStrength : 0;
            int resolve = previous != null ? previous.levelResolve : 0;

            if (progression != null)
            {
                vitality = progression.VitalityLevel;
                endurance = progression.EnduranceLevel;
                strength = progression.StrengthLevel;
                resolve = progression.ResolveLevel;
            }

            return new GameSaveData
            {
                health = player.Health != null ? player.Health.CurrentHealth : -1f,
                humanity = player.Humanity != null ? player.Humanity.CurrentHumanity : -1f,
                souls = player.Wallet != null ? player.Wallet.Souls : 0,
                chapterScene = sceneName,

                // Read through ChapterRoute rather than copied raw, exactly as the field's own doc
                // comment demands: a slot written before furthestChapter existed carries 0 there and
                // proves its progress with the scene it recorded. Copying the 0 and then pointing
                // chapterScene at an earlier gate - which is precisely what GateTravelZone.Travel does
                // with this record - erased both pieces of evidence at once. FurthestIndex handles a
                // null previous, so it is also the null check.
                furthestChapter = ChapterRoute.FurthestIndex(previous),
                hardUnlocked = previous != null && previous.hardUnlocked,

                // Taken from the live settings rather than the slot: these two describe the run being
                // played right now, and the title menu set them before this scene ever loaded.
                difficulty = (int)DifficultySettings.Current,
                newGamePlus = DifficultySettings.NewGamePlus,

                // Carried, not read from the player: nothing on the player knows which bonfire it last
                // sat at. CheckpointZone.Activate overwrites this with its own index after capturing,
                // which is the one place a rest is recorded.
                checkpointIndex = sameChapter ? previous.checkpointIndex : -1,
                shortcutOpened = sameChapter && previous.shortcutOpened,

                // The four stat levels. They are the player's, not the chapter's, so no scene test guards
                // them - a rest, a gate or a save-and-quit all carry them across.
                levelVitality = vitality,
                levelEndurance = endurance,
                levelStrength = strength,
                levelResolve = resolve
            };
        }

        /// <summary>
        /// Which checkpoint a resumed slot puts the player at in <paramref name="sceneName"/>, or -1 for
        /// "start the chapter where it starts". Answered here rather than at the call site because the
        /// chapter test is part of what <see cref="GameSaveData.checkpointIndex"/> means, and this file is
        /// where the save's shape is allowed to be known.
        /// </summary>
        public static int ResumeCheckpointIndex(string sceneName)
        {
            GameSaveData data = ResumingSlotFor(sceneName);
            return data != null ? data.checkpointIndex : -1;
        }

        /// <summary>Whether the chapter's shortcut was already open when this slot was written.</summary>
        public static bool ResumeShortcutOpened(string sceneName)
        {
            GameSaveData data = ResumingSlotFor(sceneName);
            return data != null && data.shortcutOpened;
        }

        /// <summary>
        /// The slot, but only when the game is actually resuming into the chapter it describes. A New
        /// Game leaves the last run's record in the prefs untouched until the first save, so reading it
        /// without the flag would drop a fresh run at the previous one's bonfire.
        /// </summary>
        private static GameSaveData ResumingSlotFor(string sceneName)
        {
            if (!GameSave.LoadOnNextGameplayStart)
                return null;

            GameSaveData data = GameSave.Read();
            return data != null && data.chapterScene == sceneName ? data : null;
        }

        /// <summary>
        /// True while <see cref="Apply"/> is part-way through writing a slot onto the player. Anything
        /// that reacts to a player value changing must refuse to capture while this is set: mid-Apply the
        /// player is a mixture - the levels already restored, the health, humanity and souls still the
        /// spawner's fresh-run numbers - and capturing that mixture writes an empty purse over the very
        /// slot being restored, three lines before it would have been handed back.
        /// </summary>
        public static bool IsApplying { get; private set; }

        /// <summary>
        /// Writes the slot back onto the live player. Deliberately not everything the record holds:
        /// <see cref="GameSaveData.checkpointIndex"/> and <see cref="GameSaveData.shortcutOpened"/> are
        /// the arena's, and are read through <see cref="ResumeCheckpointIndex"/> before the player exists.
        /// </summary>
        public static void Apply(GameSaveData data, GameplayPlayerContext player)
        {
            if (data == null)
                return;

            IsApplying = true;
            try
            {
                // Levels first, and the order is load-bearing. SetLevels raises the maximums the lines
                // below are measured against: Health.SetMaxHealth only ever clamps current health
                // downwards and SetHealth clamps to the maximum, so restoring 180 health into a
                // not-yet-raised 100 maximum would throw away exactly what the player paid souls for.
                if (player.GameObject != null)
                {
                    PlayerProgression.EnsureOn(player.GameObject)?.SetLevels(
                        data.levelVitality, data.levelEndurance, data.levelStrength, data.levelResolve);
                }

                // A save taken while dying would otherwise load straight back into the death it recorded.
                if (player.Health != null && data.health > 0f)
                    player.Health.SetHealth(data.health);

                if (player.Humanity != null && data.humanity >= 0f)
                    player.Humanity.SetHumanity(data.humanity);

                // Guarded like the other two. Souls had no sentinel, so any record that reached here
                // without a real capture - an older slot, a partial write - emptied the purse instead of
                // leaving it.
                if (data.souls >= 0)
                    player.Wallet?.SetSouls(data.souls);
            }
            finally
            {
                // finally, not a trailing assignment: a throw part-way through a restore would otherwise
                // leave the flag set for the rest of the session and silently stop every later save.
                IsApplying = false;
            }
        }
    }
}
