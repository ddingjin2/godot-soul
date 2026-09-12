using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.UI
{
    /// <summary>
    /// The gameplay HUD. The Unity original was a <c>MonoBehaviour</c> on the screen-space
    /// <c>Canvas</c>; here it is the <see cref="CanvasLayer"/> itself, and every uGUI <c>Image</c> is a
    /// <see cref="ColorRect"/>, every <c>Text</c> a <see cref="Label"/>.
    ///
    /// The screen is no longer assembled here. <c>Scenes/UI/GameplayHud.tscn</c> is the whole
    /// hierarchy - the layer, its process mode, the plate, the three <c>Scenes/UI/HudBar.tscn</c>
    /// gauges, the twelve readouts and the four panels, which inherit
    /// <c>Scenes/UI/ModalPanel.tscn</c> - and every button's styling is
    /// <c>Resources/UI/MenuTheme.tres</c>. What is left in this file is binding, the per-frame
    /// readouts and the two timers, which is what a script is for.
    ///
    /// The one thing still built at runtime is the gate-travel list, because how many gates a save
    /// has opened is not knowable until it is opened: <see cref="BuildGateRows"/> instantiates
    /// <c>Scenes/UI/GateTravelRow.tscn</c> per row.
    ///
    /// All numbers in this file are screen pixels, exactly as they were in uGUI - <c>World.Ppu</c> is a
    /// world-space conversion and has no business here.
    /// </summary>
    public partial class GameplayHud : CanvasLayer
    {
        /// <summary>
        /// The Theme type the mood palette is filed under in <c>Resources/UI/MenuTheme.tres</c>. No node
        /// carries it: a Theme is what Godot has instead of a colour asset, and this is the HUD reading
        /// the palette out of it rather than typing it a second time (PLAN_CLOSEOUT A8/B2, decision D5).
        /// </summary>
        private const string PaletteType = "Palette";

        private static Theme _palette;

        /// <summary>
        /// One mood token, by its name in the Theme. A dictionary hit per read, on a Theme the loader
        /// has already cached - cheap enough for the handful of tints a frame does, and the point is
        /// that there is nowhere else the colour could have come from.
        /// </summary>
        private static Color Hue(string token)
        {
            _palette ??= LoadMenuTheme();
            return _palette != null ? _palette.GetColor(token, PaletteType) : default;
        }

        // Mood palette tokens - Docs/MoodDirection.md section 2, authored in MenuTheme.tres.
        private static Color Bone100 => Hue("bone_100");   // #C3BDB1 text primary
        private static Color Bone200 => Hue("bone_200");   // #9A9488 text secondary
        private static Color Bone300 => Hue("bone_300");   // #6E685C text dim
        private static Color Cold200 => Hue("cold_200");   // #8492A0 souls / spirit
        private static Color Ember300 => Hue("ember_300"); // #A84B33 warning

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
        // Seconds, from Resources/Design/UiTuning.json, read once in Bind. Zero until then and zero if
        // the file is missing, which is an error rather than a second set of numbers (D1) - a missing
        // file shows as no hold and an instant drain, not as the shipped feel.
        private float _ghostHoldSeconds;
        private float _ghostDrainSeconds;
        private float _hitFlashSeconds;
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

        /// <summary>
        /// The authored button styling, shared with the title screen. Everything a menu button looks
        /// like - the five state plates, the font sizes and the font colour per state - lives in
        /// <c>Resources/UI/MenuTheme.tres</c>; this hands it out and is the only styling call left.
        /// </summary>
        /// <remarks>
        /// The font is the one thing the resource does not carry, and is written here instead. An
        /// <c>ext_resource</c> pointing at a font Godot has not imported yet is a hard load failure,
        /// which on a cold clone would take both UI screens down with it; <see cref="LoadUiFont"/>
        /// degrades to <c>ThemeDB.FallbackFont</c> and the menus still draw. <c>CutsceneOverlay.Bind</c>
        /// makes the same trade for the same reason. A Theme falls back to <c>default_font</c> for any
        /// type that names no font of its own, so this reaches every variation in the resource too.
        ///
        /// <c>Res.Load</c> hands back the cached instance, so the assignment lands on the same resource
        /// the <c>.tscn</c> files reference through their own <c>ext_resource</c>.
        /// </remarks>
        internal static Theme LoadMenuTheme()
        {
            var theme = Res.Load<Theme>("UI/MenuTheme", ".tres");
            if (theme != null && theme.DefaultFont == null)
                theme.DefaultFont = LoadUiFont();
            return theme;
        }

        /// <summary>The authored gate-travel row. Instanced once per gate the save has opened.</summary>
        private const string GateRowScenePath = "res://Scenes/UI/GateTravelRow.tscn";

        public override void _Ready()
        {
            // ProcessMode is authored on the scene's root as Always, and it is load-bearing: the warning
            // line expires on unscaled time and the menus have to keep answering while the world is
            // stopped. The other half of that pair is that the ghost gauge and the hit flash stay on
            // scaled time and freeze at TimeScale 0 anyway - see TickHealthGauge.
            Bind();
        }

        /// <summary>
        /// Resolves the authored hierarchy. It builds nothing: <c>Scenes/UI/GameplayHud.tscn</c> owns
        /// every node, anchor, colour and font size, and this only picks up the references the readouts
        /// write to. Idempotent, because <see cref="CreateUi"/> calls it for the headless case where the
        /// HUD never entered a tree and so never got a <c>_Ready</c>.
        /// </summary>
        /// <remarks>
        /// The node names are the contract, and they are how the layout reads: HudRoot, the three
        /// gauges, the eleven readouts, and one child per panel. Two tests reach in by name -
        /// GameplayHealItemTests for FlaskText and GameplayVictoryPanelTests for the victory panel's
        /// Subtitle and RestartButton - so a rename here is a rename there.
        /// </remarks>
        private void Bind()
        {
            if (_root != null)
                return;

            _root = GetNodeOrNull<Control>("HudRoot");
            if (_root == null)
                return;

            // Writes the Korean face into the shared Theme the scenes reference through their own
            // ext_resource - it is the same cached instance, and the resource deliberately names no font
            // of its own. Has to happen before the first draw or every label falls back to Godot's face.
            LoadMenuTheme();

            // The two timers' seconds, from the designer's file. The bootstrap has already refused to
            // build an arena on an incomplete catalog, so null here means a HUD stood up outside one.
            UiTuningData ui = GameplayTuningCatalog.Load()?.UiTuning;
            if (ui == null)
            {
                GD.PushError("GameplayHud: Resources/Design/UiTuning.json is missing; the ghost gauge and the hit flash have no timings.");
            }
            else
            {
                _ghostHoldSeconds = ui.ghostHoldSeconds;
                _ghostDrainSeconds = ui.ghostDrainSeconds;
                _hitFlashSeconds = ui.hitFlashSeconds;
            }

            var healthGauge = _root.GetNode<Control>("HealthGauge");
            _healthFill = healthGauge.GetNode<ColorRect>("Fill");
            _ghostFill = healthGauge.GetNode<ColorRect>("GhostFill");
            _staminaFill = _root.GetNode<ColorRect>("StaminaGauge/Fill");
            _poiseGauge = _root.GetNode<Control>("PoiseGauge");
            _poiseFill = _poiseGauge.GetNode<ColorRect>("Fill");

            _healthText = _root.GetNode<Label>("HealthText");
            _humanityText = _root.GetNode<Label>("HumanityText");
            _resonanceText = _root.GetNode<Label>("ResonanceText");
            _activeSinText = _root.GetNode<Label>("ActiveSinText");
            _deathCountText = _root.GetNode<Label>("DeathCountText");
            _spiritStateText = _root.GetNode<Label>("SpiritStateText");
            _staminaText = _root.GetNode<Label>("StaminaText");
            _poiseText = _root.GetNode<Label>("PoiseText");
            _soulsText = _root.GetNode<Label>("SoulsText");
            _flaskText = _root.GetNode<Label>("FlaskText");
            _actionText = _root.GetNode<Label>("ActionText");
            _warningText = _root.GetNode<Label>("WarningText");

            _victoryPanel = _root.GetNode<Control>("VictoryPanel");
            // Subtitle, not VictorySubtitle: the four panels inherit ModalPanel.tscn, and Godot does not
            // let an inherited node be renamed. One name per role across all four panels.
            _victorySubtitle = _victoryPanel.GetNode<Label>("Subtitle");
            _restartButton = _victoryPanel.GetNode<Button>("RestartButton");
            _titleButton = _victoryPanel.GetNode<Button>("TitleButton");

            _pausePanel = _root.GetNode<Control>("PausePanel");
            _pauseStatusText = _pausePanel.GetNode<Label>("Subtitle");
            _resumeButton = _pausePanel.GetNode<Button>("ResumeButton");
            _saveButton = _pausePanel.GetNode<Button>("SaveButton");
            _pauseTitleButton = _pausePanel.GetNode<Button>("PauseTitleButton");

            _gateTravelPanel = _root.GetNode<Control>("GateTravelPanel");
            // The VBox inside the CenterContainer: the rows are added to the box and centred by the
            // container, which is what replaced the per-open column arithmetic.
            _gateTravelList = _gateTravelPanel.GetNode<Control>("GateListCenter/GateList");
            _gateTravelCloseButton = _gateTravelPanel.GetNode<Button>("GateTravelCloseButton");

            _levelUpPanel = _root.GetNode<Control>("LevelUpPanel");
            _levelUpSoulsText = _levelUpPanel.GetNode<Label>("Subtitle");
            _levelUpButtons = new Button[LevelUpStats.Length];
            for (var i = 0; i < LevelUpStats.Length; i++)
            {
                // Fresh local per iteration: the loop variable would be read at click time and every row
                // would buy Resolve.
                PlayerStat stat = LevelUpStats[i];
                Button row = _levelUpPanel.GetNode<Button>($"LevelUp{stat}Button");
                _levelUpButtons[i] = row;

                // Added once and never removed: unlike the gate rows these buttons outlive an open.
                row.Pressed += () => PurchaseLevel(stat);
            }

            _levelUpCloseButton = _levelUpPanel.GetNode<Button>("LevelUpCloseButton");
            _levelUpCloseButton.Pressed += CloseLevelUp;
        }

        /// <summary>
        /// Binds the HUD to its authored hierarchy. It used to build that hierarchy, and the callers
        /// still call it in the same place for the same reason - the HUD has to be usable before
        /// <see cref="Initialize"/> runs - so the entry point is kept even though the body is now one
        /// call. <paramref name="canvas"/> named the layer to build into and is ignored: the scene is
        /// the layer.
        /// </summary>
        public void CreateUi(CanvasLayer canvas = null)
        {
            Bind();
        }

        /// <summary>
        /// Recolours one readout. The scene authors each label's resting colour as a theme override;
        /// these are the four lines that change colour with what they say - the active sin, a staggered
        /// poise, an empty flask, and the warning line - which is state, not styling.
        /// </summary>
        private static void Tint(Label label, Color color) =>
            label.AddThemeColorOverride("font_color", color);

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
        /// <remarks>
        /// All that is left here is instantiate and bind. The row's rect is authored in
        /// <c>GateTravelRow.tscn</c> and where it sits in the column is the panel's VBoxContainer inside
        /// a CenterContainer - the 62px step and the half-column offset this used to compute per open are
        /// a 56px row and a separation of 6.
        /// </remarks>
        private void BuildGateRows(string[] gates, string[] titles, string currentScene, Action<string> pickAction)
        {
            if (gates == null)
                return;

            for (var i = 0; i < gates.Length; i++)
            {
                string gate = gates[i];
                string title = titles != null && i < titles.Length && !string.IsNullOrWhiteSpace(titles[i])
                    ? titles[i]
                    : gate;

                var row = GD.Load<PackedScene>(GateRowScenePath).Instantiate<Button>();
                row.Name = $"Gate{i}Button";
                row.Text = $"{i + 1}. {title}";
                _gateTravelList.AddChild(row);

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
            Tint(_warningText, Bone200);
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

            // The victory shot's completion can arrive from CutsceneDirector._ExitTree while the scene is
            // being torn down. Since K7 the director is authored at the front of the shell, so it leaves
            // the tree after the HUD, and a button that has already left cannot take focus (engine error,
            // not an exception). Nothing else in ShowVictory needs the tree.
            if (_restartButton.IsInsideTree())
                _restartButton.GrabFocus();
        }

        /// <summary>
        /// Moves one gauge's fill. The fill is driven by its right anchor rather than by a ProgressBar,
        /// because a bar here is two stacked layers - the live fill and the ghost behind it - sharing one
        /// strip, which a ProgressBar cannot express. The strip, both layers, the 240x26 rect and each
        /// bar's colour are authored in <c>Scenes/UI/HudBar.tscn</c> and its three instances.
        /// Returns the ratio it applied, so a caller that also has to react to it need not divide twice.
        /// </summary>
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
                _ghostHoldUntil = GameClock.Time + _ghostHoldSeconds;
                _hitFlashUntil = GameClock.Time + _hitFlashSeconds;
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
                // Constant rate rather than a lerp toward the target: a whole bar takes ghostDrainSeconds
                // and a scratch is proportionally quicker, which is what makes the size of a hit readable.
                // A lerp spends the same wall-clock time on both and never quite lands on the target.
                _ghostRatio = Mathf.Max(ratio, _ghostRatio - delta / _ghostDrainSeconds);
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
                ? Hue("bone_100_hit_flash")
                : Hue("ember_300_health_fill");
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
                Tint(_activeSinText, Bone300);
            }
            else
            {
                _activeSinText.Text = $"Active: {sin}";
                Tint(_activeSinText, GetSinColor(sin));
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
                Tint(_spiritStateText, Cold200);
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
                Tint(_poiseText, staggered ? Ember300 : Bone200);
            }

            SetFill(_poiseFill, _poise.CurrentPoise, _poise.MaxPoise);
            if (_poiseFill != null)
                // Brighter as well as red while staggered: the strip is the peripheral read, and the
                // stagger is the one poise state the player has to catch without looking at the numbers.
                _poiseFill.Color = staggered
                    ? Hue("ember_300_poise_staggered")
                    : Hue("bone_300_poise_fill");
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
            Tint(_flaskText, _player.HealCharges > 0 ? Bone200 : Bone300);
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
                SinState.Wrath => Hue("sin_wrath"),
                SinState.Sloth => Hue("sin_sloth"),
                SinState.Pride => Hue("sin_pride"),
                SinState.Gluttony => Hue("sin_gluttony"),
                SinState.Greed => Hue("sin_greed"),
                SinState.Envy => Hue("sin_envy"),
                SinState.Lust => Hue("sin_lust"),
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
            Tint(_warningText, Ember300);
            _warningExpiryUnscaled = GameClock.UnscaledTime + 2f;
        }

        private void ShowDeathWarning()
        {
            if (_warningText == null) return;
            _warningText.Text = "YOU DIED";
            Tint(_warningText, Ember300);
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
