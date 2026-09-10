using System;
using Godot;
using MyGame.Combat;

namespace MyGame.UI
{
    /// <summary>
    /// The title screen. It builds nothing: <c>Scenes/UI/TitleScreen.tscn</c> owns every node, anchor,
    /// colour and font size on this screen - <c>Scenes/TitleScene.tscn</c>, the scene the game boots
    /// into, inherits it - and what is left here is binding, the difficulty the menu is holding, and the
    /// signals. Stage 4 of docs/migrations/scene-data is what moved it; what a button *looks* like had
    /// already gone in Stage 2, to <c>Resources/UI/MenuTheme.tres</c>.
    ///
    /// The one thing still assembled at runtime is the row of value buttons inside a segmented option:
    /// how many steps anti-aliasing or render scale has follows <see cref="GraphicsOptions"/>, so each
    /// row instances <c>Scenes/UI/SegmentButton.tscn</c> once per step - the case Rule 3 answers with
    /// "instance a pre-authored item scene".
    ///
    /// Nodes are addressed by name, and the names are the localisation key plus a role -
    /// <c>UI_TITLE_QUITButton</c>, <c>UI_OPTION_VSYNCToggle</c>, <c>UI_OPTION_MSAARow</c>. Never the
    /// translated caption: a name built from text moves with the locale and takes every lookup with it.
    ///
    /// Every number on this screen is a screen pixel, exactly as it was in uGUI: <c>World.Ppu</c> scales
    /// world distances and has nothing to do with a menu. None are left in this file.
    /// </summary>
    public sealed partial class TitleMenuBootstrap : Node
    {
        /// <summary>One value of a segmented option row. Instanced once per step of that option.</summary>
        private const string SegmentScenePath = "res://Scenes/UI/SegmentButton.tscn";

        // Lighting a segment used to mean rewriting five styleboxes on it. Both plates are authored in
        // MenuTheme.tres now and the choice is which of the two type variations a button is named as -
        // idle is the shared plate with the dim text, active one step up from it, because a button's
        // state styles only ever darken the plate and so cannot brighten the chosen value on their own.
        private const string SegmentIdleVariation = "SegmentIdle";
        private const string SegmentActiveVariation = "SegmentActive";

        [Export] private string newGameSceneName = "GameplayScene";

        private Control _settingsPanel;

        /// <summary>The padded column inside <see cref="_settingsPanel"/> - uGUI's VerticalLayoutGroup.</summary>
        private Control _settingsBox;

        /// <summary>
        /// The difficulty New Game and New Game+ will start on. Seeded from the slot so the menu opens on
        /// the setting the player last used rather than resetting to Normal every launch.
        /// </summary>
        private Difficulty _difficulty = Difficulty.Normal;

        private Button _difficultyButton;
        private Label _presetLabel;
        private CheckBox _shakeToggle;
        private CheckBox _hitStopToggle;
        private CheckBox _hitFlashToggle;
        private CheckBox _vsyncToggle;
        private SegmentedRow _msaaRow;
        private SegmentedRow _renderScaleRow;

        public override void _Ready()
        {
            Bind();
        }

        /// <summary>
        /// Resolves the authored screen and connects it. The node names are the contract, and
        /// <c>TitleGraphicsOptionsTests</c> reaches in by three of them - the menu buttons, the option
        /// boxes and the two option rows - so a rename here is a rename there and in the scene.
        /// </summary>
        private void Bind()
        {
            // Writes the Korean face into the shared Theme the scene references through its own
            // ext_resource - it is the same cached instance, and the resource deliberately names no font
            // of its own, because an ext_resource to an unimported .otf is a hard load failure. Has to
            // happen before the first draw or every caption falls back to Godot's face.
            GameplayHud.LoadMenuTheme();

            Control root = GetNode<Control>("TitleCanvas/TitleRoot");
            Control stack = root.GetNode<Control>("MenuButtons");

            Button newGame = KeyedButton(stack, "UI_TITLE_NEW_GAME");
            newGame.Pressed += StartNewGame;

            // Greyed out with no save to load, so Continue never looks like it did nothing.
            Button continueButton = KeyedButton(stack, "UI_TITLE_CONTINUE");
            continueButton.Pressed += ContinueGame;
            continueButton.Disabled = !GameSave.Exists;

            SeedDifficultyFromSave();
            _difficultyButton = KeyedButton(stack, "UI_TITLE_DIFFICULTY");
            _difficultyButton.Pressed += CycleDifficulty;
            RefreshDifficultyLabel();

            // New Game+ only exists for a save that has finished the road, which is the same thing that
            // unlocks Hard. Greyed out rather than hidden, so the reward is visible before it is earned.
            GameSaveData save = GameSave.Read();
            Button newGamePlusButton = KeyedButton(stack, "UI_TITLE_NEW_GAME_PLUS");
            newGamePlusButton.Pressed += StartNewGamePlus;
            newGamePlusButton.Disabled = !(save != null && save.hardUnlocked);

            KeyedButton(stack, "UI_TITLE_SETTINGS").Pressed += ToggleSettings;
            KeyedButton(stack, "UI_TITLE_QUIT").Pressed += QuitGame;

            BindSettingsPanel(root);

            // The Unity menu leaned on the EventSystem's first selectable; Godot hands focus to nothing
            // until something asks, so the top of the stack asks. This is what makes the menu playable on
            // a gamepad without a mouse ever touching it.
            newGame.GrabFocus();
        }

