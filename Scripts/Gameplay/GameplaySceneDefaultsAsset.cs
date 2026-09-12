using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// A per-scene override of the shipped arena. Unity's <c>ScriptableObject</c> with a
    /// <c>CreateAssetMenu</c> entry is a Godot <see cref="Resource"/> with <c>[Export]</c> fields, which
    /// the inspector offers under "New GameplaySceneDefaultsAsset" in the same way.
    /// </summary>
    /// <remarks>
    /// UNITS - the exported fields are authored in <b>Unity metres with +Y up</b>, exactly as the
    /// original asset was, and are converted on the way out in <see cref="ApplyTo"/>. That keeps the
    /// numbers a designer types here the same numbers they typed in Unity, and keeps
    /// <see cref="GameplaySceneDefaults"/>'s rule intact: everything reaching it is already in Godot
    /// pixels.
    /// <para>
    /// The Unity fields were <c>Vector3</c>; z was the camera's depth (-10) and the spawns' draw order,
    /// neither of which survives into 2D Godot, so both are <see cref="Vector2"/> here.
    /// </para>
    /// <para>
    /// No <c>.tres</c> ships: <c>Resources/Gameplay/SceneDefaults.tres</c> does not exist, so every load
    /// of this type answers null and the layout JSON alone decides the arena. The type stays as the
    /// hook a scene can opt into.
    /// </para>
    /// <para>
    /// The four value fields have no initialiser, by decision D4: they used to be set to
    /// <c>SceneLayout.json</c>'s camera and spawn so a freshly created asset changed nothing, which
    /// made an <c>[Export]</c> default a second copy of the designer's number. Nothing reads them
    /// unless the matching <c>override*</c> flag is ticked, and ticking a flag is exactly the moment
    /// the author means to type their own number - so a zero is harmless and an inherited default was
    /// not. Before that they were a camera at (8, 2) with a 7.0 half height that no shipped layout
    /// names (numbers audit §3.3).
    /// </para>
    /// </remarks>
    [GlobalClass]
    public sealed partial class GameplaySceneDefaultsAsset : Resource
    {
        [ExportGroup("Camera")]
        [Export] private bool overrideCamera;

        /// <summary>Unity metres, +Y up.</summary>
        [Export] private Vector2 cameraPosition;

        /// <summary>Visible half-height in Unity units - stays unconverted, see <see cref="GameplaySceneDefaults"/>.</summary>
        [Export] private float cameraOrthographicSize;

        [ExportGroup("Spawn")]
        [Export] private bool overrideSpawn;

        /// <summary>Unity metres, +Y up.</summary>
        [Export] private Vector2 playerSpawnPosition;

        /// <summary>Unity metres, +Y up.</summary>
        [Export] private Vector2 checkpointPosition;

        internal void ApplyTo(GameplaySceneDefaults defaults)
        {
            if (defaults == null)
                return;

            if (overrideCamera)
                defaults.OverrideCamera(World.V(cameraPosition), cameraOrthographicSize);

            if (overrideSpawn)
                defaults.OverrideSpawn(World.V(playerSpawnPosition), World.V(checkpointPosition));
        }
    }
}
