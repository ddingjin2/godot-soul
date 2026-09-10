using System;
using Godot;
using MyGame.Combat;

namespace MyGame.UI
{
    /// <summary>
    /// The title screen, still assembled in code - there is no authored menu scene yet, only the shell
    /// <c>Scenes/TitleScene.tscn</c> this script sits on; Stage 4 of docs/migrations/scene-data authors
    /// it. What a button *looks* like is already out of here: <c>Resources/UI/MenuTheme.tres</c> carries
    /// the plates, the sizes and the colours, and the lit segment of an option row is the
    /// <c>SegmentActive</c> variation in the same resource.
    ///
    /// Every number here is a screen pixel, exactly as it was in uGUI: <c>World.Ppu</c> scales world
    /// distances and has nothing to do with a menu.
    /// </summary>
    public sealed partial class TitleMenuBootstrap : Node
    {
        // Mood palette tokens - Docs/MoodDirection.md section 2, written as hex/255 so that
        // round(v * 255) reproduces the documented hex exactly.
        private static readonly Color Bone100 = new Color(0.7647059f, 0.7411765f, 0.69411767f);   // #C3BDB1 text primary
        private static readonly Color Bone200 = new Color(0.6039216f, 0.5803922f, 0.53333336f);   // #9A9488 text secondary
        private static readonly Color Bone300 = new Color(0.43137255f, 0.40784314f, 0.36078432f); // #6E685C text dim

        // Segment fills used to be two Colors here and five StyleBoxFlats per button per refresh. They
        // are now the SegmentIdle / SegmentActive type variations in MenuTheme.tres - idle is the same
        // plate every button uses, active one step up from it, because the button's state styles only
        // ever darken the plate and so cannot brighten the chosen value on their own.
        /// <summary>The Theme button type the title stack is drawn as - the shared plate, one point smaller.</summary>
        private const string TitleButtonVariation = "TitleButton";

        private const string SegmentIdleVariation = "SegmentIdle";
        private const string SegmentActiveVariation = "SegmentActive";

        /// <summary>The solid colour the Unity title camera cleared to.</summary>
        private static readonly Color Backdrop = new Color(0.023529412f, 0.02745098f, 0.039215688f); // #06070A

        // The settings panel is 440 wide with 16 of padding a side; the segmented rows size themselves
        // against what is left, so a value button can never be clipped by the panel edge.
        private const float PanelInnerWidth = 408f;
        private const float SegmentCaptionWidth = 180f;
        private const float SegmentSpacing = 4f;

        [Export] private string newGameSceneName = "GameplayScene";

        private Control _settingsPanel;

        /// <summary>The padded column inside <see cref="_settingsPanel"/> - uGUI's VerticalLayoutGroup.</summary>
        private VBoxContainer _settingsBox;

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
            BuildTitleUi();
        }

