using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.UI;

namespace MyGame.Tests
{
    /// <summary>
    /// The graphics options panel the title screen carries.
    ///
    /// PORT, three deliberate differences and the reason for each:
    ///  1. Unity loaded <c>TitleScene</c> and looked for the canvas it built. Changing scenes here would
    ///     free the test runner with it (the runner *is* the current scene), so the screen is
    ///     instantiated instead. <c>Scenes/UI/TitleScreen.tscn</c> is the authored screen and
    ///     <c>Scenes/TitleScene.tscn</c>, the scene the game boots into, inherits it and adds nothing -
    ///     which is what lets this fixture instantiate the one without booting the other.
    ///  2. uGUI's <c>Toggle</c>/<c>Text</c>/<c>Button</c> are Godot's <c>CheckBox</c>/<c>Label</c>/
    ///     <c>Button</c>; the lit segment is the SegmentActive theme type variation rather than a
    ///     Text.color.
    ///  3. <c>GraphicsOptions.LoadAndApply</c> lost Unity's <c>[RuntimeInitializeOnLoadMethod]</c>, so
    ///     this fixture calls it itself - otherwise the statics would hold their compiled defaults
    ///     rather than what is in PlayerPrefs, and the restore in TearDown would write those defaults
    ///     over the player's saved settings.
    ///
    /// UNITS: nothing on this screen is a world distance. Every number here is a screen pixel and
    /// crosses unscaled, exactly as it did in uGUI.
    /// </summary>
    public sealed class TitleGraphicsOptionsTests
    {
        /// <summary>The authored screen. TitleScene.tscn inherits this and adds nothing.</summary>
        private const string TitleScenePath = "res://Scenes/UI/TitleScreen.tscn";

        private TitleMenuBootstrap _title;

        private bool _originalShake;
        private bool _originalHitStop;
        private bool _originalHitFlash;
        private int _originalMsaa;
        private float _originalRenderScale;
        private bool _originalVSync;
        private DisplayServer.VSyncMode _originalVSyncMode;

        /// <summary>
        /// Every value, not just the ones this test clicks: the preset buttons write all six, and
        /// GraphicsOptions persists them to PlayerPrefs, so anything left behind follows the machine into
        /// the next run. Restoring in TearDown rather than at the end of the test means a failed
        /// assertion cannot leak either.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            // Unity ran this off [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]. Nothing runs it here,
            // so the capture below would otherwise record the compiled defaults instead of the stored
            // settings and TearDown would overwrite them.
            GraphicsOptions.LoadAndApply();

            _originalShake = GraphicsOptions.ScreenShake;
            _originalHitStop = GraphicsOptions.HitStop;
            _originalHitFlash = GraphicsOptions.HitFlash;
            _originalMsaa = GraphicsOptions.Msaa;
            _originalRenderScale = GraphicsOptions.RenderScale;
            _originalVSync = GraphicsOptions.VSync;
            _originalVSyncMode = DisplayServer.WindowGetVsyncMode();
        }

        [TearDown]
        public void TearDown()
        {
            if (GodotObject.IsInstanceValid(_title))
                _title.Free();

            _title = null;

            GraphicsOptions.SetScreenShake(_originalShake);
            GraphicsOptions.SetHitStop(_originalHitStop);
            GraphicsOptions.SetHitFlash(_originalHitFlash);
            GraphicsOptions.SetMsaa(_originalMsaa);
            GraphicsOptions.SetRenderScale(_originalRenderScale);
            GraphicsOptions.SetVSync(_originalVSync);
            DisplayServer.WindowSetVsyncMode(_originalVSyncMode);
        }