        private void BindSettingsPanel(Control root)
        {
            // Authored hidden; the settings button toggles it.
            _settingsPanel = root.GetNode<Control>("SettingsPanel");
            _settingsBox = _settingsPanel.GetNode<Control>("SettingsColumn");

            _presetLabel = _settingsBox.GetNode<Label>("PresetLine");

            KeyedButton(_settingsBox, "UI_OPTION_PRESET_HIGH").Pressed += () => ApplyPreset(GraphicsPreset.High);
            KeyedButton(_settingsBox, "UI_OPTION_PRESET_LOW").Pressed += () => ApplyPreset(GraphicsPreset.Low);

            _shakeToggle = BindToggle("UI_OPTION_SCREEN_SHAKE", GraphicsOptions.SetScreenShake);
            _hitStopToggle = BindToggle("UI_OPTION_HIT_STOP", GraphicsOptions.SetHitStop);
            _hitFlashToggle = BindToggle("UI_OPTION_HIT_FLASH", GraphicsOptions.SetHitFlash);
            _vsyncToggle = BindToggle("UI_OPTION_VSYNC", GraphicsOptions.SetVSync);

            _msaaRow = BindRow("UI_OPTION_MSAA", StepLabels(GraphicsOptions.MsaaSteps),
                index => GraphicsOptions.SetMsaa(GraphicsOptions.MsaaSteps[index]),
                () => System.Array.IndexOf(GraphicsOptions.MsaaSteps, GraphicsOptions.Msaa));

            _renderScaleRow = BindRow("UI_OPTION_RENDER_SCALE", StepLabels(GraphicsOptions.RenderScaleSteps),
                index => GraphicsOptions.SetRenderScale(GraphicsOptions.RenderScaleSteps[index]),
                () => System.Array.FindIndex(GraphicsOptions.RenderScaleSteps, step => Mathf.IsEqualApprox(step, GraphicsOptions.RenderScale)));

            KeyedButton(_settingsBox, "UI_COMMON_CLOSE").Pressed += ToggleSettings;

            RefreshOptions();
        }

        /// <summary>An authored button, addressed by the localisation key its caption is written from.</summary>
        private static Button KeyedButton(Control parent, string key) => parent.GetNode<Button>(key + "Button");

        private void ApplyPreset(GraphicsPreset preset)
        {
            GraphicsOptions.ApplyPreset(preset);
            RefreshOptions();
        }

        /// <summary>
        /// A two-value option is a checkbox: the state is readable at a glance rather than hidden in the
        /// caption's suffix, and Godot's <see cref="CheckBox"/> is that widget already - box, caption and
        /// the whole row as the hit target.
        /// </summary>
        /// <remarks>
        /// No transition to animate: uGUI's 0.1s alpha fade left the box mid-tween for the frames after a
        /// preset button rewrote every value at once. Godot's check icon swaps instantly.
        /// </remarks>
        private CheckBox BindToggle(string key, Action<bool> set)
        {
            CheckBox toggle = _settingsBox.GetNode<CheckBox>(key + "Toggle");
            toggle.Toggled += value =>
            {
                set(value);
                RefreshOptions();
            };
            return toggle;
        }

