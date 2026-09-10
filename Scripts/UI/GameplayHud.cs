using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.UI
{
    /// <summary>
    /// The gameplay HUD, built entirely in code. The Unity original was a <c>MonoBehaviour</c> on the
    /// screen-space <c>Canvas</c>; here it is the <see cref="CanvasLayer"/> itself, and every uGUI
    /// <c>Image</c> is a <see cref="ColorRect"/>, every <c>Text</c> a <see cref="Label"/>.
    ///
    /// All numbers in this file are screen pixels, exactly as they were in uGUI - <c>World.Ppu</c> is a
    /// world-space conversion and has no business here.
    /// </summary>
    public partial class GameplayHud : CanvasLayer
    {
        // Mood palette tokens - Docs/MoodDirection.md section 2. Written as hex/255 so that
        // round(v * 255) reproduces the documented hex exactly; a two-decimal literal does not
        // always land on the right byte (0.68 * 255 rounds to 0xAD, not 0xAE).
        private static readonly Color Bone100 = new Color(0.7647059f, 0.7411765f, 0.69411767f);   // #C3BDB1 text primary
        private static readonly Color Bone200 = new Color(0.6039216f, 0.5803922f, 0.53333336f);   // #9A9488 text secondary
        private static readonly Color Bone300 = new Color(0.43137255f, 0.40784314f, 0.36078432f); // #6E685C text dim
        private static readonly Color Cold200 = new Color(0.5176471f, 0.57254905f, 0.627451f);    // #8492A0 souls / spirit
        private static readonly Color Ember300 = new Color(0.65882355f, 0.29411766f, 0.2f);       // #A84B33 warning
        private static readonly Color PanelInk = new Color(0.039215688f, 0.043137256f, 0.05490196f); // #0A0B0E panel fill

        /// <summary>The full-rect <see cref="Control"/> every widget hangs off - a CanvasLayer has no rect of its own.</summary>
        private Control _root;

        private Label _healthText;
        private Label _humanityText;
        private Label _resonanceText;
        private Label _activeSinText;
        private Label _deathCountText;
        private Label _spiritStateText;
        private Label _warningText;
        private Label _staminaText;
        private Label _poiseText;
        private Label _soulsText;
        private Label _flaskText;
        private Label _actionText;

        // A uGUI fill was an Image plus its RectTransform; a Godot fill is one ColorRect that is both,
        // so the Unity pairs (_healthFill / _healthFillImage, _poiseFill / _poiseFillImage) collapse.
        private ColorRect _healthFill;
        private ColorRect _staminaFill;
        private ColorRect _poiseFill;
        private ColorRect _ghostFill;
        private Control _poiseGauge;

        // Ghost gauge and hit flash, on scaled time on purpose: at timeScale 0 the pause menu freezes
        // both, which is what stops a hit taken on the last frame before Escape from draining behind
        // the menu and being over by the time the player looks again.
        private const float GhostHoldSeconds = 0.4f;
        private const float GhostDrainSeconds = 0.5f;
        private const float HitFlashSeconds = 0.15f;
        private float _ghostRatio;
        private float _ghostHoldUntil;
        private float _hitFlashUntil;
        private Control _victoryPanel;
        private const string DefaultRestartLabelKey = "UI_VICTORY_RESTART";

        private Label _victorySubtitle;
        private Button _restartButton;
        private Button _titleButton;
        private Control _pausePanel;
        private Button _resumeButton;
        private Button _saveButton;
        private Button _pauseTitleButton;
        private Label _pauseStatusText;
        private Control _gateTravelPanel;
        private Control _gateTravelList;
        private Button _gateTravelCloseButton;
        private Font _gateTravelFont;
        private Control _levelUpPanel;
        private Button[] _levelUpButtons;
        private Label _levelUpSoulsText;
        private Button _levelUpCloseButton;
        private Action _levelUpCloseAction;
        private PlayerProgression _progression;

        // Godot buttons carry their own label, so the Unity _levelUpLabels array is gone: the row's text
        // and its font size are set on the Button itself.

        /// <summary>The four rows, in the order <see cref="PlayerStat"/> declares them.</summary>
        private static readonly PlayerStat[] LevelUpStats =
        {
            PlayerStat.Vitality, PlayerStat.Endurance, PlayerStat.Strength, PlayerStat.Resolve
        };

        public bool IsVictoryVisible => _victoryPanel != null && _victoryPanel.Visible;
        public bool IsPauseVisible => _pausePanel != null && _pausePanel.Visible;
        public bool IsGateTravelVisible => _gateTravelPanel != null && _gateTravelPanel.Visible;
        public bool IsLevelUpVisible => _levelUpPanel != null && _levelUpPanel.Visible;

        private Health _health;
        private HumanityController _humanity;
        private SinResonanceController _sinResonance;
        private DeathStateController _deathController;
        private StaminaSystem _stamina;
        private PlayerController2D _player;
        private Poise _poise;
        private SoulsWallet _wallet;

        /// <summary>
        /// The Korean face every screen draws with, falling back to Godot's built-in face.
        /// <see cref="ResourceLoader"/> answers null until Godot has imported the .otf - a fresh clone,
        /// and any headless run before the reimport - and a Label with a null font draws nothing at all,
        /// so the fallback is what keeps the HUD on screen rather than a nicety.
        /// Shared with <see cref="TitleMenuBootstrap"/> and the cutscene overlay.
        /// </summary>
        public static Font LoadUiFont()
        {
            var korean = Res.Load<Font>("UI/NotoSerifKR-Regular", ".otf");
            return korean != null ? korean : ThemeDB.FallbackFont;
        }

        public override void _Ready()
        {
            // The warning line expires on unscaled time and the menus have to keep answering while the
            // world is stopped, so the HUD runs through a pause. The ghost gauge stays on scaled time and
            // freezes anyway - see TickHealthGauge.
            ProcessMode = ProcessModeEnum.Always;
        }

        /// <summary>
        /// Builds the whole HUD. <paramref name="canvas"/> is the overlay layer to build into; it
        /// defaults to this node, which is the CanvasLayer the Unity <c>Canvas</c> became.
        /// </summary>
        public void CreateUi(CanvasLayer canvas = null)
        {
            Font uiFont = LoadUiFont();

            _root = new Control { Name = "HudRoot" };
            _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            // Ignore, not Stop: the plate behind the readouts must not eat clicks meant for the world or
            // for a panel. Children are hit-tested on their own, so the panels still block.
            _root.MouseFilter = Control.MouseFilterEnum.Ignore;
            (canvas ?? this).AddChild(_root);

            CreateBackground(_root);

            // Built before the text rows on purpose: a Control draws its children in tree order, so
            // anything made here ends up behind every row below and the numbers stay legible on top of
            // their gauge.
            _healthFill = CreateBar(_root, "HealthGauge", Ember300, new Vector2(20, -40));
            _staminaFill = CreateBar(_root, "StaminaGauge", Cold200, new Vector2(20, -220));
            _poiseFill = CreateBar(_root, "PoiseGauge", Bone300, new Vector2(20, -250));
            _poiseGauge = _poiseFill.GetParent<Control>();

            // Health alone gets the ghost. Stamina and poise refill in under a second, so a trailing
            // strip on those two would be lit most of the fight and read as noise rather than as damage.
            // Built after the live fill and then moved to child 0, so it draws behind it.
            _ghostFill = CreateFill(_healthFill.GetParent<Control>(), "GhostFill", Bone100, 0.3f);
            _ghostFill.GetParent().MoveChild(_ghostFill, 0);

            _healthText = CreateText(_root, "HealthText", uiFont, Bone100, 22, new Vector2(20, -40), HorizontalAlignment.Left);
            _humanityText = CreateText(_root, "HumanityText", uiFont, Bone200, 22, new Vector2(20, -70), HorizontalAlignment.Left);
            _resonanceText = CreateText(_root, "ResonanceText", uiFont, Bone200, 22, new Vector2(20, -100), HorizontalAlignment.Left);
            _activeSinText = CreateText(_root, "ActiveSinText", uiFont, Bone100, 22, new Vector2(20, -130), HorizontalAlignment.Left);
            _deathCountText = CreateText(_root, "DeathCountText", uiFont, Bone300, 20, new Vector2(20, -160), HorizontalAlignment.Left);
            _spiritStateText = CreateText(_root, "SpiritStateText", uiFont, Cold200, 20, new Vector2(20, -190), HorizontalAlignment.Left);
            _staminaText = CreateText(_root, "StaminaText", uiFont, Bone200, 20, new Vector2(20, -220), HorizontalAlignment.Left);
            _poiseText = CreateText(_root, "PoiseText", uiFont, Bone200, 20, new Vector2(20, -250), HorizontalAlignment.Left);
            _soulsText = CreateText(_root, "SoulsText", uiFont, Cold200, 22, new Vector2(20, -280), HorizontalAlignment.Left);
            _flaskText = CreateText(_root, "FlaskText", uiFont, Bone200, 22, new Vector2(20, -310), HorizontalAlignment.Left);
            _actionText = CreateText(_root, "ActionText", uiFont, Bone300, 18, new Vector2(20, -340), HorizontalAlignment.Left);
            // x = 0, not half the viewport width: the centred branch of CreateText anchors this rect to
            // the middle of the screen already, so a half-screen offset on top pushed the warning to the
            // right edge.
            _warningText = CreateText(_root, "WarningText", uiFont, Ember300, 28, new Vector2(0, -40), HorizontalAlignment.Center);
            CreateVictoryPanel(_root, uiFont);

            // Before the pause panel on purpose. Children draw in tree order, so whatever is built last
            // covers everything before it - and the level-up panel is the one panel here that does not
            // stop the world, so Escape has to be able to put the pause menu on top of it.
            CreateLevelUpPanel(_root, uiFont);

            CreatePausePanel(_root, uiFont);
            CreateGateTravelPanel(_root, uiFont);
        }

        /// <summary>
        /// uGUI placed a rect with anchorMin/anchorMax + pivot + anchoredPosition + sizeDelta; Godot
        /// places one with four anchors and four offsets. This is that conversion, in one place. Unity's
        /// +Y-up screen axis flips to Godot's +Y-down here, which is why every <c>anchor.Y</c> and every
        /// <c>position.Y</c> is negated.
        /// </summary>
        internal static void PlaceRect(Control control, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            control.AnchorLeft = control.AnchorRight = anchor.X;
            control.AnchorTop = control.AnchorBottom = 1f - anchor.Y;

            float left = position.X - pivot.X * size.X;
            float top = -position.Y - (1f - pivot.Y) * size.Y;
            control.OffsetLeft = left;
            control.OffsetTop = top;
            control.OffsetRight = left + size.X;
            control.OffsetBottom = top + size.Y;
        }

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        private static void CreateBackground(Control parent)
        {
            var bg = new ColorRect
            {
                Name = "HUDBackground",
                Color = new Color(0f, 0f, 0f, 0.45f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            parent.AddChild(bg);
            // Sits behind the text column (rows at x 20, y -40 down to -340) with a 10px margin. The old
            // (150, -110) put the plate's left edge to the right of every row it was meant to back.
            PlaceRect(bg, TopLeft, TopLeft, new Vector2(10, -30), new Vector2(280, 350));
        }

        private void CreateVictoryPanel(Control parent, Font font)
        {
            _victoryPanel = CreatePanel(parent, "VictoryPanel", 0.94f);

            Label title = CreateVictoryText(_victoryPanel, "VictoryTitle", font, "VICTORY", 58, new Vector2(0f, 105f));
            title.LabelSettings.FontColor = new Color(0.72156864f, 0.64705884f, 0.47843137f); // #B8A57A
            _victorySubtitle = CreateVictoryText(_victoryPanel, "VictorySubtitle", font, "Wrath has fallen", 24, new Vector2(0f, 46f));

            _restartButton = CreateVictoryButton(_victoryPanel, font, "RestartButton", Tr(DefaultRestartLabelKey), new Vector2(0f, -35f));
            _titleButton = CreateVictoryButton(_victoryPanel, font, "TitleButton", Tr("UI_COMMON_RETURN_TO_TITLE"), new Vector2(0f, -105f));
            _victoryPanel.Visible = false;
        }

        /// <summary>A full-screen ink plate. The four modal panels are the same shape, so they share this.</summary>
        private static Control CreatePanel(Control parent, string name, float alpha)
        {
            var panel = new ColorRect
            {
                Name = name,
                Color = new Color(PanelInk.R, PanelInk.G, PanelInk.B, alpha),
                // Stop, unlike the HUD plate: a modal panel is supposed to swallow clicks aimed at
                // whatever is behind it.
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            parent.AddChild(panel);
            panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            return panel;
        }

        private static Label CreateVictoryText(Control parent, string name, Font font, string content, int fontSize, Vector2 position)
        {
            var label = new Label
            {
                Name = name,
                Text = content,
                LabelSettings = Face(font, fontSize, Bone100),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            parent.AddChild(label);
            PlaceRect(label, Centre, Centre, position, new Vector2(560f, 70f));
            return label;
        }

        private static Button CreateVictoryButton(Control parent, Font font, string name, string label, Vector2 position)
        {
            var button = new Button { Name = name, Text = label };
            parent.AddChild(button);
            StyleMenuButton(button, font, 24);
            PlaceRect(button, Centre, Centre, position, new Vector2(280f, 56f));
            return button;
        }

        /// <summary>
        /// The plate a menu button is drawn on, in every state it has.
        ///
        /// uGUI's <c>ColorBlock</c> multiplied the target graphic, so a block could only ever darken it:
        /// the graphic was the brightest state (#2E3038) and the block stepped down from it - see
        /// Docs/MoodDirection.md section 4. Godot has no multiply; each state gets its own StyleBoxFlat
        /// with the product already worked out, which is the same picture with none of the arithmetic at
        /// runtime. Godot's focus box is what uGUI called <c>selectedColor</c>, and it draws over the
        /// others, so it carries the full-brightness plate.
        /// </summary>
        private static void StyleMenuButton(Button button, Font font, int fontSize)
        {
            var plate = new Color(0.18039216f, 0.1882353f, 0.21960784f, 0.94f); // #2E3038
            button.AddThemeStyleboxOverride("normal", Plate(Mul(plate, 0.5686275f)));      // #919191 multiply
            button.AddThemeStyleboxOverride("hover", Plate(plate));                        // white multiply
            button.AddThemeStyleboxOverride("focus", Plate(plate));
            button.AddThemeStyleboxOverride("pressed", Plate(Mul(plate, 0.32156864f)));    // #525252
            button.AddThemeStyleboxOverride("disabled", Plate(Mul(plate, 0.36078432f, 0.6f))); // #5C5C5C a0.60

            if (font != null)
                button.AddThemeFontOverride("font", font);
            button.AddThemeFontSizeOverride("font_size", fontSize);
            button.AddThemeColorOverride("font_color", Bone100);
            button.AddThemeColorOverride("font_hover_color", Bone100);
            button.AddThemeColorOverride("font_focus_color", Bone100);
            button.AddThemeColorOverride("font_pressed_color", Bone100);
            button.AddThemeColorOverride("font_disabled_color", new Color(Bone300.R, Bone300.G, Bone300.B, 0.6f));
        }

        internal static StyleBoxFlat Plate(Color color) => new StyleBoxFlat { BgColor = color };

        /// <summary>RGB-only multiply, the way uGUI's ColorBlock treated an opaque block entry.</summary>
        internal static Color Mul(Color c, float m, float alphaScale = 1f) =>
            new Color(c.R * m, c.G * m, c.B * m, c.A * alphaScale);

        internal static LabelSettings Face(Font font, int fontSize, Color color) =>
            new LabelSettings { Font = font, FontSize = fontSize, FontColor = color };

        /// <summary>
        /// Built up front and hidden, like the victory panel, so opening the pause menu is a visibility
        /// flip rather than a burst of allocation on the frame the player presses Escape.
        /// Reuses the victory panel's text and button builders - the two panels are the same shape.
        /// </summary>
        private void CreatePausePanel(Control parent, Font font)
        {
            _pausePanel = CreatePanel(parent, "PausePanel", 0.88f);

            Label title = CreateVictoryText(_pausePanel, "PauseTitle", font, Tr("UI_PAUSE_TITLE"), 48, new Vector2(0f, 140f));
            title.LabelSettings.FontColor = Bone100;
            _pauseStatusText = CreateVictoryText(_pausePanel, "PauseStatus", font, string.Empty, 20, new Vector2(0f, 90f));
            _pauseStatusText.LabelSettings.FontColor = Cold200;

            _resumeButton = CreateVictoryButton(_pausePanel, font, "ResumeButton", Tr("UI_PAUSE_RESUME"), new Vector2(0f, 20f));
            _saveButton = CreateVictoryButton(_pausePanel, font, "SaveButton", Tr("UI_PAUSE_SAVE"), new Vector2(0f, -50f));
            _pauseTitleButton = CreateVictoryButton(_pausePanel, font, "PauseTitleButton", Tr("UI_COMMON_RETURN_TO_TITLE"), new Vector2(0f, -120f));
            _pausePanel.Visible = false;
        }

        /// <summary>
        /// Godot's <c>Pressed</c> signal has no RemoveAllListeners, so every panel that rebinds its
        /// buttons per open keeps the delegate it added and detaches that one. Same effect as uGUI's
        /// clear-then-add, without leaving a stale click on the button.
        /// </summary>
        private static void Rebind(Button button, ref Action slot, Action action)
        {
            if (button == null)
                return;

            if (slot != null)
                button.Pressed -= slot;

            slot = action;
            if (slot != null)
                button.Pressed += slot;
        }

        private Action _resumeHandler;
        private Action _saveHandler;
        private Action _pauseTitleHandler;

        public void SetPauseVisible(bool visible, Action resumeAction, Action saveAction, Action titleAction)
        {
            if (_pausePanel == null)
                return;

            Rebind(_resumeButton, ref _resumeHandler, visible ? resumeAction : null);
            Rebind(_saveButton, ref _saveHandler, visible ? saveAction : null);
            Rebind(_pauseTitleButton, ref _pauseTitleHandler, visible ? titleAction : null);

            if (_pauseStatusText != null)
                _pauseStatusText.Text = string.Empty;

            _pausePanel.Visible = visible;

            if (visible)
                _resumeButton.GrabFocus();
        }

        /// <summary>
        /// The gate portal's panel. Its rows are the only ones in the HUD that cannot be built up front -
        /// how many gates are open changes with the save - so the frame and the close button are built
        /// here and the list is filled in <see cref="SetGateTravelVisible"/>.
        /// </summary>
        private void CreateGateTravelPanel(Control parent, Font font)
        {
            _gateTravelFont = font;

            _gateTravelPanel = CreatePanel(parent, "GateTravelPanel", 0.92f);

            Label title = CreateVictoryText(_gateTravelPanel, "GateTravelTitle", font, Tr("UI_GATE_TRAVEL"), 44, new Vector2(0f, 300f));
            title.LabelSettings.FontColor = Bone100;

            _gateTravelList = new Control { Name = "GateList", MouseFilter = Control.MouseFilterEnum.Ignore };
            _gateTravelPanel.AddChild(_gateTravelList);
            _gateTravelList.SetAnchorsPreset(Control.LayoutPreset.FullRect);

            _gateTravelCloseButton = CreateVictoryButton(_gateTravelPanel, font, "GateTravelCloseButton", Tr("UI_COMMON_CLOSE"), new Vector2(0f, -320f));
            _gateTravelPanel.Visible = false;
        }

        private Action _gateCloseHandler;

        /// <summary>
        /// Shows the gates this save may travel to. The gate the player is standing in is listed and
        /// disabled rather than left out, because a list that silently drops a row reads as a gate that
        /// closed.
        /// </summary>
        /// <remarks>
        /// The rows are destroyed and rebuilt on every open. Opening a portal is rare, the list is at
        /// most one row per chapter, and the alternative - a pool of hidden buttons kept in sync with the
        /// save - is more state than the thing it saves.
        /// </remarks>
        public void SetGateTravelVisible(bool visible, string[] gates, string[] titles, string currentScene, Action<string> pickAction, Action closeAction)
        {
            if (_gateTravelPanel == null)
                return;

            foreach (Node row in _gateTravelList.GetChildren())
            {
                // Detached before freeing: QueueFree is deferred to the end of the frame, and the rows
                // below are built right now, so a plain QueueFree would draw the old list under the new
                // one for a frame.
                _gateTravelList.RemoveChild(row);
                row.QueueFree();
            }

            Rebind(_gateTravelCloseButton, ref _gateCloseHandler, visible ? closeAction : null);

            if (visible)
                BuildGateRows(gates, titles, currentScene, pickAction);

            _gateTravelPanel.Visible = visible;

            if (visible)
                _gateTravelCloseButton.GrabFocus();
        }

        /// <summary>
        /// One row per gate. The titles come in from the caller because the chapter's name lives on data
        /// in the Enemy namespace, which UI is not allowed to reference; a missing or short title array
        /// falls back to the scene name rather than leaving a blank button.
        /// </summary>
        private void BuildGateRows(string[] gates, string[] titles, string currentScene, Action<string> pickAction)
        {
            if (gates == null)
                return;

            const float step = 62f;
            float top = (gates.Length - 1) * 0.5f * step;

            for (var i = 0; i < gates.Length; i++)
            {
                string gate = gates[i];
                string title = titles != null && i < titles.Length && !string.IsNullOrWhiteSpace(titles[i])
                    ? titles[i]
                    : gate;

                Button row = CreateVictoryButton(
                    _gateTravelList,
                    _gateTravelFont,
                    $"Gate{i}Button",
                    $"{i + 1}. {title}",
                    new Vector2(0f, top - i * step));

                if (gate == currentScene)
                {
                    row.Disabled = true;
                    continue;
                }

                if (pickAction != null)
                {
                    // Captured into a local first: the loop variable would be read at click time, and
                    // every row would travel to the last gate in the list.
                    string target = gate;
                    row.Pressed += () => pickAction(target);
                }
            }
        }

        /// <summary>
        /// The checkpoint's soul sink. Built up front and hidden like the pause panel rather than filled
        /// on open like the gate list, because the row count is fixed: there are four stats and there
        /// always will be. Only the labels change.
        /// </summary>
        private void CreateLevelUpPanel(Control parent, Font font)
        {
            _levelUpPanel = CreatePanel(parent, "LevelUpPanel", 0.92f);

            Label title = CreateVictoryText(_levelUpPanel, "LevelUpTitle", font, Tr("UI_LEVEL_UP"), 44, new Vector2(0f, 300f));
            title.LabelSettings.FontColor = Bone100;

            _levelUpSoulsText = CreateVictoryText(_levelUpPanel, "LevelUpSouls", font, string.Empty, 24, new Vector2(0f, 236f));
            _levelUpSoulsText.LabelSettings.FontColor = Cold200;

            _levelUpButtons = new Button[LevelUpStats.Length];

            const float step = 70f;
            float top = (LevelUpStats.Length - 1) * 0.5f * step;

            for (var i = 0; i < LevelUpStats.Length; i++)
            {
                // Fresh local per iteration, for the reason spelled out in BuildGateRows: the loop
                // variable would be read at click time and every row would buy Resolve.
                PlayerStat stat = LevelUpStats[i];

                Button row = CreateVictoryButton(
                    _levelUpPanel, font, $"LevelUp{stat}Button", string.Empty, new Vector2(0f, top - i * step));

                // Wider and smaller-lettered than a menu button: a row carries four columns of text
                // (stat, level, what a level gives, price) where 계속하기 carries one word.
                PlaceRect(row, Centre, Centre, new Vector2(0f, top - i * step), new Vector2(520f, 56f));
                row.AddThemeFontSizeOverride("font_size", 20);

                _levelUpButtons[i] = row;

                // Added once and never removed: unlike the gate rows these buttons outlive an open.
                row.Pressed += () => PurchaseLevel(stat);
            }

            _levelUpCloseButton = CreateVictoryButton(_levelUpPanel, font, "LevelUpCloseButton", Tr("UI_COMMON_CLOSE"), new Vector2(0f, -320f));
            _levelUpCloseButton.Pressed += CloseLevelUp;
            _levelUpPanel.Visible = false;
        }

        /// <summary>
        /// Opens the level-up panel on the player who just rested. Takes the player node rather than a
        /// <see cref="PlayerProgression"/> because <see cref="PlayerProgression.EnsureOn"/> is the way in
        /// from outside its namespace - the shipped player predates the component, so asking for it
        /// directly would make the caller handle a null that this handles for it.
        /// </summary>
        /// <remarks>
        /// <para><b>This panel does not touch <see cref="GameClock.TimeScale"/>, on purpose.</b> Two
        /// owners already share it and they needed an interlock to stop closing one from unfreezing the
        /// game underneath the other; a third owner would put that defect straight back. Levelling reads
        /// as a pause because the player is stood still at a checkpoint, not because the clock stopped.
        /// If the world should genuinely freeze here, that call belongs to whoever already owns the
        /// freeze, and it is one line at the call site rather than anything in this file.</para>
        ///
        /// <para>Nothing here reads input either. The panel is buttons and a close button, like the gate
        /// list, so it cannot punch through a menu the way a raw key read would.</para>
        /// </remarks>
        public void SetLevelUpVisible(bool visible, Node player, Action closeAction = null)
        {
            if (_levelUpPanel == null)
                return;

            _progression = visible ? PlayerProgression.EnsureOn(player) : null;
            _levelUpCloseAction = visible ? closeAction : null;
            _levelUpPanel.Visible = visible;

            if (!visible)
                return;

            RefreshLevelUp();
            _levelUpCloseButton.GrabFocus();
        }

        private void CloseLevelUp()
        {
            _levelUpPanel.Visible = false;
            _levelUpCloseAction?.Invoke();
        }

        /// <summary>
        /// A refused purchase is silent: the row is already disabled when the purse is short or the stat
        /// is capped, so the only way to arrive here on a refusal is a race with souls lost elsewhere -
        /// and the refresh below tells that story better than a message would.
        /// </summary>
        private void PurchaseLevel(PlayerStat stat)
        {
            if (_progression != null)
                _progression.TryPurchase(stat);

            RefreshLevelUp();
        }

        /// <summary>
        /// Every number on this panel is read back out of <see cref="PlayerProgression"/> - the level, the
        /// price, the cap and what a level gives. None of them are written here. The tuning behind them is
        /// designer-owned and is expected to move again after the first stopwatch pass, and a literal in
        /// this method would become a lie one edit later without anything failing to compile.
        /// </summary>
        private void RefreshLevelUp()
        {
            if (_progression == null)
                return;

            // Read off the player rather than off the HUD's own _wallet, so the panel does not depend on
            // BindCombatResources having run first.
            // PlayerProgression is a plain class composed into the controller here, not a node, so the
            // lookup starts from the player node it was created on.
            var wallet = FindComponent<SoulsWallet>(_progression.Owner);
            _levelUpSoulsText.Text = $"Karma: {(wallet != null ? wallet.Souls : 0)}";

            ProgressionTuningData tuning = _progression.Tuning;

            for (var i = 0; i < LevelUpStats.Length; i++)
            {
                PlayerStat stat = LevelUpStats[i];
                int level = _progression.LevelOf(stat);

                // A capped stat says MAX where the price would be, rather than disappearing: both a capped
                // row and an unaffordable one grey out, and the label is what tells them apart. Kept to
                // ASCII and the two words already on the HUD - the fallback face is whatever Godot ships
                // and a headless run cannot show whether it has a glyph, so an arrow here would be an
                // unverifiable tofu box for no information the price does not already carry.
                _levelUpButtons[i].Text = _progression.IsAtCap(stat)
                    ? $"{stat}   Lv.{level}   MAX"
                    : $"{stat}   Lv.{level}   {GainLabel(stat, tuning)}   {_progression.CostOf(stat)}";

                _levelUpButtons[i].Disabled = !_progression.CanPurchase(stat);
            }
        }

        /// <summary>What one level buys, in the units the HUD already uses for those four readouts.</summary>
        private static string GainLabel(PlayerStat stat, ProgressionTuningData tuning)
        {
            switch (stat)
            {
                case PlayerStat.Vitality: return $"Health +{tuning.vitalityPerLevel:0.##}";
                case PlayerStat.Endurance: return $"Stamina +{tuning.endurancePerLevel:0.##}";
                case PlayerStat.Strength: return $"Attack +{tuning.strengthPerLevel:0.##}";
                case PlayerStat.Resolve: return $"Poise +{tuning.resolvePerLevel:0.##}";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Unity's <c>GetComponent&lt;T&gt;</c> for "another component on the same actor". Components
        /// that shared a GameObject are sibling or child nodes here, so the search is: this node, its
        /// children, then its parent's children. Local to the HUD rather than a Core shim, because it
        /// only exists to keep two lines of the Unity original honest.
        /// </summary>
        private static T FindComponent<T>(Node node) where T : class
        {
            if (node == null)
                return null;

            if (node is T self)
                return self;

            foreach (Node child in node.GetChildren())
            {
                if (child is T found)
                    return found;
            }

            Node parent = node.GetParent();
            if (parent == null)
                return null;

            foreach (Node sibling in parent.GetChildren())
            {
                if (sibling is T found)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Confirms a save in place instead of as a timed toast: the pause menu runs at a zero timescale,
        /// where a scaled scene timer never fires, so a toast would hang there forever.
        /// </summary>
        public void ShowPauseSaved()
        {
            if (_pauseStatusText != null)
                _pauseStatusText.Text = Tr("UI_PAUSE_SAVED");
        }

        /// <summary>
        /// Confirms a checkpoint rest. Resting had no feedback at all before this - health, stamina and
        /// flask all came back and the screen said nothing, so a player could not tell a rest from
        /// standing next to one.
        /// </summary>
        /// <remarks>
        /// Shares the warning line and its unscaled expiry rather than owning a second text object: the
        /// two never compete in practice, and the humanity warning is the one that should win if they
        /// ever do. Wording matches <see cref="ShowPauseSaved"/> - the same event, told the same way.
        /// The tone is [[MoodDirection]]'s: bone, not a celebration.
        /// </remarks>
        public void ShowCheckpointSaved()
        {
            if (_warningText == null) return;

            _warningText.Text = Tr("UI_PAUSE_SAVED");
            _warningText.LabelSettings.FontColor = Bone200;
            _warningExpiryUnscaled = GameClock.UnscaledTime + 2f;
        }

        private Action _restartHandler;
        private Action _victoryTitleHandler;

        /// <summary>
        /// The victory panel. <paramref name="primaryLabel"/> renames the first button for the run of
        /// the campaign where beating a boss opens the next gate rather than ending the game - the same
        /// button, a different thing to do, so relabelling it beats a second button nobody presses.
        /// </summary>
        public void ShowVictory(
            Action restartAction, Action titleAction, string primaryLabel = null, string subtitle = null)
        {
            if (_victoryPanel == null)
                return;

            // Restored rather than left alone when no label is given: a panel re-shown after a rematch
            // would otherwise still read 다음 관문 over a button that restarts.
            _restartButton.Text = !string.IsNullOrEmpty(primaryLabel) ? primaryLabel : Tr(DefaultRestartLabelKey);

            if (_victorySubtitle != null && !string.IsNullOrEmpty(subtitle))
                _victorySubtitle.Text = subtitle;

            Rebind(_restartButton, ref _restartHandler, restartAction);
            Rebind(_titleButton, ref _victoryTitleHandler, titleAction);

            _victoryPanel.Visible = true;
            _restartButton.GrabFocus();
        }

        /// <summary>
        /// A dark strip with a fill child, sized to sit under one text row. The fill is driven by its
        /// right anchor rather than by a ProgressBar, because the bar is two stacked layers (the live
        /// fill and the ghost behind it) sharing one strip, which a ProgressBar cannot express.
        /// Returns the fill - <see cref="SetFill"/> is what moves it.
        /// </summary>
        private static ColorRect CreateBar(Control parent, string name, Color fillColor, Vector2 position)
        {
            var strip = new ColorRect
            {
                Name = name,
                Color = new Color(PanelInk.R, PanelInk.G, PanelInk.B, 0.85f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            parent.AddChild(strip);
            // 240 wide keeps the strip inside the 280px HUD plate; 26 tall covers the glyph band of a
            // 22pt row without spilling into the row below it (rows are 30px apart).
            PlaceRect(strip, TopLeft, TopLeft, position, new Vector2(240, 26));

            // Half-transparent over the ink strip: an opaque bone or slate fill wins the contrast fight
            // against the number printed on top of it, which is the one thing that must stay readable.
            return CreateFill(strip, "Fill", fillColor, 0.55f);
        }

        /// <summary>One left-anchored layer of a bar - the live fill, or the ghost behind it.</summary>
        private static ColorRect CreateFill(Control strip, string name, Color color, float alpha)
        {
            var fill = new ColorRect
            {
                Name = name,
                Color = new Color(color.R, color.G, color.B, alpha),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            strip.AddChild(fill);
            fill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            return fill;
        }

        /// <summary>Returns the ratio it applied, so a caller that also has to react to it need not divide twice.</summary>
        private static float SetFill(ColorRect fill, float current, float max)
        {
            float ratio = max > 0f ? Mathf.Clamp(current / max, 0f, 1f) : 0f;
            if (fill == null) return ratio;

            // The uGUI original moved anchorMax.x; the Godot equivalent is the right anchor with the
            // right offset pinned at zero, which is what the full-rect preset already left behind.
            fill.AnchorRight = ratio;
            fill.OffsetRight = 0f;
            return ratio;
        }

        private static Label CreateText(Control parent, string name, Font font, Color color, int fontSize, Vector2 position, HorizontalAlignment alignment)
        {
            bool centred = alignment == HorizontalAlignment.Center;
            var label = new Label
            {
                Name = name,
                LabelSettings = Face(font, fontSize, color),
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Top,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            parent.AddChild(label);
            Vector2 anchor = centred ? new Vector2(0.5f, 1f) : TopLeft;
            PlaceRect(label, anchor, anchor, position, new Vector2(centred ? 400 : 260, 30));
            return label;
        }

        public void Initialize(Health health, HumanityController humanity, SinResonanceController sinResonance, DeathStateController deathController, StaminaSystem stamina)
        {
            Initialize(health, humanity, sinResonance, deathController, stamina, null);
        }

        public void Initialize(Health health, HumanityController humanity, SinResonanceController sinResonance, DeathStateController deathController, StaminaSystem stamina, PlayerController2D player)
        {
            _health = health;
            _humanity = humanity;
            _sinResonance = sinResonance;
            _deathController = deathController;
            _stamina = stamina;
            _player = player;

            // UnityEvent.AddListener is a plain C# event subscription here.
            if (_health != null)
                _health.OnHealthChanged += UpdateHealth;

            if (_humanity != null)
            {
                _humanity.OnHumanityChanged += UpdateHumanity;
                _humanity.OnLowHumanityWarning += ShowLowHumanityWarning;
            }

            if (_sinResonance != null)
            {
                _sinResonance.OnSinStateChanged += UpdateActiveSin;
            }

            if (_stamina != null)
            {
                _stamina.OnStaminaChanged += UpdateStamina;
            }

            if (_deathController != null)
            {
                _deathController.OnDeath += ShowDeathWarning;
                _deathController.OnRespawn += ClearWarning;
            }

            ForceRefresh();
        }

        public void BindPlayer(PlayerController2D player)
        {
            _player = player;
            UpdateActionState();
        }

        /// <summary>
        /// Separate from <see cref="Initialize"/> rather than two more parameters on it, so the existing
        /// callers and their overload keep working untouched.
        /// </summary>
        public void BindCombatResources(Poise poise, SoulsWallet wallet)
        {
            _poise = poise;
            _wallet = wallet;

            if (_poise != null)
                _poise.OnPoiseChanged += UpdatePoise;

            if (_wallet != null)
                _wallet.OnSoulsChanged += UpdateSouls;

            UpdatePoise(_poise != null ? _poise.CurrentPoise : 0f);
            UpdateSouls(_wallet != null ? _wallet.Souls : 0);
        }

        public void ForceRefresh()
        {
            UpdateHealth(_health?.CurrentHealth ?? 0);
            UpdateHumanity(_humanity?.CurrentHumanity ?? 0);
            UpdateResonance();
            UpdateActiveSin(_sinResonance != null ? _sinResonance.CurrentSin : SinState.None);
            UpdateDeathCount();
            UpdateSpiritState();
            UpdateStamina(_stamina?.CurrentStamina ?? 0);
            UpdatePoise(_poise?.CurrentPoise ?? 0);
            UpdateSouls(_wallet?.Souls ?? 0);
            UpdateActionState();
        }

        private void UpdateHealth(float current)
        {
            if (_health == null) return;

            if (_healthText != null)
                _healthText.Text = $"Health: {Mathf.FloorToInt(_health.CurrentHealth)}/{_health.MaxHealth}";

            float ratio = SetFill(_healthFill, _health.CurrentHealth, _health.MaxHealth);
            if (ratio < _ghostRatio)
            {
                // Damage: leave the ghost where it was and start both timers. TickHealthGauge does the
                // rest, so a hit that lands during a hitstop still holds for its full 0.4s afterwards.
                _ghostHoldUntil = GameClock.Time + GhostHoldSeconds;
                _hitFlashUntil = GameClock.Time + HitFlashSeconds;
            }
            else if (ratio > _ghostRatio)
            {
                // Healing and respawning snap it: a ghost trailing *below* the fill is not a memory of
                // anything, just a second edge moving in the bar.
                _ghostRatio = ratio;
                SetFill(_ghostFill, _ghostRatio, 1f);
            }
        }

        /// <summary>
        /// Drains the ghost toward the live fill and expires the hit flash. Scaled time throughout, so
        /// both stop dead at timeScale 0 rather than running out behind the pause menu - Godot scales the
        /// delta handed to _Process by Engine.TimeScale, so a paused clock hands this a zero delta.
        /// </summary>
        private void TickHealthGauge(float delta)
        {
            if (_healthFill == null) return;

            float ratio = _healthFill.AnchorRight;
            if (_ghostRatio > ratio && GameClock.Time >= _ghostHoldUntil)
            {
                // Constant rate rather than a lerp toward the target: a whole bar takes GhostDrainSeconds
                // and a scratch is proportionally quicker, which is what makes the size of a hit readable.
                // A lerp spends the same wall-clock time on both and never quite lands on the target.
                _ghostRatio = Mathf.Max(ratio, _ghostRatio - delta / GhostDrainSeconds);
                SetFill(_ghostFill, _ghostRatio, 1f);
            }
            else if (_ghostRatio < ratio)
            {
                _ghostRatio = ratio;
                SetFill(_ghostFill, _ghostRatio, 1f);
            }

            // Bone over ember for the flash frame. ColorRect.Color early-outs on an unchanged value, so
            // writing it every frame costs no redraw.
            _healthFill.Color = GameClock.Time < _hitFlashUntil
                ? new Color(Bone100.R, Bone100.G, Bone100.B, 0.85f)
                : new Color(Ember300.R, Ember300.G, Ember300.B, 0.55f);
        }

        private void UpdateHumanity(float current)
        {
            if (_humanity != null && _humanityText != null)
                _humanityText.Text = $"Humanity: {Mathf.FloorToInt(_humanity.CurrentHumanity)}/{_humanity.MaxHumanity}";
        }

        private void UpdateResonance()
        {
            if (_sinResonance != null && _resonanceText != null)
                // The denominator was the literal 100. It agreed with SinTuning.json's maxResonance and
                // would have stopped agreeing the moment a designer retuned it; MaxResonance is the value
                // ApplyTuning actually took from that file.
                _resonanceText.Text = $"Resonance: {Mathf.FloorToInt(_sinResonance.CurrentResonance)} / {_sinResonance.MaxResonance}";
        }

        private void UpdateActiveSin(SinState sin)
        {
            if (_activeSinText == null) return;

            if (sin == SinState.None)
            {
                _activeSinText.Text = "Active: None";
                _activeSinText.LabelSettings.FontColor = Bone300;
            }
            else
            {
                _activeSinText.Text = $"Active: {sin}";
                _activeSinText.LabelSettings.FontColor = GetSinColor(sin);
            }
        }

        private void UpdateDeathCount()
        {
            if (_deathController != null && _deathCountText != null)
                _deathCountText.Text = $"Deaths: {_deathController.DeathCount}";
        }

        private void UpdateSpiritState()
        {
            if (_spiritStateText == null) return;

            bool inSpirit = _deathController != null && _deathController.IsInSpiritState;
            if (inSpirit)
            {
                _spiritStateText.Text = "SPIRIT FORM";
                _spiritStateText.LabelSettings.FontColor = Cold200;
            }
            else
            {
                _spiritStateText.Text = "";
            }
        }

        private void UpdateStamina(float current)
        {
            if (_stamina == null) return;

            if (_staminaText != null)
                _staminaText.Text = $"Stamina: {Mathf.FloorToInt(_stamina.CurrentStamina)}/{_stamina.MaxStamina}";
            SetFill(_staminaFill, _stamina.CurrentStamina, _stamina.MaxStamina);
        }

        private void UpdatePoise(float current)
        {
            // The gauge follows the same condition the line does - an empty poise row with a strip still
            // sitting under it would read as a bar stuck at zero rather than as a stat that does not apply.
            bool readable = _poise != null && _poise.CanBreak;
            if (_poiseGauge != null && _poiseGauge.Visible != readable)
                _poiseGauge.Visible = readable;

            if (!readable)
            {
                if (_poiseText != null)
                    _poiseText.Text = "";
                return;
            }

            bool staggered = _player != null && _player.IsStaggered;
            if (_poiseText != null)
            {
                _poiseText.Text = staggered
                    ? "Poise: STAGGERED"
                    : $"Poise: {Mathf.FloorToInt(_poise.CurrentPoise)}/{_poise.MaxPoise}";
                _poiseText.LabelSettings.FontColor = staggered ? Ember300 : Bone200;
            }

            SetFill(_poiseFill, _poise.CurrentPoise, _poise.MaxPoise);
            if (_poiseFill != null)
                // Brighter as well as red while staggered: the strip is the peripheral read, and the
                // stagger is the one poise state the player has to catch without looking at the numbers.
                _poiseFill.Color = staggered
                    ? new Color(Ember300.R, Ember300.G, Ember300.B, 0.75f)
                    : new Color(Bone300.R, Bone300.G, Bone300.B, 0.55f);
        }

        private void UpdateSouls(int current)
        {
            if (_soulsText != null)
                // Karma is what the world calls it (WorldSetting). The C# symbols stay Souls - renaming
                // SoulsWallet, soulReward and the save keys to match would be a much larger change than
                // the one the player can see.
                _soulsText.Text = $"Karma: {current}";
        }

        /// <summary>
        /// Polled rather than event-driven: charges change from a drink, a rest and a respawn, and adding
        /// an event to each of those three would be more wiring than reading an int once a frame.
        /// </summary>
        private void UpdateFlask()
        {
            if (_flaskText == null) return;

            if (_player == null || _player.MaxHealCharges <= 0)
            {
                _flaskText.Text = "";
                return;
            }

            _flaskText.Text = $"Flask: {_player.HealCharges}/{_player.MaxHealCharges}";

            // Dim when empty rather than hidden. A missing readout reads as a bug; a dim one reads as
            // nothing left, which is the thing the player has to notice before walking into the boss.
            _flaskText.LabelSettings.FontColor = _player.HealCharges > 0 ? Bone200 : Bone300;
        }

        private void UpdateActionState()
        {
            if (_actionText == null) return;

            var actions = _player != null ? FindComponent<PlayerActionController>(_player) : null;
            if (actions == null)
            {
                _actionText.Text = "";
                return;
            }

            string phase = actions.CurrentAttackPhase.ToString();
            string grounded = _player.IsGrounded ? "Grounded" : "Airborne";
            _actionText.Text = $"Action: {phase} / {grounded}";
        }

        private Color GetSinColor(SinState sin)
        {
            return sin switch
            {
                SinState.Wrath => new Color(0.54901963f, 0.20392157f, 0.15686275f), // #8C3428
                SinState.Sloth => new Color(0.24705882f, 0.29019609f, 0.3882353f),  // #3F4A63
                SinState.Pride => new Color(0.47843137f, 0.41568628f, 0.23529412f), // #7A6A3C

                // The four the table filled in on 2026-08-10. Desaturated the same way the first three
                // are, and leaning on the chapter each sin is named for (CombatTypes: Gluttony is the
                // orange chapter, Greed yellow, Envy green) - except Lust, which takes rose rather than
                // its chapter's blue, because blue is already Sloth and two sins that read the same
                // colour is worse than one that does not match its chapter.
                SinState.Gluttony => new Color(0.54901963f, 0.35294119f, 0.15686275f), // #8C5A28
                SinState.Greed => new Color(0.54901963f, 0.47843137f, 0.15686275f),    // #8C7A28
                SinState.Envy => new Color(0.24705882f, 0.3882353f, 0.27843137f),      // #3F6347
                SinState.Lust => new Color(0.47843137f, 0.24705882f, 0.36078432f),     // #7A3F5C
                _ => Bone300
            };
        }

        /// <summary>
        /// Expires on unscaled time, checked from _Process, for the same reason ShowPauseSaved refuses to
        /// be a toast: the pause menu runs at timeScale 0, where a scaled SceneTreeTimer never fires, so
        /// an awaited 2-second timer would leave the humanity warning on screen for the whole pause.
        /// Negative means nothing is pending.
        /// </summary>
        private float _warningExpiryUnscaled = -1f;

        private void ShowLowHumanityWarning()
        {
            if (_warningText == null) return;
            _warningText.Text = "HUMANITY FADING";
            _warningText.LabelSettings.FontColor = Ember300;
            _warningExpiryUnscaled = GameClock.UnscaledTime + 2f;
        }

        private void ShowDeathWarning()
        {
            if (_warningText == null) return;
            _warningText.Text = "YOU DIED";
            _warningText.LabelSettings.FontColor = Ember300;
            // YOU DIED holds until the respawn clears it, so drop any humanity timer still pending.
            _warningExpiryUnscaled = -1f;
        }

        private void ClearWarning()
        {
            _warningExpiryUnscaled = -1f;
            if (_warningText != null)
                _warningText.Text = "";
        }

        public override void _Process(double delta)
        {
            if (_warningExpiryUnscaled >= 0f && GameClock.UnscaledTime >= _warningExpiryUnscaled)
                ClearWarning();

            if (_sinResonance != null)
                UpdateResonance();

            if (_deathController != null)
            {
                UpdateDeathCount();
                UpdateSpiritState();
            }

            // The stagger lock ends on a timer rather than a poise event, so the readout has to be
            // refreshed here or it would keep saying STAGGERED after the player is free again.
            if (_poise != null)
                UpdatePoise(_poise.CurrentPoise);

            TickHealthGauge((float)delta);
            UpdateActionState();
            UpdateFlask();
        }
    }
}
