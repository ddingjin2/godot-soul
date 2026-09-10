using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Fade, letterbox and dialogue line on their own layer above the HUD.
    /// </summary>
    /// <remarks>
    /// In Unity this deliberately held no logic at all: Timeline Animation Tracks drove the child alphas
    /// and bar heights by path name, so every child name here was part of the contract with an authored
    /// <c>.playable</c>. Timeline did not survive the port (see <see cref="CutsceneDirector"/>), so the
    /// channels those tracks wrote are now three plain setters - <see cref="SetFade"/>,
    /// <see cref="SetLetterbox"/> and <see cref="SetLine"/> - and the director's coded sequences call
    /// them. The node names are kept anyway: they are how the layout reads.
    /// <para>
    /// The letterbox heights and the line's rectangle are UI pixels, straight from the Unity canvas.
    /// They are not world distances and get no <c>World.U</c>.
    /// </para>
    /// </remarks>
    public sealed partial class CutsceneOverlay : CanvasLayer
    {
        public const string ObjectName = "CutsceneOverlay";

        // MoodDirection.md: INK_950 for the blackout surfaces, BONE_100 for text.
        private static readonly Color Ink950 = new(0x06 / 255f, 0x07 / 255f, 0x0A / 255f, 1f);
        private static readonly Color Bone100 = new(0xC3 / 255f, 0xBD / 255f, 0xB1 / 255f, 1f);

        private ColorRect _fade;
        private Control _letterboxTop;
        private Control _letterboxBottom;
        private Label _line;

        /// <summary>
        /// Built from a factory rather than <c>_Ready</c> so a headless test can construct one and drive
        /// it without a scene - the same reason the Unity version had a Create.
        /// </summary>
        public static CutsceneOverlay Create()
        {
            var overlay = new CutsceneOverlay
            {
                Name = ObjectName,

                // Above the HUD layer, which sits at the default 0. A cutscene fade that leaves the
                // health readout on top of it is not a fade.
                Layer = 100,
            };

            // The shot has to keep drawing through the pause menu and through the victory freeze.
            overlay.ProcessMode = Node.ProcessModeEnum.Always;

            // Unity's `new GameObject` dropped the canvas into the active scene for free, and the
            // bootstrap relies on that - it never parents the overlay itself.
            GameplayBuildShim.SceneRoot?.AddChild(overlay);

            overlay.Build();
            return overlay;
        }

        /// <summary>
        /// Puts the screen on black before anything animates it. <see cref="Build"/> leaves the fade at
        /// alpha 0, so a sequence that is the first thing to write it renders the arena lit for the
        /// frames before the first step runs and the fade-from-black reads as a flash. The entry
        /// cutscene calls this from bootstrap, which still runs ahead of the first rendered frame.
        /// </summary>
        public void SetBlackout(float letterboxHeight)
        {
            SetFade(1f);
            SetLetterbox(letterboxHeight);
        }

        /// <summary>The fade plane's opacity. Unity animated a CanvasGroup alpha on the "Fade" child.</summary>
        public void SetFade(float alpha)
        {
            if (_fade != null)
                _fade.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));
        }

        /// <summary>Both bars, same height in UI pixels. Unity animated <c>sizeDelta.y</c> on each.</summary>
        public void SetLetterbox(float height)
        {
            SetBarHeight(_letterboxTop, height, top: true);
            SetBarHeight(_letterboxBottom, height, top: false);
        }

        /// <summary>
        /// The caption under the letterbox. No shipped sequence writes it - none of the four
        /// <c>.playable</c> files carried a curve on the Line - but the line exists in the layout and a
        /// step that wants to speak has somewhere to speak from.
        /// </summary>
        public void SetLine(string text, float alpha = 1f)
        {
            if (_line == null)
                return;

            _line.Text = text ?? string.Empty;
            _line.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));
        }

        public void ResetToNeutral()
        {
            SetFade(0f);
            SetLine(string.Empty, 0f);
            SetLetterbox(0f);
        }

        private void Build()
        {
            _fade = new ColorRect { Name = "Fade", Color = Ink950 };
            _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);

            // Ignore everywhere, never Stop: the overlay must never swallow a click meant for the pause
            // or victory buttons underneath it. This is what Unity said by leaving off the
            // GraphicRaycaster.
            _fade.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(_fade);

            _letterboxTop = CreateBar("LetterboxTop", top: true);
            _letterboxBottom = CreateBar("LetterboxBottom", top: false);

            _line = new Label
            {
                Name = "Line",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };

            // The HUD's loader, not a second Resources.Load of the same .otf: it is the Korean-capable
            // face and it already falls back sensibly when the font is missing.
            _line.AddThemeFontOverride("font", MyGame.UI.GameplayHud.LoadUiFont());
            _line.AddThemeFontSizeOverride("font_size", 24);
            _line.AddThemeColorOverride("font_color", Bone100);

            // Bottom-centre, 900x80, sitting 90px off the bottom edge - the Unity anchoredPosition and
            // sizeDelta, unchanged.
            _line.AnchorLeft = 0.5f;
            _line.AnchorRight = 0.5f;
            _line.AnchorTop = 1f;
            _line.AnchorBottom = 1f;
            _line.OffsetLeft = -450f;
            _line.OffsetRight = 450f;
            _line.OffsetTop = -170f;
            _line.OffsetBottom = -90f;
            AddChild(_line);

            ResetToNeutral();
        }

        private Control CreateBar(string name, bool top)
        {
            var bar = new ColorRect { Name = name, Color = Ink950, MouseFilter = Control.MouseFilterEnum.Ignore };

            // Full width against a stretched anchor pair; the height is the animated channel.
            bar.AnchorLeft = 0f;
            bar.AnchorRight = 1f;
            bar.AnchorTop = top ? 0f : 1f;
            bar.AnchorBottom = top ? 0f : 1f;
            bar.OffsetLeft = 0f;
            bar.OffsetRight = 0f;
            AddChild(bar);
            return bar;
        }

        private static void SetBarHeight(Control bar, float height, bool top)
        {
            if (bar == null)
                return;

            // Anchored to one edge, so the bar grows inward from it: the top bar's bottom offset moves
            // down, the bottom bar's top offset moves up.
            if (top)
            {
                bar.OffsetTop = 0f;
                bar.OffsetBottom = height;
            }
            else
            {
                bar.OffsetTop = -height;
                bar.OffsetBottom = 0f;
            }
        }
    }
}
