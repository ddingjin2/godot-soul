using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The pieces every gameplay scene needs whether or not it authored them: a camera on its rig, a hit
    /// stop manager, and the framing and follow the arena was designed around.
    /// </summary>
    public static class GameplaySystemBootstrapper
    {
        public const string CameraRigObjectName = "CameraRig";

        /// <summary>
        /// <paramref name="backgroundColor"/> was Unity's per-camera clear colour. Godot's Camera2D has
        /// none, so it becomes the renderer's default clear colour - the one thing that paints behind
        /// everything in a 2D scene.
        /// </summary>
        public static Camera2D EnsureCamera(Color backgroundColor)
        {
            Camera2D cam = SceneQuery.FindFirst<Camera2D>();

            if (cam == null)
            {
                cam = new Camera2D { Name = "Main Camera" };
                GameplayBuildShim.SceneRoot?.AddChild(cam);
            }

            cam.MakeCurrent();
            RenderingServer.SetDefaultClearColor(backgroundColor);

            // Unity's orthographic flag and its AudioListener are both gone: a Camera2D is always
            // orthographic, and Godot's default 2D audio listener is the current camera.

            // CameraShake drives the current camera's own offset now and no longer has to be parented to
            // it - but there must be exactly one, and it is a singleton, so the instance is the test.
            if (CameraShake.Instance == null)
                cam.AddChild(new CameraShake { Name = nameof(CameraShake) });

            EnsureCameraRig(cam);
            return cam;
        }

        /// <summary>
        /// Splits the camera into a rig parent, and returns it. Follow and the cutscene director write
        /// the rig's world position; <see cref="CameraShake"/> writes the camera's own offset. Before the
        /// split all three wrote one transform, and the shake's subtract-then-re-add ran against whatever
        /// the last writer had left there.
        /// </summary>
        /// <remarks>
        /// The camera stays the current one, so <c>GetViewport().GetCamera2D()</c> keeps resolving to it.
        /// </remarks>
        public static Node2D EnsureCameraRig(Camera2D cam)
        {
            if (cam == null)
                return null;

            if (cam.GetParent() is Node2D existing && existing.Name == CameraRigObjectName)
                return existing;

            // The rig *is* the follow node. In Unity the follow was a component added to the rig
            // GameObject and wrote that transform's position; a Godot child cannot move its parent, so
            // the two are one node instead. ConfigureCameraFollow finds it with GetComponent, which
            // answers "this node, or a child of it" and therefore answers the rig itself.
            var rig = new GameplayCameraFollow2D { Name = CameraRigObjectName };
            Node parent = cam.GetParent();

            if (parent == null)
            {
                // A camera not yet in the tree: build the rig above it and let the caller place the rig.
                rig.AddChild(cam);
                GameplayBuildShim.SceneRoot?.AddChild(rig);
            }
            else
            {
                rig.Position = cam.Position;
                parent.AddChild(rig);
                parent.RemoveChild(cam);
                rig.AddChild(cam);
            }

            cam.Position = Vector2.Zero;
            cam.Rotation = 0f;
            cam.MakeCurrent();
            return rig;
        }

        public static HitStopManager EnsureHitStopManager(string objectName)
        {
            if (HitStopManager.Instance != null)
                return HitStopManager.Instance;

            var hitStop = new HitStopManager { Name = objectName };
            GameplayBuildShim.SceneRoot?.AddChild(hitStop);
            return hitStop;
        }

        public static void FrameCombatRoom(Camera2D cam)
        {
            FrameCombatRoom(cam, GameplaySceneDefaults.Create());
        }

        /// <summary>
        /// Puts the rig where the arena was framed from and matches the authored orthographic half
        /// height. Godot's Camera2D has no orthographicSize - it has Zoom - so the authored half height
        /// becomes a zoom against the viewport's own height. CameraOrthographicSize is the one field
        /// GameplaySceneDefaults leaves in Unity units, which is why it alone is scaled here.
        /// </summary>
        public static void FrameCombatRoom(Camera2D cam, GameplaySceneDefaults scene)
        {
            if (cam == null || scene == null)
                return;

            // CameraPosition is already Godot pixels with +Y down - GameplaySceneDefaults is the conversion
            // boundary and nothing downstream converts again.
            EnsureCameraRig(cam).GlobalPosition = scene.CameraPosition;

            float halfHeightPx = World.U(scene.CameraOrthographicSize);
            if (halfHeightPx <= 0f)
                return;

            float viewportHeight = cam.GetViewportRect().Size.Y;
            cam.Zoom = Vector2.One * (viewportHeight * 0.5f / halfHeightPx);
        }

        public static void ConfigureCameraFollow(Camera2D cam, Node2D target, GameplaySceneDefaults scene)
        {
            if (cam == null || target == null || scene == null)
                return;

            Node2D rig = EnsureCameraRig(cam);
            var follow = rig.GetComponent<GameplayCameraFollow2D>();
            if (follow == null)
            {
                // Only reachable for a rig authored by hand as a plain Node2D. A follow child could not
                // move it, so this is a wiring error rather than something to paper over.
                GD.PushError($"GameplaySystemBootstrapper: the camera rig '{rig.Name}' is not a GameplayCameraFollow2D; the camera will not follow.");
                return;
            }

            // The feel is shared by every arena and lives on WorldTuning; the bounds are this arena's.
            follow.ApplyTuning(GameplayTuningCatalog.Load()?.WorldTuning);
            follow.Initialize(target, scene.CameraHorizontalBounds, scene.CameraVerticalBounds);
        }
    }
}
