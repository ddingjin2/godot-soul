using System.Threading.Tasks;
using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Eases the camera rig from wherever it currently is to a target, for the cutscene beats an
    /// authored clip cannot express.
    /// </summary>
    /// <remarks>
    /// An authored clip holds absolute values, but every rig move in <c>CutsceneDirection.md</c> starts
    /// from a position only the running game knows - where camera follow left the rig, or where the
    /// player happened to die. Fade, letterbox and zoom belong to <see cref="CutsceneDirector"/>'s
    /// sequences; this owns rig position.
    /// <para>
    /// Targets arrive already in Godot pixels. The authored Unity distances that produce them are
    /// converted at their own call sites (<see cref="GameplayCutsceneTriggers"/> and the bootstrap),
    /// which is where the metres are actually written down.
    /// </para>
    /// </remarks>
    public sealed partial class CutsceneRigMove : Node
    {
        /// <summary>
        /// Bumped by every new <see cref="Begin"/>. An ease still awaiting a frame compares it after each
        /// await and gives up if a newer one has started - the coroutine-stopping the Unity version did
        /// with <c>StopCoroutine</c>, which async has no direct equivalent for.
        /// </summary>
        private int _generation;

        private Node2D _rig;

        /// <summary>
        /// Starts an ease on <paramref name="rig"/>, replacing whatever it was already doing.
        /// <paramref name="target"/> is a world position in Godot pixels.
        /// </summary>
        public static void Play(Node2D rig, Vector2 target, float duration, float delay = 0f)
        {
            if (rig == null)
                return;

            CutsceneRigMove mover = rig.GetComponent<CutsceneRigMove>();
            if (mover == null)
            {
                mover = new CutsceneRigMove { Name = nameof(CutsceneRigMove), ProcessMode = ProcessModeEnum.Always };
                rig.AddChild(mover);
            }

            mover._rig = rig;
            mover.Begin(target, duration, delay);
        }

        private void Begin(Vector2 target, float duration, float delay)
        {
            // One ease at a time. Two of these writing the same node is the same bug the rig split was
            // made to end, and the second would lerp from a start the first is still moving.
            _generation++;
            _ = Ease(_generation, target, duration, delay);
        }

        private async Task Ease(int generation, Vector2 target, float duration, float delay)
        {
            // Unscaled throughout. These beats ride on top of hit stop (timescale 0.05) and the victory
            // freeze (timescale 0), so a scaled delta would stall the move under exactly the moments it
            // is staging. Same reason the director's own curves run on GameClock.UnscaledDeltaTime.
            for (float waited = 0f; waited < delay; waited += GameClock.UnscaledDeltaTime)
            {
                if (!await NextFrame(generation))
                    return;
            }

            Vector2 start = _rig.GlobalPosition;
            for (float elapsed = 0f; elapsed < duration; elapsed += GameClock.UnscaledDeltaTime)
            {
                if (!await NextFrame(generation))
                    return;

                float t = duration > 0f ? Mathf.Clamp(elapsed / duration, 0f, 1f) : 1f;

                // Ease out: fast off the mark, settling into the target. The doc asks for it by name and
                // it is what makes a camera move read as a camera stopping rather than a cut.
                Apply(start.Lerp(target, 1f - (1f - t) * (1f - t)));
            }

            Apply(target);
        }

        /// <summary>
        /// Waits a frame and reports whether this ease still owns the rig. False when a newer ease
        /// started, when the node or the rig has been freed, or when the cutscene has handed the rig
        /// back.
        /// </summary>
        private async Task<bool> NextFrame(int generation)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            return IsInstanceValid(this)
                   && _generation == generation
                   && IsInstanceValid(_rig)
                   && !Released();
        }

        /// <summary>
        /// True once the cutscene has handed the rig back. <see cref="CutsceneDirector"/> switches
        /// <see cref="GameplayCameraFollow2D"/>'s processing back on when it restores, so follow ticking
        /// again is the signal that a skip - or the end of the shot - has taken the rig away from us.
        /// </summary>
        /// <remarks>
        /// Letting go leaves the rig mid-ease rather than snapping it to the target, on purpose: follow
        /// SmoothDamps back to the player from wherever the rig stands, and it does that from a half-done
        /// ease just as well as from the target. Snapping first would add a jump the player sees, in the
        /// one moment - a skip - where the player has asked for less camera work, not more.
        /// <para>
        /// Unity's <c>Behaviour.enabled</c> has no Godot counterpart; for a node whose whole job is
        /// _Process, "enabled" is "is processing".
        /// </para>
        /// </remarks>
        private bool Released()
        {
            GameplayCameraFollow2D follow = _rig.GetComponent<GameplayCameraFollow2D>();
            return follow != null && follow.IsProcessing();
        }

        private void Apply(Vector2 xy)
        {
            // Unity read and wrote the rig's z untouched, because the rig carried the camera's depth and
            // a move that wrote z flew the camera (CutsceneDirection 2d). Godot 2D has no z at all - draw
            // order is ZIndex, which is not a position - so the whole position is safe to write.
            _rig.GlobalPosition = xy;
        }
    }
}
