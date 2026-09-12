using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Fade, letterbox and dialogue line on their own layer above the HUD.
    /// </summary>
    /// <remarks>
    /// In Unity this deliberately held no logic at all: the hierarchy was authored, and Timeline
    /// Animation Tracks drove the child alphas and bar heights <i>by path name</i>, so every child name
    /// here was part of the contract with an authored <c>.playable</c>. Timeline did not survive the port
    /// (see <see cref="CutsceneDirector"/>), so the channels those tracks wrote are now three plain
    /// setters - <see cref="SetFade"/>, <see cref="SetLetterbox"/> and <see cref="SetLine"/> - and the
    /// director's coded sequences call them.
    /// <para>
    /// The port also rebuilt the hierarchy itself in code, which the original never did.
    /// <c>Scenes/UI/CutsceneOverlay.tscn</c> restores the authored shape: the nodes, their anchors,
    /// offsets, colours and mouse filters, the layer number and the process mode all live in the scene,
    /// and what is left here is binding and the three animated channels - which is what a script is for.
    /// The node names are still the contract; they are how the layout reads and how <see cref="Bind"/>
    /// finds its children.
    /// </para>
    /// <para>
    /// The letterbox heights and the line's rectangle are UI pixels, straight from the Unity canvas.
    /// They are not world distances and get no <c>World.U</c>.
    /// </para>
    /// </remarks>
    public sealed partial class CutsceneOverlay : CanvasLayer
    {
        public const string ObjectName = "CutsceneOverlay";

        /// <summary>The authored hierarchy. Its root node is already named <see cref="ObjectName"/>.</summary>
        private const string ScenePath = "res://Scenes/UI/CutsceneOverlay.tscn";

        private ColorRect _fade;
        private Control _letterboxTop;
        private Control _letterboxBottom;
        private Label _line;

        /// <summary>
        /// Instantiates the packed scene rather than letting a caller <c>new</c> one, so a headless test
        /// can stand an overlay up and drive it without a scene of its own - the same reason the Unity
        /// version had a Create.
        /// </summary>
        public static CutsceneOverlay Create()
        {
            // Layer 100 (above the HUD's default 0 - a fade with the health readout on top of it is not a
            // fade) and ProcessMode Always (the shot keeps drawing through the pause menu and through the
            // victory freeze) are both authored in the scene.
            var overlay = GD.Load<PackedScene>(ScenePath).Instantiate<CutsceneOverlay>();

            // Unity's `new GameObject` dropped the canvas into the active scene for free, and the
            // bootstrap relies on that - it never parents the overlay itself.
            GameplayBuildShim.SceneRoot?.AddChild(overlay);

            // AddChild ran _Ready and bound already; this is for the headless case where SceneRoot is null
            // and nothing entered a tree. Binding twice is a re-resolve of the same four children.
            overlay.Bind();
            return overlay;
        }

        public override void _Ready()
        {
            Bind();
        }

        /// <summary>
        /// Puts the screen on black before anything animates it. The scene rests the fade at alpha 0, so a
        /// sequence that is the first thing to write it renders the arena lit for the frames before the
        /// first step runs and the fade-from-black reads as a flash. The entry cutscene calls this from
        /// bootstrap, which still runs ahead of the first rendered frame.
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

        /// <summary>
        /// Resolves the four authored children. It creates nothing - the scene owns the hierarchy - and it
        /// is idempotent, because <see cref="Create"/> calls it for the case where the overlay never
        /// entered a tree and so never got a <c>_Ready</c>.
        /// </summary>
        private void Bind()
        {
            _fade = GetNode<ColorRect>("Fade");
            _letterboxTop = GetNode<Control>("LetterboxTop");
            _letterboxBottom = GetNode<Control>("LetterboxBottom");
            _line = GetNode<Label>("Line");

            // The one property that stays a runtime binding. The scene could reference the .otf directly,
            // but an ext_resource pointing at a font Godot has not imported yet is a hard scene-load
            // failure - the whole overlay would fail to instantiate - whereas GameplayHud.LoadUiFont()
            // degrades to ThemeDB.FallbackFont and the caption still draws. That fallback is the HUD
            // loader's job and it should have exactly one owner, so the scene authors the caption's size
            // and colour and this authors its face. Not a second Resources.Load of the same .otf: it is
            // the Korean-capable face the rest of the UI already uses.
            _line.AddThemeFontOverride("font", MyGame.UI.GameplayHud.LoadUiFont());
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