        /// <summary>
        /// A three-or-more-value option is a row of value buttons: one click reaches any value, where the
        /// cycling button it replaces needed up to N-1 to come back around. Not an OptionButton - the
        /// whole point is that all the steps are visible at once, which a dropdown hides behind a click.
        /// </summary>
        /// <remarks>
        /// The row, its caption and its rect are authored in <c>SegmentedRow.tscn</c>; only the buttons
        /// are built here, because how many there are follows <see cref="GraphicsOptions"/>'s step arrays.
        /// The width arithmetic that used to size them is gone - <c>Segments</c> expands into what the
        /// caption leaves and its own separation splits that between however many there turn out to be.
        /// </remarks>
        private SegmentedRow BindRow(string key, string[] values, Action<int> choose, Func<int> currentIndex)
        {
            Control segments = _settingsBox.GetNode<Control>(key + "Row/Segments");
            var segmentScene = GD.Load<PackedScene>(SegmentScenePath);

            var buttons = new Button[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                // Fresh local per iteration: the loop variable would be read at click time and every
                // segment would write the last value.
                int index = i;

                Button segment = segmentScene.Instantiate<Button>();
                segment.Name = "Segment" + index;

                // The caption is a value, not a phrase - "4x", "100%", the translated "off" - so it is
                // written here rather than authored as a localisation key like every other caption.
                segment.Text = values[index];
                segment.Pressed += () =>
                {
                    choose(index);
                    RefreshOptions();
                };

                segments.AddChild(segment);
                buttons[index] = segment;
            }

            return new SegmentedRow(buttons, currentIndex);
        }

        private void RefreshOptions()
        {
            _presetLabel.Text = string.Format(Tr("UI_OPTION_PRESET_VALUE"), PresetName(GraphicsOptions.Preset));

            // SetPressedNoSignal, not ButtonPressed: a preset button writes all six values and then
            // refreshes, and the plain setter would fire Toggled back into this method once for every box
            // it moved. (uGUI called this SetIsOnWithoutNotify.)
            _shakeToggle.SetPressedNoSignal(GraphicsOptions.ScreenShake);
            _hitStopToggle.SetPressedNoSignal(GraphicsOptions.HitStop);
            _hitFlashToggle.SetPressedNoSignal(GraphicsOptions.HitFlash);
            _vsyncToggle.SetPressedNoSignal(GraphicsOptions.VSync);

            _msaaRow.Refresh();
            _renderScaleRow.Refresh();
        }

        private static string[] StepLabels(int[] steps)
        {
            var labels = new string[steps.Length];
            for (int i = 0; i < steps.Length; i++)
                labels[i] = steps[i] <= 1 ? TranslationServer.Translate("UI_OPTION_STEP_OFF") : steps[i] + "x";
            return labels;
        }

        private static string[] StepLabels(float[] steps)
        {
            var labels = new string[steps.Length];
            for (int i = 0; i < steps.Length; i++)
                labels[i] = Mathf.RoundToInt(steps[i] * 100f) + "%";
            return labels;
        }

        /// <summary>One option drawn as a row of value buttons, the chosen value lit and the rest dimmed.</summary>
        private sealed class SegmentedRow
        {
            private readonly Button[] _buttons;
            private readonly Func<int> _currentIndex;

            public SegmentedRow(Button[] buttons, Func<int> currentIndex)
            {
                _buttons = buttons;
                _currentIndex = currentIndex;
            }

            /// <summary>
            /// A stored value that is not one of the steps - PlayerPrefs is editable from outside the game -
            /// reads as index -1 and simply lights nothing, rather than throwing on the way into the menu.
            /// </summary>
            public void Refresh()
            {
                int current = _currentIndex();
                for (int i = 0; i < _buttons.Length; i++)
                {
                    // Naming the variation is the whole of "this one is lit", and it is also what keeps
                    // "exactly one segment is lit" readable from the outside: Control.GetThemeColor
                    // resolves through the variation, so the row answers font_color = bright bone for the
                    // chosen value and the dim bone for the rest.
                    _buttons[i].ThemeTypeVariation = i == current ? SegmentActiveVariation : SegmentIdleVariation;
                }
            }
        }

        private static string PresetName(GraphicsPreset preset)
        {
            switch (preset)
            {
                case GraphicsPreset.High:
                    return TranslationServer.Translate("UI_OPTION_PRESET_HIGH");
                case GraphicsPreset.Low:
                    return TranslationServer.Translate("UI_OPTION_PRESET_LOW");
                default:
                    return TranslationServer.Translate("UI_OPTION_PRESET_CUSTOM");
            }
        }

        private void StartNewGame()
        {
            // The save itself is left alone - a new run only overwrites it if the player saves again.
            GameSave.LoadOnNextGameplayStart = false;

            // The chosen difficulty has to survive the scene load, and the slot is not the way: a New
            // Game does not read it, and it still holds whatever the last run was played on. The static
            // is what GameplayBootstrap leaves alone when it is not resuming.
            DifficultySettings.Set(_difficulty, 0);

            LoadGameplay();
        }

