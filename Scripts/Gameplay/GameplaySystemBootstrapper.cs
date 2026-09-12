using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Finds the pieces <c>Scenes/World/GameplayShell.tscn</c> authors - the camera on its rig and the
    /// hit stop manager - and applies the framing and follow the arena was designed around.
    /// </summary>
    /// <remarks>
    /// Until K7 this class <i>built</i> the camera stack on every boot, because no scene authored one.
    /// The shell does now, so what is left here is lookup and binding: a scene that has lost a system
    /// node gets an error naming it rather than a silently rebuilt one (PLAN_CLOSEOUT D1, rule 2).
    /// </remarks>
    public static class GameplaySystemBootstrapper
    {
        public const string CameraRigObjectName = "CameraRig";

        /// <summary>
        /// The shell's <c>Main Camera</c>, made current. <paramref name="backgroundColor"/> was Unity's
        /// per-camera clear colour. Godot's Camera2D has none, so it becomes the renderer's default
        /// clear colour - the one thing that paints behind everything in a 2D scene.
        /// </summary>
        public static Camera2D FindCamera(Color backgroundColor)
        {
            Camera2D cam = SceneQuery.FindFirst<Camera2D>();

            if (cam == null)
            {
                GD.PushError("GameplaySystemBootstrapper: the scene authors no Camera2D; Scenes/World/GameplayShell.tscn carries 'CameraRig/Main Camera'. The arena has no camera.");
                return null;
            }

            cam.MakeCurrent();
            RenderingServer.SetDefaultClearColor(backgroundColor);

            // Unity's orthographic flag and its AudioListener are both gone: a Camera2D is always
            // orthographic, and Godot's default 2D audio listener is the current camera. CameraShake is
            // the camera's authored child and makes itself the singleton when it readies - which is
            // before this runs, because a child is ready before its parent's parent.
            return cam;
        }

        /// <summary>
        /// The rig the camera hangs under. Follow and the cutscene director write the rig's world
        /// position; <see cref="CameraShake"/> writes the camera's own offset. Before that split all
        /// three wrote one transform, and the shake's subtract-then-re-add ran against whatever the last
        /// writer had left there.
        /// </summary>
        /// <remarks>
        /// The rig <i>is</i> the follow node. In Unity the follow was a component added to the rig
        /// GameObject and wrote that transform's position; a Godot child cannot move its parent, so the
        /// two are one node instead. <c>ConfigureCameraFollow</c> finds it with <c>GetComponent</c>,
        /// which answers "this node, or a child of it" and therefore answers the rig itself.
        /// </remarks>
        public static Node2D FindCameraRig(Camera2D cam)
        {
            if (cam == null)
                return null;

            if (cam.GetParent() is Node2D rig && rig.Name == CameraRigObjectName)
                return rig;

            GD.PushError($"GameplaySystemBootstrapper: the camera is not parented to a Node2D named '{CameraRigObjectName}'; Scenes/World/GameplayShell.tscn authors that rig. Nothing frames or follows.");
            return null;
        }

        /// <summary>The shell's hit stop manager, which made itself the singleton when it readied.</summary>
        public static HitStopManager FindHitStopManager(string objectName)
        {
            if (HitStopManager.Instance != null)
                return HitStopManager.Instance;

            GD.PushError($"GameplaySystemBootstrapper: no HitStopManager in the scene; Scenes/World/GameplayShell.tscn authors one named '{objectName}'. Hits will not freeze the clock.");
            return null;
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

            Node2D rig = FindCameraRig(cam);
            if (rig == null)
                return;

            // CameraPosition is already Godot pixels with +Y down - GameplaySceneDefaults is the conversion
            // boundary and nothing downstream converts again.
            rig.GlobalPosition = scene.CameraPosition;

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

            Node2D rig = FindCameraRig(cam);
            if (rig == null)
                return;

            var follow = rig.GetComponent<GameplayCameraFollow2D>();
            if (follow == null)
            {
                // Only reachable for a rig authored as a plain Node2D. A follow child could not move it,
                // so this is a wiring error rather than something to paper over.
                GD.PushError($"GameplaySystemBootstrapper: the camera rig '{rig.Name}' is not a GameplayCameraFollow2D; the camera will not follow.");
                return;
            }

            // The feel is shared by every arena and lives on WorldTuning; the bounds are this arena's.
            follow.ApplyTuning(GameplayTuningCatalog.Load()?.WorldTuning);
            follow.Initialize(target, scene.CameraHorizontalBounds, scene.CameraVerticalBounds);
        }
    }
}
