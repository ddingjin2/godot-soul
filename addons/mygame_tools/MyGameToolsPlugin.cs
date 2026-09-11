#if TOOLS
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// The Godot home of what used to be <c>Assets/_Project/Scripts/Editor</c>.
    ///
    /// Unity hung each tool off a <c>[MenuItem("MyGame/...")]</c> attribute, which Godot has no
    /// equivalent of: an EditorPlugin gets to add UI, not menu entries, so every menu item became a
    /// button in one dock. The grouping is the old menu's - Design, Art, Animations, Scenes, Tests.
    /// </summary>
    [Tool]
    public partial class MyGameToolsPlugin : EditorPlugin
    {
        private Control _dock;

        public override void _EnterTree()
        {
            var root = new VBoxContainer { Name = "MyGameTools" };
            root.AddThemeConstantOverride("separation", 4);

            Heading(root, "Design");
            Button(root, "1. Export JSON To Spreadsheet", DesignSpreadsheetTool.ExportToSpreadsheet);
            Button(root, "2. Import Spreadsheet To JSON", DesignSpreadsheetTool.ImportFromSpreadsheet);
            Button(root, "Validate + Normalise Design JSON", GameplayTuningJsonValidator.Run);

            Heading(root, "Art");
            Button(root, "Bake Generated Sprites", GameplaySpriteBaker.BakeAll);
            Button(root, "Fix Pixel Art Import Settings", PixelActorTextureImportSettings.Run);

            Heading(root, "Animations");
            Button(root, "Rebuild Player Attack Animations", PlayerAttackAnimationAssetCreator.Rebuild);

            Heading(root, "Scenes");
            Button(root, "Create Missing Chapter Scenes", ChapterSceneCreator.CreateMissing);
            ProjectSceneSelector.BuildSceneList(root);

            Heading(root, "Tests");
            Button(root, "Run Headless Test Suite", TestRunLauncher.Run);

            _dock = root;

            // AddControlToDock/RemoveControlFromDocks are deprecated in 4.7 in favour of AddDock(EditorDock),
            // which wants the dock's contents wrapped in an EditorDock resource. A VBox of buttons does not
            // need one; the warning is silenced rather than the shape changed.
#pragma warning disable CS0618
            AddControlToDock(DockSlot.LeftUr, _dock);
#pragma warning restore CS0618
        }

        public override void _ExitTree()
        {
            if (_dock == null)
            {
                return;
            }

#pragma warning disable CS0618
            RemoveControlFromDocks(_dock);
#pragma warning restore CS0618
            _dock.QueueFree();
            _dock = null;
        }

        private static void Heading(Container parent, string text)
        {
            if (parent.GetChildCount() > 0)
            {
                parent.AddChild(new HSeparator());
            }

            var label = new Label { Text = text };
            label.AddThemeColorOverride("font_color", new Color(0.6f, 0.75f, 1f));
            parent.AddChild(label);
        }

        private static void Button(Container parent, string text, System.Action action)
        {
            var button = new Button { Text = text, TooltipText = text };
            button.Pressed += action;
            parent.AddChild(button);
        }
    }
}
#endif
