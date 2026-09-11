using System;
using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>One keyframe of a cutscene channel: seconds into the shot, and the channel's value there.</summary>
    public class CutsceneKeyframe
    {
        public float time;
        public float value;
    }

    /// <summary>
    /// One authored shot: the Unity <c>.playable</c>'s keyframe tables, one array per track. A channel
    /// with no rows does not run. Every channel a shot has must end on neutral - fade 0, letterbox 0,
    /// camera 1 - because <see cref="CutsceneDirector.Skip"/> is "write neutral", not "evaluate the
    /// last frame".
    /// </summary>
    public class CutsceneShot
    {
        public string key;

        /// <summary>Overlay opacity, 0-1.</summary>
        public CutsceneKeyframe[] fade;

        /// <summary>Bar height in UI pixels - screen space, never <see cref="World.U"/>.</summary>
        public CutsceneKeyframe[] letterbox;

        /// <summary>
        /// The camera's orthographic size as a fraction of whatever it was when the shot started: 1 is
        /// the scene's own framing, 0.88 is a 12% push in. Unity animated absolute sizes (5.984 against
        /// a 6.8 rest); a fraction is the same push on a chapter that frames its room differently.
        /// </summary>
        public CutsceneKeyframe[] cameraSize;
    }

    /// <summary>
    /// Designer-owned cutscene timing: the four shots' keyframe tables, the beats the triggers play
    /// around them, and the bootstrap's entry settle. Loaded from
    /// <c>Resources/Design/CutsceneTuning.json</c>.
    /// </summary>
    /// <remarks>
    /// UNITS - three kinds cross this boundary and only one is scaled:
    /// <list type="bullet">
    /// <item><description><b>Unity metres -> pixels, scaled in <see cref="Load"/>:</b>
    /// <c>enterSettleHeight</c>, <c>deathMoveDistance</c>. Magnitudes, so no sign flip.</description></item>
    /// <item><description><b>UI pixels, NOT scaled:</b> every <c>letterbox</c> row.
    /// <see cref="CutsceneOverlay"/> works in screen space.</description></item>
    /// <item><description><b>Crosses untouched:</b> every time (seconds), <c>fade</c> (alpha 0-1),
    /// <c>cameraSize</c> (a fraction of the resting size).</description></item>
    /// </list>
    /// A missing file is an error: <see cref="Load"/> returns null, <c>Res.LoadJson</c> has said which
    /// file, and <c>GameplayBootstrap</c> refuses to build the arena. Should a director still be asked
    /// to <c>Play</c> with no file, it takes its missing-shot path - clear the overlay, hand the
    /// continuation back, carry on. A scalar key the file leaves out reads as zero and a shot it
    /// leaves out does not run; neither has a C# twin, because duplicating the numbers here would
    /// be the fallback drift the numbers audit (§2.8) exists to stop.
    /// </remarks>
    public partial class CutsceneTuningData : Resource
    {
        public const string FileName = "CutsceneTuning";

        // GameplayBootstrap's entry beat: the rig settles one unit down onto its rest, landing with the fade.
        [Export] public float enterSettleHeight;
        [Export] public float enterSettleDuration;

        // GameplayCutsceneTriggers' beats (CutsceneDirection.md 3 and 5).
        [Export] public float bossIntroMoveDuration;
        [Export] public float deathHitStop;
        [Export] public float deathMoveDelay;
        [Export] public float deathMoveDuration;
        [Export] public float deathMoveDistance;

        /// <summary>Not [Export]ed for the same reason as <c>SinTuningData.sins</c>: plain JSON rows, not Resources.</summary>
        public CutsceneShot[] shots;

        /// <summary>The shot named <paramref name="key"/> - the Unity resource names, case-insensitive - or null.</summary>
        public CutsceneShot Shot(string key)
        {
            if (key == null || shots == null)
                return null;

            foreach (CutsceneShot shot in shots)
            {
                if (string.Equals(shot?.key, key, StringComparison.OrdinalIgnoreCase))
                    return shot;
            }

            return null;
        }

        /// <summary>
        /// The bar height a shot opens on, so the bootstrap can stage it before the first rendered frame
        /// without carrying a second copy of the number. 0 for a shot with no letterbox track.
        /// </summary>
        public float OpeningLetterbox(string key)
        {
            CutsceneKeyframe[] rows = Shot(key)?.letterbox;
            return rows != null && rows.Length > 0 ? rows[0].value : 0f;
        }

        public static CutsceneTuningData Load(string path = "Design/" + FileName)
        {
            CutsceneTuningData data = Res.LoadJson<CutsceneTuningData>(path);
            if (data == null)
                return null;

            data.enterSettleHeight = World.U(data.enterSettleHeight);
            data.deathMoveDistance = World.U(data.deathMoveDistance);

            return data;
        }

        /// <summary>The file, read once per run. Null while it is missing.</summary>
        public static CutsceneTuningData Shared => _shared ??= Load();

        private static CutsceneTuningData _shared;
    }
}
