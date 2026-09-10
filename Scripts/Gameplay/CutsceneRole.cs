namespace MyGame.Gameplay
{
    /// <summary>
    /// The set of objects a cutscene can drive.
    /// </summary>
    /// <remarks>
    /// In Unity these names were also Timeline track names, parsed with <c>Enum.TryParse</c> when a
    /// <c>.playable</c> was bound. Timeline is gone (see <see cref="CutsceneDirector"/>), so nothing
    /// parses these from a file any more - but the roles themselves survived the port unchanged,
    /// because "which object does this beat move" is the question the sequences still ask.
    /// </remarks>
    public enum CutsceneRole
    {
        CameraRig,
        Overlay,
        Signals,
        Player,
        Boss,
    }
}
