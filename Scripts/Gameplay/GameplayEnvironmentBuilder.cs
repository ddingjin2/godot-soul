using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// What the arena hands back to the bootstrap. Unity's <c>Transform</c> is a
    /// <see cref="Node2D"/> and its <c>GameObject</c> is a <see cref="Node"/>.
    /// </summary>
    public readonly struct GameplayEnvironment
    {
        public GameplayEnvironment(Node2D checkpoint, Node2D spiritPlatform)
        {
            Checkpoint = checkpoint;
            SpiritPlatform = spiritPlatform;
        }

        public Node2D Checkpoint { get; }
        public Node2D SpiritPlatform { get; }
    }

    /// <summary>
    /// Builds the whole arena at runtime. The scenes in this project are empty shells; this is where
    /// the ground, the platforms, the backdrop, the gates and the bonfires actually come from.
    ///
    /// UNITS: everything read off <see cref="GameplaySceneDefaults"/> and
    /// <see cref="GameplayReadabilityDefaults"/> is already in Godot pixels with +Y down - those two
    /// classes are the conversion boundary, as is <see cref="WorldTuningData"/> for the world-edge slab
    /// and the gate portal offset. The fallbacks those keep here, for a run with no
    /// <c>WorldTuning.json</c>, are the only metres left in this file and are wrapped in
    /// <see cref="World.U"/> where they stand.
    ///
    /// Solid geometry is an instance of <c>Scenes/World/SolidBox.tscn</c> - a
    /// <see cref="StaticBody2D"/> on <see cref="World.Layer.Ground"/> carrying a
    /// <see cref="CollisionShape2D"/> and a <see cref="Sprite2D"/> - and decoration is a bare Sprite2D
    /// still built here, which is exactly the split Unity had between "collider plus renderer" and
    /// "renderer only". A one-node scene whose only content is a Sprite2D would be a file and a load
    /// for no structure, so <see cref="CreateSceneryPiece"/> stays code on purpose.
    ///
    /// This file no longer builds nodes for the pieces that have a scene: it loads them, binds the
    /// designer-owned numbers, and places them. The names it writes are the ones the rest of the
    /// project reaches for - a platform takes the layout's own <c>platform.Name</c>, not the scene
    /// root's - so the instance is renamed before it enters the tree.
    /// </summary>
    public static class GameplayEnvironmentBuilder
    {
        /// <summary>World-space text, shared with <see cref="GameplayEnemySpawner"/>'s role markers.</summary>
        private const string WorldLabelScenePath = "res://Scenes/World/WorldLabel.tscn";

        /// <summary>Every piece of static collision in the arena: floor, platforms, edges, spirit platform.</summary>
        private const string SolidBoxScenePath = "res://Scenes/World/SolidBox.tscn";

        /// <summary>The same box with the gate's lever hung on it - an inherited scene.</summary>
        private const string ShortcutGateScenePath = "res://Scenes/World/ShortcutGate.tscn";

        private const string CheckpointScenePath = "res://Scenes/World/Checkpoint.tscn";
        private const string GatePortalScenePath = "res://Scenes/World/GatePortal.tscn";

        /// <summary>The two children <c>SolidBox.tscn</c> authors. Reached by name, as the scene guarantees them.</summary>
        private const string SolidBoxShapeName = "Shape";

        private const string SolidBoxSpriteName = "Sprite";

        /// <summary>
        /// Builds the arena and hands back the checkpoint the player belongs at. On a resumed slot that
        /// is the bonfire last rested at rather than the chapter's first, which is the whole point of
        /// recording one.
        /// </summary>
        public static GameplayEnvironment Build(GameplaySceneDefaults scene, GameplayReadabilityDefaults readability)
        {
            WarnIfTheCameraCannotFollowTheLevel(scene);

            CreateBackdrop(scene, readability);
            CreateArenaSignage(scene, readability);
            CreateGroundFloor(scene, readability);
            CreatePlatforms(scene, readability);

            string sceneName = GameplayBuildShim.ActiveSceneName;
            int resumeIndex = GameplaySaveBridge.ResumeCheckpointIndex(sceneName);

            Node2D checkpoint = CreateCheckpoints(scene, resumeIndex);

            // The player spawner reads PlayerSpawnPosition next, and it is the only thing that does.
            // Resuming means standing at the bonfire the run stopped at, and a body cannot be moved
            // there after it spawns without sweeping the arena on the way - so the position has to be
            // right before anything is instantiated, which makes this the seam.
            if (resumeIndex >= 0 && checkpoint != null)
            {
                scene.PlayerSpawnPosition = checkpoint.GlobalPosition;

                // The opening frame moves with it. The camera starts at the arena's fixed establishing
                // shot and the follow rig only closes twelve units a second, so a resume 250 units into
                // a long level would spend twenty seconds flying up the map before the player could see
                // themselves. Clamped, because the follow rig clamps too and starting outside the bounds
                // only buys a snap on the first frame.
                scene.CameraPosition = new Vector2(
                    Mathf.Clamp(checkpoint.GlobalPosition.X, scene.CameraHorizontalBounds.X, scene.CameraHorizontalBounds.Y),
                    scene.CameraPosition.Y);
            }

            CreateGatePortal(checkpoint);
            CreateShortcutGate(scene, readability, GameplaySaveBridge.ResumeShortcutOpened(sceneName));

            Node2D spiritPlatform = CreateSpiritPlatform(scene, readability);
            return new GameplayEnvironment(checkpoint, spiritPlatform);
        }

        /// <summary>
        /// Nothing derives the camera bounds from the ground - they are two hand-authored numbers in the
        /// same file - so a level that grows past them leaves the player walking off the side of a
        /// stationary screen with nothing anywhere saying why. Everything here was built and tested at 36
        /// units; this is the one thing that gets longer and fails in silence.
        /// </summary>
        /// <remarks>
        /// ponytail: a screen-width heuristic rather than a real frustum test, because the aspect ratio is
        /// not known here and a headless run's is not the player's. It catches a level that outgrew its
        /// bounds, not a bound that is a metre short. Swap it for a viewport check if a metre ever matters.
        /// The message is printed back in Unity units, because the file the reader has to edit is in them.
        /// </remarks>
        private static void WarnIfTheCameraCannotFollowTheLevel(GameplaySceneDefaults scene)
        {
            float groundLeft = scene.GroundPosition.X - scene.GroundSize.X * 0.5f;
            float groundRight = scene.GroundPosition.X + scene.GroundSize.X * 0.5f;

            // CameraOrthographicSize is the one field on the defaults still in Unity units - it becomes a
            // Camera2D zoom rather than a distance - so it is the one number here that needs scaling.
            float oneScreen = World.U(scene.CameraOrthographicSize) * 2f;

            float unreachableRight = groundRight - scene.CameraHorizontalBounds.Y;
            float unreachableLeft = scene.CameraHorizontalBounds.X - groundLeft;
            if (unreachableRight <= oneScreen && unreachableLeft <= oneScreen)
                return;

            GD.PushWarning(
                $"GameplayEnvironmentBuilder: the ground runs {World.ToUnits(groundLeft):0.#} to {World.ToUnits(groundRight):0.#} but the camera is bounded to " +
                $"{World.ToUnits(scene.CameraHorizontalBounds.X):0.#} to {World.ToUnits(scene.CameraHorizontalBounds.Y):0.#}. The player will walk off the edge of a " +
                "screen that has stopped following them. Widen cameraHorizontalBounds in this scene's SceneLayout file.");
        }

        private static void CreateBackdrop(GameplaySceneDefaults scene, GameplayReadabilityDefaults readability)
        {
            // White base: the sprite's modulate already carries BackgroundColor, and a grey base tints it twice.
            CreateSceneryPiece(
                "Backdrop",
                scene.BackdropPosition,
                GameplayVisualFactory.CreateSquareSprite(Colors.White),
                scene.BackdropSize,
                readability.BackgroundColor,
                readability.BackdropSortingOrder);

            foreach (GameplaySceneDefaults.SceneryDefinition scenery in scene.BackdropScenery)
            {
                Color color = GetSceneryColor(scenery.Name, readability);
                int sortingOrder = GetScenerySortingOrder(scenery.Name, readability);
                CreateSceneryPiece(
                    scenery.Name,
                    scenery.Position,
                    GameplayVisualFactory.CreateSprite(scenery.SpriteKind),
                    scenery.Size,
                    color,
                    sortingOrder,
                    GameplayVisualFactory.Pivot(scenery.SpriteKind));
            }
        }

        private static void CreateArenaSignage(GameplaySceneDefaults scene, GameplayReadabilityDefaults readability)
        {
            CreateWorldLabel("CHECKPOINT", scene.CheckpointLabelPosition, readability.CheckpointLabelColor, readability);
            CreateWorldLabel("DUEL FLOOR", scene.DuelFloorLabelPosition, readability.DuelFloorLabelColor, readability);
            CreateWorldLabel("WRATH ALTAR", scene.WrathAltarLabelPosition, readability.WrathAltarLabelColor, readability);
        }

        private static void CreateGroundFloor(GameplaySceneDefaults scene, GameplayReadabilityDefaults readability)
        {
            CreateSolidBox("Ground", scene.GroundPosition, scene.GroundSize, readability.GroundColor, readability.GroundSortingOrder);

            CreateSceneryPiece("GroundRim", scene.GroundRimPosition, GameplayVisualFactory.CreateSquareSprite(Colors.White), scene.GroundRimSize, readability.GroundRimColor, readability.GroundRimSortingOrder);
            CreateSceneryPiece("LeftArenaGate", scene.LeftArenaGatePosition, GameplayVisualFactory.CreateSquareSprite(Colors.White), scene.ArenaGateSize, readability.ArenaGateColor, readability.GateSortingOrder);
            CreateSceneryPiece("RightArenaGate", scene.RightArenaGatePosition, GameplayVisualFactory.CreateSquareSprite(Colors.White), scene.ArenaGateSize, readability.ArenaGateColor, readability.GateSortingOrder);

            CreateWorldEdge(scene, "LeftWorldEdge", -1);
            CreateWorldEdge(scene, "RightWorldEdge", 1);
        }

        /// <summary>
        /// A wall at each end of the floor, so walking off the side of the level is not a death.
        /// The arena gates look like they already do this and do not: they are built by
        /// <see cref="CreateSceneryPiece"/>, which is a sprite and nothing else, and every layout
        /// parks them just outside the ground anyway. Giving them colliders instead would put the wall
        /// wherever eight design files happen to have placed a decoration; derived from the floor, it is
        /// in the right place in a level nobody has authored yet.
        /// Tall rather than exact: the climb chains reach 9 units in chapter eight, and a wall a player
        /// can jump over at the top of a tower is the same bug one screen higher.
        /// </summary>
        private static void CreateWorldEdge(GameplaySceneDefaults scene, string name, int side)
        {
            // WorldTuning.json's, already pixels. No fallback: an arena missing its design file gets no
            // walls and an error saying so, rather than walls nobody authored (PLAN_CLOSEOUT D1).
            WorldTuningData world = GameplayTuningCatalog.Load()?.WorldTuning;
            if (world == null)
            {
                GD.PushError($"GameplayEnvironmentBuilder: Design/WorldTuning.json is missing; '{name}' is not built.");
                return;
            }

            float thicknessPx = world.worldEdgeWallThickness;
            float heightPx = world.worldEdgeWallHeight;

            // Half the wall's height *above* the floor, and up is -Y here, so this subtracts.
            var position = new Vector2(
                scene.GroundPosition.X + side * (scene.GroundSize.X * 0.5f + thicknessPx * 0.5f),
                scene.GroundPosition.Y - heightPx * 0.5f);

            // The same SolidBox.tscn the floor and the platforms use, with the renderer switched off:
            // a world edge is a boundary rather than a thing to look at, and Unity's version carried no
            // SpriteRenderer either. Hidden rather than freed, because the scene owns the tree.
            StaticBody2D edge = CreateSolidBox(name, position, new Vector2(thicknessPx, heightPx), Colors.White, 0);
            edge.GetNode<Sprite2D>(SolidBoxSpriteName).Visible = false;
        }

        private static void CreatePlatforms(GameplaySceneDefaults scene, GameplayReadabilityDefaults readability)
        {
            foreach (GameplaySceneDefaults.PlatformDefinition platform in scene.Platforms)
                CreatePlatform(platform, readability);
        }

        private static void CreatePlatform(GameplaySceneDefaults.PlatformDefinition platform, GameplayReadabilityDefaults readability)
        {
            CreateSolidBox(platform.Name, platform.Position, platform.Size, readability.PlatformColor, readability.PlatformSortingOrder);

            // A fraction of the platform's height above its centre; up is -Y. Both numbers are the
            // designer's, from ReadabilityLayout.json, and the thickness is already pixels.
            var rimPosition = new Vector2(platform.Position.X, platform.Position.Y - platform.Size.Y * readability.PlatformRimHeightFraction);
            var rimSize = new Vector2(platform.Size.X, readability.PlatformRimThickness);
            CreateSceneryPiece(platform.Name + "Rim", rimPosition, GameplayVisualFactory.CreateSquareSprite(Colors.White), rimSize, readability.PlatformRimColor, readability.PlatformRimSortingOrder);
        }

        /// <summary>
        /// The portal that walks the road backwards, placed beside the checkpoint. Every arena is entered
        /// at its checkpoint, so that is the one spot in every chapter a player is guaranteed to stand in
        /// - and it is the same corner they already look at when they want to leave.
        /// </summary>
        /// <remarks>
        /// Offset rather than co-located: two triggers on one point would both answer the same Interact,
        /// and resting would open the travel panel over the bonfire's own feedback.
        /// </remarks>
        private static void CreateGatePortal(Node2D checkpoint)
        {
            var go = GD.Load<PackedScene>(GatePortalScenePath).Instantiate<Node2D>();

            // Positioned before it enters the tree, as GameplayBuildShim.NewObject did. The scene
            // already names the root "GatePortal", so nothing is renamed here.
            // WorldTuning.json's, already pixels. Missing means the portal lands on the bonfire and an
            // error says why, rather than an offset nobody authored (PLAN_CLOSEOUT D1).
            WorldTuningData world = GameplayTuningCatalog.Load()?.WorldTuning;
            if (world == null)
                GD.PushError("GameplayEnvironmentBuilder: Design/WorldTuning.json is missing; the gate portal has no offset from its bonfire.");

            float portalOffsetX = world != null ? world.gatePortalOffsetX : 0f;
            go.Position = checkpoint.GlobalPosition + new Vector2(portalOffsetX, 0f);

            // Bound while the instance is still detached: the marker's pulse caches the colour it finds
            // at _Ready and writes it back every frame, so a tint applied afterwards is invisible.
            go.GetNode<GateTravelZone>("GateTravelZone").BindAuthoredTriggerAndMarker();

            GameplayBuildShim.SceneRoot?.AddChild(go);
        }

        /// <summary>
        /// Every bonfire the chapter authored, numbered in the order they are met, and the one the player
        /// starts at handed back. An index that names no checkpoint - an older slot, a chapter that lost
        /// a bonfire since it was written - falls back to the first rather than to nothing.
        /// </summary>
        private static Node2D CreateCheckpoints(GameplaySceneDefaults scene, int activeIndex)
        {
            Vector2[] positions = scene.CheckpointPositions;
            if (positions == null || positions.Length == 0)
                return CreateCheckpoint(scene);

            var checkpoints = new Node2D[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                checkpoints[i] = CreateCheckpointAt(positions[i], i);

            int index = activeIndex >= 0 && activeIndex < checkpoints.Length ? activeIndex : 0;
            return checkpoints[index];
        }

        /// <summary>
        /// The single-checkpoint arena, which is what every chapter shipped before there were several.
        /// </summary>
        /// <remarks>
        /// Kept under this exact name and signature because the P0 runner reflects on it by name with one
        /// <see cref="GameplaySceneDefaults"/> parameter, and a plain lookup would throw on an overload.
        /// Renaming it breaks a test the compiler cannot show you.
        /// </remarks>
        private static Node2D CreateCheckpoint(GameplaySceneDefaults scene)
        {
            return CreateCheckpointAt(scene.CheckpointPosition, 0);
        }

        /// <summary>
        /// One bonfire: the respawn marker the death loop reads, and the zone the player rests at. Both
        /// on one object, because a checkpoint you cannot sit at is a respawn coordinate and the level
        /// needs somewhere to stop.
        /// </summary>
        private static Node2D CreateCheckpointAt(Vector2 position, int index)
        {
            var go = GD.Load<PackedScene>(CheckpointScenePath).Instantiate<Node2D>();

            // Name and position before it enters the tree, as GameplayBuildShim.NewObject did. The
            // scene root ships as "Checkpoint"; only the numbered ones are renamed.
            go.Name = index == 0 ? "Checkpoint" : $"Checkpoint{index}";
            go.Position = position;

            go.GetNode<Checkpoint>("Checkpoint").Configure(index == 0 ? "Gameplay-start" : $"Gameplay-checkpoint-{index}", Vector2.Zero);

            // Inert until CheckpointZone.InitializeAll wires it, which the bootstrap does once the player
            // and the respawner exist. The bind below is not that wiring: it is the designer-owned trigger
            // radius and marker colour, which have to land before the marker's pulse readies and caches
            // the colour it will write back every frame.
            CheckpointZone zone = go.GetNode<CheckpointZone>("CheckpointZone");
            zone.SetCheckpointIndex(index);
            zone.BindAuthoredTriggerAndMarker();

            GameplayBuildShim.SceneRoot?.AddChild(go);
            return go;
        }

        /// <summary>
        /// The one-way door, when the chapter authors one. Built from the arena gate's own body and
        /// colour - no new art - and put on the Ground layer so it is a wall until it is opened.
        /// </summary>
        private static void CreateShortcutGate(GameplaySceneDefaults scene, GameplayReadabilityDefaults readability, bool startsOpen)
        {
            if (!scene.HasShortcutGate)
                return;

            StaticBody2D go = CreateSolidBox(
                "ShortcutGate", scene.ShortcutGatePosition, scene.ShortcutGateSize, readability.ArenaGateColor, readability.GateSortingOrder,
                ShortcutGateScenePath);

            // Authored as a child of the door body by Scenes/World/ShortcutGate.tscn, under the name
            // AddComponent gave it - the gate's log line reads off it. Told which way it opens only
            // after the body and the sprite exist: the gate caches both, and SetOpen writes them.
            ShortcutGate gate = go.GetNode<ShortcutGate>("ShortcutGate");
            gate.SetOpensFromRight(scene.ShortcutOpensFromRight);
            gate.SetOpen(startsOpen);
        }

        private static Node2D CreateSpiritPlatform(GameplaySceneDefaults scene, GameplayReadabilityDefaults readability)
        {
            StaticBody2D go = CreateSolidBox(
                "SpiritPlatform", scene.SpiritPlatformPosition, scene.SpiritPlatformSize, readability.SpiritPlatformColor, readability.SpiritPlatformSortingOrder);

            // Unity's SetActive: hidden, not ticking, and - the part that matters for a platform - not
            // colliding either.
            go.SetActive(scene.SpiritPlatformStartsActive);
            return go;
        }

        /// <summary>
        /// One instance of <c>Scenes/World/SolidBox.tscn</c>: a <see cref="StaticBody2D"/> on
        /// <see cref="World.Layer.Ground"/> carrying one rectangle collider and one stretched white
        /// sprite tinted to the wanted colour. Unity did the same thing with a BoxCollider2D, a sliced
        /// SpriteRenderer and a static Rigidbody2D on one GameObject.
        ///
        /// <paramref name="name"/> is the layout's own name for the piece - <c>platform.Name</c>,
        /// <c>Ground</c>, <c>SpiritPlatform</c> - and never the scene root's, because tests and
        /// diagnostics look the built arena up by those names. Written, like the position, before the
        /// node enters the tree, as <c>GameplayBuildShim.NewObject</c> did.
        /// </summary>
        /// <param name="scenePath">
        /// <c>SolidBox.tscn</c>, or the shortcut gate's inherited scene - the same box with a lever
        /// hung on it.
        /// </param>
        private static StaticBody2D CreateSolidBox(
            string name, Vector2 position, Vector2 size, Color color, int sortingOrder, string scenePath = SolidBoxScenePath)
        {
            var body = GD.Load<PackedScene>(scenePath).Instantiate<StaticBody2D>();
            body.Name = name;
            body.Position = position;

            // The scene marks its RectangleShape2D resource_local_to_scene, so this resizes this box
            // and not every other one in the arena.
            ((RectangleShape2D)body.GetNode<CollisionShape2D>(SolidBoxShapeName).Shape).Size = size;

            // The white square is generated at runtime - there is no baked Resources/Art/Square.png for
            // the scene to carry - and the stretch is per instance, so the texture and the size are the
            // two things Dress still has to write. Pivot (0.5, 0.5) is Sprite2D's own centring, which the
            // scene authors, so the offset it computes is zero.
            Sprite2D sprite = body.GetNode<Sprite2D>(SolidBoxSpriteName);
            GameplayVisualFactory.Dress(sprite, GameplayVisualFactory.CreateSquareSprite(Colors.White), size, new Vector2(0.5f, 0.5f));
            sprite.Modulate = color;
            sprite.ZIndex = sortingOrder;

            GameplayBuildShim.SceneRoot?.AddChild(body);
            return body;
        }

        /// <summary>
        /// One arena sign. The Control's centring - Unity's TextAnchor.MiddleCenter - and its resting
        /// font size and sorting order are authored in <c>Scenes/World/WorldLabel.tscn</c>; the text, the
        /// colour and the two readability numbers are bound here.
        ///
        /// <see cref="GameplayEnemySpawner"/> instances the same scene for the word under an enemy's
        /// feet, with the role-marker font size and sorting order instead.
        /// </summary>
        private static void CreateWorldLabel(string label, Vector2 position, Color color, GameplayReadabilityDefaults readability)
        {
            var go = GD.Load<PackedScene>(WorldLabelScenePath).Instantiate<Node2D>();
            go.Name = label + "Label";

            // Positioned before it enters the tree, as GameplayBuildShim.NewObject did.
            go.Position = position;

            Label text = go.GetNode<Label>("Text");
            text.Text = label;
            text.ZIndex = readability.WorldLabelSortingOrder;
            text.AddThemeFontSizeOverride("font_size", readability.WorldLabelFontSizePx);
            text.AddThemeColorOverride("font_color", color);

            GameplayBuildShim.SceneRoot?.AddChild(go);
        }

        private static void CreateSceneryPiece(string name, Vector2 position, Texture2D texture, Vector2 size, Color color, int sortingOrder)
        {
            CreateSceneryPiece(name, position, texture, size, color, sortingOrder, new Vector2(0.5f, 0.5f));
        }

        /// <summary>Decoration: a sprite and nothing else, exactly as in Unity - no collider, no body.</summary>
        private static void CreateSceneryPiece(string name, Vector2 position, Texture2D texture, Vector2 size, Color color, int sortingOrder, Vector2 pivot)
        {
            Sprite2D sprite = GameplayBuildShim.NewObject<Sprite2D>(name, position);
            GameplayVisualFactory.Dress(sprite, texture, size, pivot);
            sprite.Modulate = color;
            sprite.ZIndex = sortingOrder;
        }

        private static Color GetSceneryColor(string name, GameplayReadabilityDefaults readability)
        {
            return name switch
            {
                "Moon" => readability.MoonColor,
                "DistantArchMid" => readability.DistantArchMidColor,
                _ => readability.DistantArchColor,
            };
        }

        private static int GetScenerySortingOrder(string name, GameplayReadabilityDefaults readability)
        {
            return name switch
            {
                "Moon" => readability.MoonSortingOrder,
                "DistantArchMid" => readability.DistantArchMidSortingOrder,
                _ => readability.DistantArchSortingOrder,
            };
        }
    }
}