        [Test]
        public async Task SettingsPanel_ShowsPresetsAndSwitchesToCustomOnSingleChange()
        {
            // Before the node enters the tree, not after: the first test of a run starts inside the
            // runner's own _Ready, and Godot refuses an add_child to a parent that is still propagating
            // ready.
            await TestContext.Runner.NextFrame();

            _title = GD.Load<PackedScene>(TitleScenePath).Instantiate<TitleMenuBootstrap>();
            TestContext.Tree.Root.AddChild(_title);
            await TestContext.Runner.NextFrame();

            var canvas = _title.GetNodeOrNull<CanvasLayer>("TitleCanvas");
            Assert.NotNull(canvas, "The title screen should carry the title canvas.");

            var root = canvas.GetNodeOrNull<Control>("TitleRoot");
            Assert.NotNull(root, "The title canvas should carry the menu root.");

            var panel = root.GetNodeOrNull<Control>("SettingsPanel");
            Assert.NotNull(panel, "The title canvas should carry the settings panel.");
            Assert.IsFalse(panel.Visible, "Settings panel should start hidden.");

            Click(root, "UI_TITLE_SETTINGS");
            Assert.IsTrue(panel.Visible, "The settings button should open the panel.");

            // The panel's static captions are authored as localisation keys and resolved by Godot's
            // auto-translation, so the property still reads back as the key. What is worth proving from
            // out here is that the key the scene names is one the table actually carries - Tr asserts it.
            Tr(panel.GetNode<Label>("SettingsColumn/Heading").Text);

            Click(panel, "UI_OPTION_PRESET_HIGH");
            Assert.AreEqual(PresetLine("UI_OPTION_PRESET_HIGH"), PresetLabel(panel), "High preset should be reported.");
            Assert.IsTrue(Box(panel, "UI_OPTION_SCREEN_SHAKE").ButtonPressed, "The High preset should tick the screen shake box.");
            AssertLit(panel, "UI_OPTION_MSAA", "4x");
            AssertLit(panel, "UI_OPTION_RENDER_SCALE", "100%");

            // Unticking the box rather than clicking a label: a two-value option is a checkbox now.
            // Writing ButtonPressed (rather than SetPressedNoSignal) is what fires the listener, which is
            // uGUI's isOn setter exactly.
            Box(panel, "UI_OPTION_SCREEN_SHAKE").ButtonPressed = false;
            Assert.AreEqual(PresetLine("UI_OPTION_PRESET_CUSTOM"), PresetLabel(panel), "Changing one option should report Custom.");
            Assert.IsFalse(GraphicsOptions.ScreenShake, "The toggle should reach the shared options.");

            Click(panel, "UI_OPTION_PRESET_LOW");
            Assert.AreEqual(PresetLine("UI_OPTION_PRESET_LOW"), PresetLabel(panel));

            // A preset writes every value and then refreshes the widgets. If that refresh went through
            // ButtonPressed instead of SetPressedNoSignal it would fire each box's Toggled back into the
            // refresh, and the preset it had just applied would read as Custom.
            Assert.IsFalse(Box(panel, "UI_OPTION_SCREEN_SHAKE").ButtonPressed, "The Low preset should untick the box.");
            Assert.AreEqual(PresetLine("UI_OPTION_PRESET_LOW"), PresetLabel(panel), "Refreshing the boxes must not write back through their listeners.");
            AssertLit(panel, "UI_OPTION_MSAA", Tr("UI_OPTION_STEP_OFF"));

            // PORT: in Unity this row also proved the renderer was resized. Godot's render-scale
            // equivalent is 3D-only, so GraphicsOptions stores, clamps, cycles and saves the value but
            // never reaches the renderer - the assertion is therefore about the stored value the row
            // lights, which is all that is left to be wrong.
            AssertLit(panel, "UI_OPTION_RENDER_SCALE", "60%");
            Assert.AreEqual(0.6f, GraphicsOptions.RenderScale, 0.0001f,
                "The Low preset should store 60% render scale, even though nothing downstream reads it in 2D.");

            // One click reaches any value; the cycling button this replaced needed three to get here.
            ClickSegment(Row(panel, "UI_OPTION_MSAA"), "8x");
            Assert.AreEqual(8, GraphicsOptions.Msaa, "A segment should write its own value, not the next one.");
            AssertLit(panel, "UI_OPTION_MSAA", "8x");

            // uGUI's Canvas.ForceUpdateCanvases plus LayoutUtility.GetPreferredHeight: a Godot container
            // reports the same thing as its combined minimum size, and one frame is enough for the panel
            // to have taken its own.
            await TestContext.Runner.NextFrame();
            var column = panel.GetNodeOrNull<Control>("SettingsColumn");
            Assert.NotNull(column, "The panel should hold its padded column.");
            Assert.LessOrEqual(
                column.GetCombinedMinimumSize().Y,
                panel.Size.Y,
                "Every settings row should fit inside the panel instead of overflowing it.");

            Click(panel, "UI_COMMON_CLOSE");
            Assert.IsFalse(panel.Visible, "The close button should hide the panel.");
        }

