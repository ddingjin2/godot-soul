#if TOOLS
using System.IO;
using Godot;
using MyGame.Core;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Builds the player's sword swing animations.
    /// </summary>
    /// <remarks>
    /// Unity wrote two <c>.anim</c> clips and an <c>.controller</c> whose state machine ran
    /// Idle -&gt; Attack off an <c>Attack</c> trigger and back on exit time. Godot's
    /// <c>AnimationPlayer</c> plays a clip by name, so the state machine has nothing to do: the trigger
    /// became the clip's name (see <c>PlayerAttackAnimator2D.AttackTriggerName</c>), the Any-State
    /// transition became stop-then-play, and the return to Idle is the caller's business. What is left
    /// is the two clips, in one <see cref="AnimationLibrary"/>.
    ///
    /// Same keyframes as Unity, with the two conversions this port applies everywhere:
    /// <list type="bullet">
    /// <item><description>Positions were metres and are now pixels - <c>World.U</c>.</description></item>
    /// <item><description>Rotation sign flips. Godot 2D has +Y down, so a positive angle turns
    /// clockwise; Unity's +Z turned counter-clockwise. The swing has to come down on the same side of
    /// the player it always did.</description></item>
    /// </list>
    ///
    /// Unity rebuilt these on domain reload when they were missing. Not carried over: writing resource
    /// files as a side effect of loading the editor is how a generated asset quietly replaces a hand
    /// edit. It is the button.
    /// </remarks>
    public static class PlayerAttackAnimationAssetCreator
    {
        public const string LibraryPath = "res://Resources/Art/Animations/Player/PlayerSword.tres";

        public const string IdleAnimationName = "Idle";

        /// <summary>Matches <c>MyGame.Player.PlayerAttackAnimator2D.AttackTriggerName</c>, which plays it by name.</summary>
        public const string AttackAnimationName = "Attack";

        private const float FrameRate = 60f;

        public static void Rebuild()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DesignDataFiles.Abs(LibraryPath)));

            var library = new AnimationLibrary();
            library.AddAnimation(IdleAnimationName, CreateIdleClip());
            library.AddAnimation(AttackAnimationName, CreateAttackClip());

            Error error = ResourceSaver.Save(library, LibraryPath);
            if (error != Error.Ok)
            {
                GD.PushError($"PlayerAttackAnimationAssetCreator: could not write {LibraryPath} ({error}).");
                return;
            }

            GD.Print($"PlayerAttackAnimationAssetCreator: wrote {LibraryPath} ({IdleAnimationName}, {AttackAnimationName}). Add it to the sword's AnimationPlayer as the default library.");
        }

        /// <summary>The rest pose: sword held back and slightly out, held for a tenth of a second and looped.</summary>
        private static Animation CreateIdleClip()
        {
            var clip = new Animation { Length = 0.1f, LoopMode = Animation.LoopModeEnum.Linear, Step = 1f / FrameRate };

            int rotation = NewTrack(clip, ".:rotation");
            Rotate(clip, rotation, 0f, -28f);
            Rotate(clip, rotation, 0.1f, -28f);

            int offset = NewTrack(clip, ".:position:x");
            Offset(clip, offset, 0f, 0.12f);
            Offset(clip, offset, 0.1f, 0.12f);

            return clip;
        }

        /// <summary>Windup, strike, follow-through, recover - the four keys the Unity clip used, unchanged.</summary>
        private static Animation CreateAttackClip()
        {
            var clip = new Animation { Length = 0.24f, LoopMode = Animation.LoopModeEnum.None, Step = 1f / FrameRate };

            const float windup = 0f;
            const float strike = 0.08f;
            const float followThrough = 0.16f;
            const float recover = 0.24f;

            int rotation = NewTrack(clip, ".:rotation");
            Rotate(clip, rotation, windup, 38f);
            Rotate(clip, rotation, strike, -62f);
            Rotate(clip, rotation, followThrough, -82f);
            Rotate(clip, rotation, recover, -28f);

            int offset = NewTrack(clip, ".:position:x");
            Offset(clip, offset, windup, 0.18f);
            Offset(clip, offset, strike, 0.34f);
            Offset(clip, offset, followThrough, 0.28f);
            Offset(clip, offset, recover, 0.12f);

            return clip;
        }

        /// <summary>
        /// A value track on the animated node itself. Unity's empty relative path meant "the object the
        /// Animator is on"; an AnimationPlayer's root node defaults to its parent, which is that node.
        /// </summary>
        private static int NewTrack(Animation clip, string path)
        {
            int track = clip.AddTrack(Animation.TrackType.Value);
            clip.TrackSetPath(track, path);
            clip.ValueTrackSetUpdateMode(track, Animation.UpdateMode.Continuous);
            return track;
        }

        private static void Rotate(Animation clip, int track, float time, float unityDegrees)
        {
            clip.TrackInsertKey(track, time, Mathf.DegToRad(-unityDegrees));
        }

        private static void Offset(Animation clip, int track, float time, float unityUnits)
        {
            clip.TrackInsertKey(track, time, World.U(unityUnits));
        }
    }
}
#endif
