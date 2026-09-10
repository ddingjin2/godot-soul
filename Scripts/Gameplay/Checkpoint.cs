using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// A named respawn marker. The offset arrives in Godot space, like every other position the
    /// environment builder hands out - <c>GameplaySceneDefaults</c> is where the conversion happened.
    /// </summary>
    public sealed partial class Checkpoint : Node2D
    {
        [Export] private string checkpointId = "checkpoint";
        [Export] private Vector2 respawnOffset;

        public string CheckpointId => checkpointId;
        public Vector2 RespawnPosition => GlobalPosition + respawnOffset;

        /// <summary><paramref name="offset"/> is in Godot pixels (+Y down), already converted.</summary>
        public void Configure(string id, Vector2 offset)
        {
            checkpointId = string.IsNullOrWhiteSpace(id) ? "checkpoint" : id;
            respawnOffset = offset;
        }
    }
}