        /// <summary>
        /// A second lap. Keeps the souls the last run finished with and the difficulty picked for this
        /// one, puts the player back at the first gate, and counts the lap - which is what makes the
        /// enemies tougher ([[DifficultySettings]]).
        /// </summary>
        /// <remarks>
        /// Writes the slot here rather than letting the arena do it, because the run has to start from a
        /// record: the souls are the whole point of carrying a save forward, and only a slot marked for
        /// loading brings them into the new chapter.
        /// </remarks>
        private void StartNewGamePlus()
        {
            GameSaveData save = GameSave.Read();
            if (save == null || !save.hardUnlocked)
                return;

            int lap = save.newGamePlus + 1;

            GameSave.Write(new GameSaveData
            {
                // Unset, so the arena starts the player at full health and humanity rather than at
                // whatever the last boss left them on.
                health = -1f,
                humanity = -1f,
                souls = save.souls,

                chapterScene = ChapterRoute.FirstChapterScene,
                furthestChapter = 0,
                difficulty = (int)_difficulty,
                hardUnlocked = true,
                newGamePlus = lap
            });

            DifficultySettings.Set(_difficulty, lap);
            GameSave.LoadOnNextGameplayStart = true;
            LoadGameplay(ChapterRoute.FirstChapterScene);
        }

        /// <summary>
        /// Easy - Normal - Hard, skipping Hard until a save has finished the road. Wraps rather than
        /// stopping at the ends: a cycling button that refuses to move looks broken.
        /// </summary>
        private void CycleDifficulty()
        {
            GameSaveData save = GameSave.Read();

            _difficulty = _difficulty switch
            {
                Difficulty.Easy => Difficulty.Normal,
                Difficulty.Normal => DifficultySettings.IsUnlocked(Difficulty.Hard, save) ? Difficulty.Hard : Difficulty.Easy,
                _ => Difficulty.Easy
            };

            RefreshDifficultyLabel();
        }

        /// <summary>
        /// Reads the slot's difficulty, refusing a Hard it has not earned - PlayerPrefs is editable from
        /// outside the game, so the menu clamps the same way <see cref="DifficultySettings.Apply"/> does.
        /// </summary>
        private void SeedDifficultyFromSave()
        {
            GameSaveData save = GameSave.Read();
            if (save == null)
                return;

            var stored = (Difficulty)Mathf.Clamp(save.difficulty, (int)Difficulty.Easy, (int)Difficulty.Hard);
            _difficulty = DifficultySettings.IsUnlocked(stored, save) ? stored : Difficulty.Normal;
        }

        private void RefreshDifficultyLabel()
        {
            if (_difficultyButton != null)
                _difficultyButton.Text = string.Format(Tr("UI_TITLE_DIFFICULTY_VALUE"), DifficultySettings.DisplayName(_difficulty));
        }

        /// <summary>
        /// Resumes at the furthest gate the save reached, not at the start of the road. A save from
        /// before the campaign had chapters, or one naming a scene this build does not have, opens the
        /// first chapter instead - the slot is PlayerPrefs and is editable from outside the game.
        /// </summary>
        private void ContinueGame()
        {
            if (!GameSave.Exists)
                return;

            GameSave.LoadOnNextGameplayStart = true;
            LoadGameplay(ChapterRoute.Resume(GameSave.Read()));
        }

        /// <summary>
        /// Unity addressed a scene by its Build Settings name; Godot addresses it by path, and the
        /// campaign's scene names map one-to-one onto <c>res://Scenes/&lt;Name&gt;.tscn</c>, so the name
        /// carried in a save slot still works untouched. <see cref="ChapterRoute.ScenePath"/> is that
        /// mapping, in the one place that owns it.
        /// </summary>
        private void LoadGameplay(string sceneName = null)
        {
            string target = !string.IsNullOrWhiteSpace(sceneName) ? sceneName : newGameSceneName;

            if (!string.IsNullOrWhiteSpace(target))
                GetTree().ChangeSceneToFile(ChapterRoute.ScenePath(target));
        }

        private void ToggleSettings()
        {
            if (_settingsPanel != null)
                _settingsPanel.Visible = !_settingsPanel.Visible;
        }

        private void QuitGame()
        {
            // One call for both cases the Unity original had to split: GetTree().Quit() stops a running
            // game and stops a play-in-editor run alike.
            GetTree().Quit();
        }
    }
}