        /// <summary>
        /// Stands in for the Unity <c>EnsureCamera</c>: that method existed to give the title scene a
        /// camera clearing to #06070A, and Godot's 2D root already draws without one. What is left of it
        /// is the colour, painted as the bottom layer of the menu canvas. The AudioListener has no Godot
        /// counterpart either - audio buses are global.
        /// </summary>
        private static void CreateBackdrop(Control parent)
        {
            var backdrop = new ColorRect
            {
                Name = "Backdrop",
                Color = Backdrop,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            parent.AddChild(backdrop);
            backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        }

        private void BuildTitleUi()
        {
            // Unity's EnsureEventSystem has no counterpart: Godot routes mouse, keyboard and gamepad
            // through the viewport and its focus chain with nothing to install.
            var canvas = new CanvasLayer { Name = "TitleCanvas" };
            AddChild(canvas);

            // uGUI's CanvasScaler (ScaleWithScreenSize, 1920x1080, match 0.5) is Godot's
            // display/window/stretch project setting - one canvas_items stretch for the whole game
            // rather than a component per canvas - so nothing is built for it here.
            var root = new Control { Name = "TitleRoot" };
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.MouseFilter = Control.MouseFilterEnum.Ignore;
            canvas.AddChild(root);

            CreateBackdrop(root);

            // The Korean face, with Godot's built-in face behind it - see GameplayHud.LoadUiFont.
            // Every label on this screen is already Korean, so the fallback is the ugly path, not the
            // safe one; it exists so an unimported .otf leaves a readable menu instead of a blank one.
            Font font = GameplayHud.LoadUiFont();

            CreateTitle(root, font);
            CreateButtonStack(root);
            CreateSettingsPanel(root, font);
        }

        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        private static void CreateTitle(Control parent, Font font)
        {
            Label title = CreateText(parent, "Title", font, "MyGame", 54, Bone100);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.VerticalAlignment = VerticalAlignment.Center;
            GameplayHud.PlaceRect(title, Centre, Centre, new Vector2(0f, 165f), new Vector2(520f, 72f));

            Label subtitle = CreateText(parent, "Subtitle", font, "Wrath altar", 18, Bone300);
            subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            subtitle.VerticalAlignment = VerticalAlignment.Center;
            GameplayHud.PlaceRect(subtitle, Centre, Centre, new Vector2(0f, 116f), new Vector2(520f, 32f));
        }

        private void CreateButtonStack(Control parent)
        {
            var stack = new VBoxContainer { Name = "MenuButtons" };
            parent.AddChild(stack);
            GameplayHud.PlaceRect(stack, Centre, Centre, new Vector2(0f, -40f), new Vector2(260f, 250f));
            stack.AddThemeConstantOverride("separation", 14);
            stack.Alignment = BoxContainer.AlignmentMode.Center;

            Button newGame = CreateButton(stack, Tr("UI_TITLE_NEW_GAME"), StartNewGame);

            // Greyed out with no save to load, so Continue never looks like it did nothing.
            Button continueButton = CreateButton(stack, Tr("UI_TITLE_CONTINUE"), ContinueGame);
            continueButton.Disabled = !GameSave.Exists;

            // Cycles rather than opening a sub-menu: three values, one of which is usually locked, is not
            // worth a second panel. The label carries the current choice so the stack still reads as a
            // list of buttons rather than as a form.
            SeedDifficultyFromSave();
            _difficultyButton = CreateButton(stack, Tr("UI_TITLE_DIFFICULTY"), CycleDifficulty);

            // New Game+ only exists for a save that has finished the road, which is the same thing that
            // unlocks Hard. Greyed out rather than hidden, so the reward is visible before it is earned.
            GameSaveData save = GameSave.Read();
            Button newGamePlusButton = CreateButton(stack, Tr("UI_TITLE_NEW_GAME_PLUS"), StartNewGamePlus);
            newGamePlusButton.Disabled = !(save != null && save.hardUnlocked);

            RefreshDifficultyLabel();

            CreateButton(stack, Tr("UI_TITLE_SETTINGS"), ToggleSettings);
            CreateButton(stack, Tr("UI_TITLE_QUIT"), QuitGame);

            // The Unity menu leaned on the EventSystem's first selectable; Godot hands focus to nothing
            // until something asks, so the top of the stack asks. This is what makes the menu playable on
            // a gamepad without a mouse ever touching it.
            newGame.GrabFocus();
        }

        private void CreateSettingsPanel(Control parent, Font font)
        {
            var panel = new ColorRect
            {
                Name = "SettingsPanel",
                Color = new Color(0.039215688f, 0.043137256f, 0.05490196f, 0.96f), // #0A0B0E
            };
            parent.AddChild(panel);
            GameplayHud.PlaceRect(panel, Centre, Centre, new Vector2(0f, -20f), new Vector2(440f, 620f));
            _settingsPanel = panel;

            // uGUI put the padding on the layout group; Godot puts it on the column's own offsets, which
            // is the same 16px inset with one node fewer.
            _settingsBox = new VBoxContainer { Name = "SettingsColumn" };
            panel.AddChild(_settingsBox);
            _settingsBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _settingsBox.OffsetLeft = 16f;
            _settingsBox.OffsetTop = 16f;
            _settingsBox.OffsetRight = -16f;
            _settingsBox.OffsetBottom = -16f;
            _settingsBox.AddThemeConstantOverride("separation", 8);

            CreateRowText(_settingsBox, font, Tr("UI_OPTION_HEADING"), 24, Bone100);
            _presetLabel = CreateRowText(_settingsBox, font, string.Empty, 18, Bone200);

            CreatePresetButton(Tr("UI_OPTION_PRESET_HIGH_BUTTON"), GraphicsPreset.High);
            CreatePresetButton(Tr("UI_OPTION_PRESET_LOW_BUTTON"), GraphicsPreset.Low);

            // Two values get a box, three or more get a row of value buttons. The widget says how many
            // choices an option has before the player clicks anything, which one cycling button could not.
            _shakeToggle = CreateToggleRow(font, "UI_OPTION_SCREEN_SHAKE", GraphicsOptions.SetScreenShake);
            _hitStopToggle = CreateToggleRow(font, "UI_OPTION_HIT_STOP", GraphicsOptions.SetHitStop);
            _hitFlashToggle = CreateToggleRow(font, "UI_OPTION_HIT_FLASH", GraphicsOptions.SetHitFlash);
            _vsyncToggle = CreateToggleRow(font, "UI_OPTION_VSYNC", GraphicsOptions.SetVSync);

            _msaaRow = CreateSegmentedRow(font, "UI_OPTION_MSAA", StepLabels(GraphicsOptions.MsaaSteps),
                index => GraphicsOptions.SetMsaa(GraphicsOptions.MsaaSteps[index]),
                () => System.Array.IndexOf(GraphicsOptions.MsaaSteps, GraphicsOptions.Msaa));

            _renderScaleRow = CreateSegmentedRow(font, "UI_OPTION_RENDER_SCALE", StepLabels(GraphicsOptions.RenderScaleSteps),
                index => GraphicsOptions.SetRenderScale(GraphicsOptions.RenderScaleSteps[index]),
                () => System.Array.FindIndex(GraphicsOptions.RenderScaleSteps, step => Mathf.IsEqualApprox(step, GraphicsOptions.RenderScale)));

            CreateButton(_settingsBox, Tr("UI_COMMON_CLOSE"), ToggleSettings);

            RefreshOptions();
            _settingsPanel.Visible = false;
        }

        private void CreatePresetButton(string label, GraphicsPreset preset)
        {
            CreateButton(_settingsBox, label, () =>
            {
                GraphicsOptions.ApplyPreset(preset);
                RefreshOptions();
            });
        }

        /// <summary>
        /// A two-value option as a checkbox: the state is readable at a glance rather than hidden in the
        /// label's suffix. Godot's <see cref="CheckBox"/> is that widget already - box, caption, and the
        /// whole row as the hit target - so the Unity original's hand-built box, checkmark and caption
        /// collapse into it.
        /// </summary>
        private CheckBox CreateToggleRow(Font font, string labelKey, Action<bool> set)
        {
            var toggle = new CheckBox
            {
                // The node name is the key, not the caption: a name built from translated text would
                // change with the locale and every lookup by name with it.
                Name = labelKey + "Toggle",
                Text = Tr(labelKey),
                CustomMinimumSize = new Vector2(PanelInnerWidth, 48f),
                Alignment = HorizontalAlignment.Left,
            };
            _settingsBox.AddChild(toggle);

            if (font != null)
                toggle.AddThemeFontOverride("font", font);
            toggle.AddThemeFontSizeOverride("font_size", 22);
            foreach (string slot in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color" })
                toggle.AddThemeColorOverride(slot, Bone100);

            // No transition to animate: uGUI's 0.1s alpha fade left the box mid-tween for the frames
            // after a preset button rewrote every value at once. Godot's check icon swaps instantly.
            toggle.Toggled += value =>
            {
                set(value);
                RefreshOptions();
            };
            return toggle;
        }

        /// <summary>
        /// A three-or-more-value option as a row of value buttons: one click reaches any value, where the
        /// cycling button it replaces needed up to N-1 to come back around. Not an OptionButton - this
        /// menu is assembled in code and the whole point is that all the steps are visible at once, which
        /// a dropdown hides behind a click.
        /// </summary>
        private SegmentedRow CreateSegmentedRow(Font font, string labelKey, string[] values, Action<int> choose, Func<int> currentIndex)
        {
            var row = new HBoxContainer
            {
                // Key rather than caption, for the reason spelled out in CreateToggleRow.
                Name = labelKey + "Row",
                CustomMinimumSize = new Vector2(PanelInnerWidth, 48f),
                Alignment = BoxContainer.AlignmentMode.Begin,
            };
            _settingsBox.AddChild(row);
            row.AddThemeConstantOverride("separation", (int)SegmentSpacing);

            Label caption = CreateText(row, "Caption", font, Tr(labelKey), 20, Bone200);
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.CustomMinimumSize = new Vector2(SegmentCaptionWidth, 48f);
            caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            // The row is exactly the panel's inner width, so the segments split what the caption and the
            // gaps between them leave. Anything wider and the last value is clipped by the panel edge.
            float width = (PanelInnerWidth - SegmentCaptionWidth - values.Length * SegmentSpacing) / values.Length;

            var buttons = new Button[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int index = i;
                buttons[i] = CreateButton(row, values[i], () =>
                {
                    choose(index);
                    RefreshOptions();
                }, width, SegmentIdleVariation);
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
                    // uGUI recoloured one shared graphic; a Godot button carries a stylebox per state, so
                    // lighting a segment used to mean rewriting all five. Both sets are authored in
                    // MenuTheme.tres now and the choice is which of the two type variations is named -
                    // which is also what keeps "exactly one segment is lit" readable from the outside:
                    // Control.GetThemeColor resolves through the variation, so the row still answers
                    // font_color = bone for the chosen value and the dim bone for the rest.
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

        private static Label CreateRowText(Control parent, Font font, string content, int fontSize, Color color)
        {
            Label text = CreateText(parent, "Row", font, content, fontSize, color);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.CustomMinimumSize = new Vector2(0f, fontSize + 12f);
            return text;
        }

        /// <summary>
        /// A menu button. It carries no styling of its own - <c>MenuTheme.tres</c> is the whole of it,
        /// and <paramref name="variation"/> picks which of its button types this one is drawn as.
        /// </summary>
        private static Button CreateButton(Control parent, string label, Action action, float width = 260f, string variation = TitleButtonVariation)
        {
            var button = new Button
            {
                Name = label + "Button",
                Text = label,
                Theme = GameplayHud.LoadMenuTheme(),
                ThemeTypeVariation = variation,
            };
            parent.AddChild(button);
            button.CustomMinimumSize = new Vector2(width, 48f);
            button.Pressed += action;
            return button;
        }

        private static Label CreateText(Control parent, string name, Font font, string content, int fontSize, Color color)
        {
            var label = new Label
            {
                Name = name,
                Text = content,
                LabelSettings = GameplayHud.Face(font, fontSize, color),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            parent.AddChild(label);
            return label;
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
        /// carried in a save slot still works untouched.
        /// </summary>
        private void LoadGameplay(string sceneName = null)
        {
            string target = !string.IsNullOrWhiteSpace(sceneName) ? sceneName : newGameSceneName;

            if (!string.IsNullOrWhiteSpace(target))
                GetTree().ChangeSceneToFile($"res://Scenes/{target}.tscn");
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