        /// <summary>
        /// An authored button, addressed the way the screen names it: the localisation key its caption is
        /// written from, plus the role. Not by the caption - the screen's captions are keys resolved at
        /// draw time, and a node named after the translated text would be renamed by a change of locale.
        /// </summary>
        private static void Click(Node root, string key)
        {
            Button button = Descendants(root).OfType<Button>().FirstOrDefault(candidate => candidate.Name == key + "Button");
            Assert.NotNull(button, "Expected a button named: " + key + "Button");
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        /// <summary>
        /// One value of a segmented row, by its caption. These are the one set of captions the screen
        /// writes rather than authors - 4x, 100%, the translated off - because they follow
        /// GraphicsOptions' step arrays, so the text is what identifies them.
        /// </summary>
        private static void ClickSegment(Node row, string value)
        {
            Button button = Descendants(row).OfType<Button>().FirstOrDefault(candidate => candidate.Text == value);
            Assert.NotNull(button, "Expected a segment labelled: " + value);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        /// <summary>
        /// The checkbox for a two-value option, by the same key-plus-role name as everything else on this
        /// screen. uGUI needed a Toggle plus a sibling caption; Godot's CheckBox carries the caption
        /// itself, which is why there is no second node to find.
        /// </summary>
        private static CheckBox Box(Node panel, string key)
        {
            CheckBox toggle = Descendants(panel).OfType<CheckBox>().FirstOrDefault(candidate => candidate.Name == key + "Toggle");
            Assert.NotNull(toggle, "Expected a checkbox named: " + key + "Toggle");
            return toggle;
        }

        /// <summary>
        /// <paramref name="optionKey"/> is the localisation key, because that is what the row is named -
        /// a node named after the translated caption would be renamed by a change of locale. Same rule as
        /// <see cref="Click"/> and <see cref="Box"/>.
        /// </summary>
        private static Control Row(Node panel, string optionKey)
        {
            Control row = Descendants(panel).OfType<Control>().FirstOrDefault(node => node.Name == optionKey + "Row");
            Assert.NotNull(row, "Expected a segmented row for: " + optionKey);
            return row;
        }

        /// <summary>
        /// The chosen segment is the one drawn in the bright bone text colour, the rest in the dim one.
        /// Asserting on exactly one lit segment also catches a row that lights all of them or none.
        /// A Godot button carries that colour as a theme override rather than on a child Text.
        /// </summary>
        private static void AssertLit(Node panel, string optionKey, string value)
        {
            string[] lit = Descendants(Row(panel, optionKey)).OfType<Button>()
                .Where(button => button.GetThemeColor("font_color").R > 0.7f)
                .Select(button => button.Text)
                .ToArray();

            Assert.AreEqual(1, lit.Length, "Expected exactly one lit segment on " + optionKey + ": " + value);
            Assert.AreEqual(value, lit[0], "Expected the lit segment on " + optionKey + " to be " + value);
        }

        /// <summary>
        /// The localisation table, not a copy of it: the screen reads these labels through the same keys,
        /// so re-wording one in <c>localization/ui.csv</c> moves the test with it. The guard is what keeps
        /// that from weakening the assertion - an unimported table makes Translate hand back the key, and
        /// both sides of every comparison would then be that key.
        /// </summary>
        private static string Tr(string key)
        {
            string text = TranslationServer.Translate(key);
            Assert.AreNotEqual(key, text, "localization/ui.csv should carry a translation for " + key);
            return text;
        }

        /// <summary>The whole preset line as the panel should read it, for the given preset-name key.</summary>
        private static string PresetLine(string presetKey) =>
            string.Format(Tr("UI_OPTION_PRESET_VALUE"), Tr(presetKey));

        /// <summary>
        /// The preset line on screen. Found by the fixed part of its own format string rather than by a
        /// hard-coded prefix, so the row is still located after the wording is edited in the table.
        /// </summary>
        private static string PresetLabel(Node panel)
        {
            string prefix = Tr("UI_OPTION_PRESET_VALUE").Split("{0}")[0];
            Label text = Descendants(panel).OfType<Label>().FirstOrDefault(candidate => candidate.Text.StartsWith(prefix));
            Assert.NotNull(text, "Expected a label starting with: " + prefix);
            return text.Text;
        }

        /// <summary>
        /// Unity's <c>GetComponentsInChildren&lt;T&gt;(true)</c>. A component is a node here, so the
        /// same sweep is the subtree - hidden nodes included, which is the whole point while the panel
        /// starts closed.
        /// </summary>
        private static IEnumerable<Node> Descendants(Node root)
        {
            foreach (Node child in root.GetChildren())
            {
                yield return child;

                foreach (Node deeper in Descendants(child))
                    yield return deeper;
            }
        }
    }
}
