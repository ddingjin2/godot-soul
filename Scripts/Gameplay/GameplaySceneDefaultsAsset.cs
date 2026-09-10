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
    /// </remarks>
    [GlobalClass]
    public sealed partial class GameplaySceneDefaultsAsset : Resource
    {
        [ExportGroup("Camera")]
        [Export] private bool overrideCamera;

        /// <summary>Unity metres, +Y up.</summary>
        [Export] private Vector2 cameraPosition = new(8f, 2f);

        /// <summary>Visible half-height in Unity units - stays unconverted, see <see cref="GameplaySceneDefaults"/>.</summary>
        [Export] private float cameraOrthographicSize = 7f;

        [ExportGroup("Spawn")]
        [Export] private bool overrideSpawn;

        /// <summary>Unity metres, +Y up.</summary>
        [Export] private Vector2 playerSpawnPosition = new(-2f, 0.5f);

        /// <summary>Unity metres, +Y up.</summary>
        [Export] private Vector2 checkpointPosition = new(-2f, 0.5f);

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
