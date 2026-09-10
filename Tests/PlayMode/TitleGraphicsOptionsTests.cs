using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.UI;

namespace MyGame.Tests
{
    /// <summary>
    /// The graphics options panel the title screen builds.
    ///
    /// PORT, three deliberate differences and the reason for each:
    ///  1. Unity loaded <c>TitleScene</c> and looked for the canvas it built. <c>Scenes/TitleScene.tscn</c>
    ///     is a shell whose only content is <see cref="TitleMenuBootstrap"/>, and changing scenes here
    ///     would free the test runner with it (the runner *is* the current scene), so the bootstrap is
    ///     added to the tree directly. It builds the identical canvas in its <c>_Ready</c>.
    ///  2. uGUI's <c>Toggle</c>/<c>Text</c>/<c>Button</c> are Godot's <c>CheckBox</c>/<c>Label</c>/
    ///     <c>Button</c>; the lit segment is a theme colour override rather than a Text.color.
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
            // Before the node is built, not after: the first test of a run starts inside the runner's
            // own _Ready, and Godot refuses an add_child to a parent that is still propagating ready.
            await TestContext.Runner.NextFrame();

            _title = new TitleMenuBootstrap { Name = "TitleMenuBootstrap" };
            TestContext.Tree.Root.AddChild(_title);
            await TestContext.Runner.NextFrame();

            var canvas = _title.GetNodeOrNull<CanvasLayer>("TitleCanvas");
            Assert.NotNull(canvas, "Title scene should build the title canvas.");

            var root = canvas.GetNodeOrNull<Control>("TitleRoot");
            Assert.NotNull(root, "Title canvas should build the menu root.");

            var panel = root.GetNodeOrNull<Control>("SettingsPanel");
            Assert.NotNull(panel, "Title canvas should build the settings panel.");
            Assert.IsFalse(panel.Visible, "Settings panel should start hidden.");

            Click(root, "설정");
            Assert.IsTrue(panel.Visible, "The settings button should open the panel.");

            Click(panel, "상 (High)");
            Assert.AreEqual("프리셋: 상", LabelStartingWith(panel, "프리셋:"), "High preset should be reported.");
            Assert.IsTrue(Box(panel, "화면 흔들림").ButtonPressed, "The High preset should tick the screen shake box.");
            AssertLit(panel, "안티에일리어싱", "4x");
            AssertLit(panel, "렌더 스케일", "100%");

            // Unticking the box rather than clicking a label: a two-value option is a checkbox now.
            // Writing ButtonPressed (rather than SetPressedNoSignal) is what fires the listener, which is
            // uGUI's isOn setter exactly.
            Box(panel, "화면 흔들림").ButtonPressed = false;
            Assert.AreEqual("프리셋: 커스텀", LabelStartingWith(panel, "프리셋:"), "Changing one option should report Custom.");
            Assert.IsFalse(GraphicsOptions.ScreenShake, "The toggle should reach the shared options.");

            Click(panel, "하 (Low)");
            Assert.AreEqual("프리셋: 하", LabelStartingWith(panel, "프리셋:"));

            // A preset writes every value and then refreshes the widgets. If that refresh went through
            // ButtonPressed instead of SetPressedNoSignal it would fire each box's Toggled back into the
            // refresh, and the preset it had just applied would read as Custom.
            Assert.IsFalse(Box(panel, "화면 흔들림").ButtonPressed, "The Low preset should untick the box.");
            Assert.AreEqual("프리셋: 하", LabelStartingWith(panel, "프리셋:"), "Refreshing the boxes must not write back through their listeners.");
            AssertLit(panel, "안티에일리어싱", "끔");

            // PORT: in Unity this row also proved the renderer was resized. Godot's render-scale
            // equivalent is 3D-only, so GraphicsOptions stores, clamps, cycles and saves the value but
            // never reaches the renderer - the assertion is therefore about the stored value the row
            // lights, which is all that is left to be wrong.
            AssertLit(panel, "렌더 스케일", "60%");
            Assert.AreEqual(0.6f, GraphicsOptions.RenderScale, 0.0001f,
                "The Low preset should store 60% render scale, even though nothing downstream reads it in 2D.");

            // One click reaches any value; the cycling button this replaced needed three to get here.
            Click(Row(panel, "안티에일리어싱"), "8x");
            Assert.AreEqual(8, GraphicsOptions.Msaa, "A segment should write its own value, not the next one.");
            AssertLit(panel, "안티에일리어싱", "8x");

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

            Click(panel, "닫기");
            Assert.IsFalse(panel.Visible, "The close button should hide the panel.");
        }

        private static void Click(Node root, string label)
        {
            Button button = Descendants(root).OfType<Button>().FirstOrDefault(candidate => candidate.Text == label);
            Assert.NotNull(button, "Expected a button labelled: " + label);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        /// <summary>
        /// The checkbox for a two-value option. uGUI needed a Toggle plus a sibling caption; Godot's
        /// CheckBox carries the caption itself, so the label is the widget's own text.
        /// </summary>
        private static CheckBox Box(Node panel, string label)
        {
            CheckBox toggle = Descendants(panel).OfType<CheckBox>().FirstOrDefault(candidate => candidate.Text == label);
            Assert.NotNull(toggle, "Expected a checkbox labelled: " + label);
            return toggle;
        }

        private static Control Row(Node panel, string option)
        {
            Control row = Descendants(panel).OfType<Control>().FirstOrDefault(node => node.Name == option + "Row");
            Assert.NotNull(row, "Expected a segmented row for: " + option);
            return row;
        }

        /// <summary>
        /// The chosen segment is the one drawn in the bright bone text colour, the rest in the dim one.
        /// Asserting on exactly one lit segment also catches a row that lights all of them or none.
        /// A Godot button carries that colour as a theme override rather than on a child Text.
        /// </summary>
        private static void AssertLit(Node panel, string option, string value)
        {
            string[] lit = Descendants(Row(panel, option)).OfType<Button>()
                .Where(button => button.GetThemeColor("font_color").R > 0.7f)
                .Select(button => button.Text)
                .ToArray();

            Assert.AreEqual(1, lit.Length, "Expected exactly one lit segment on " + option + ": " + value);
            Assert.AreEqual(value, lit[0], "Expected the lit segment on " + option + " to be " + value);
        }

        private static string LabelStartingWith(Node panel, string prefix)
        {
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
