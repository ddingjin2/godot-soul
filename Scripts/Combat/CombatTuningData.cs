using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// Designer-owned combat feel: hit stop, screen shake, the hit flash and squash, the audio
    /// fallback gain, the pack spacing, and the swing knockback. Was a Unity ScriptableObject asset;
    /// here it is a Godot <see cref="Resource"/> loaded from
    /// <c>Resources/Design/CombatTuning.json</c>. A missing file is an error: <see cref="Load"/> returns
    /// null, <c>Res.LoadJson</c> has already said which file, and <c>GameplayBootstrap</c> refuses to
    /// build the arena. The file names every field; a key it leaves out reads as zero.
    /// </summary>
    /// <remarks>
    /// <b>Every key in the file is what the runtime actually ran before the file existed.</b> The class
    /// used to carry a second, never-exercised set of numbers (hit stop at 0.04, medium shake at 0.2)
    /// plus a third copy of the stamina block that <c>PlayerResources.json</c> owns. Those were
    /// removed rather than authored: a key nothing reads, or a key that disagrees with the live
    /// literal, is worse than no file at all.
    ///
    /// <b>Unit scaling</b> (see PORTING_GUIDE "Units and axes"). 1 Unity metre = 100 px, converted
    /// once, here, inside <see cref="Load"/>:
    /// <list type="bullet">
    /// <item><description><b>Unity metres -> pixels:</b> <c>knockbackLight</c>, the five
    /// <c>shakeIntensity*</c> values, <c>preferredSpacing</c>, <c>spacingForce</c>,
    /// <c>spacingRadius</c>.</description></item>
    /// <item><description><b>Crosses untouched:</b> every duration and delay (seconds),
    /// <c>hitStopPauseScale</c>, <c>impactScaleAmount</c> and <c>alignmentForce</c> (multipliers),
    /// <c>shakeFrequency</c> (a noise sampling rate, unitless), <c>audioFallbackVolume</c> (linear
    /// gain), and the two colours.</description></item>
    /// </list>
    /// The shake intensities used to be converted at the far end, in
    /// <see cref="CameraShake"/>._Process. That conversion moved here so the boundary is where the
    /// project's rule says it is - which also means <c>CameraShake.TriggerShake(float, float)</c> now
    /// takes <b>pixels</b>, not metres.
    /// </remarks>
    public partial class CombatTuningData : Resource
    {
        /// <summary>Base name of the design file.</summary>
        public const string FileName = "CombatTuning";

        // Hit stop. Seconds of frozen time, and the scale time runs at while frozen.
        [Export] public float hitStopDurationLight;
        [Export] public float hitStopDurationHeavy;
        [Export] public float hitStopDurationParry;

        /// <summary>How hard the freeze bites: 0 is a dead stop, 1 is no freeze at all.</summary>
        [Export] public float hitStopPauseScale;

        // Camera shake. Intensities are Unity metres and are scaled in Load(); durations are seconds.
        [Export] public float shakeIntensityLight;
        [Export] public float shakeIntensityMedium;
        [Export] public float shakeIntensityHeavy;
        [Export] public float shakeIntensityInvulnerable;
        [Export] public float shakeIntensityBossPhase;
        [Export] public float shakeDurationLight;
        [Export] public float shakeDurationMedium;
        [Export] public float shakeDurationHeavy;
        [Export] public float shakeDurationInvulnerable;
        [Export] public float shakeDurationBossPhase;

        /// <summary>Seconds a shake triggered without an explicit duration runs for.</summary>
        [Export] public float shakeDefaultDuration;

        /// <summary>How fast the screen rattles: the rate the shake noise is sampled at. Unitless.</summary>
        [Export] public float shakeFrequency;

        /// <summary>
        /// Metres. The swing volume's knockback - the same number
        /// <c>PlayerCombat.json.attackKnockback</c> authors for the attack that volume belongs to,
        /// read from here because Combat must not learn about <c>MyGame.Player</c>.
        /// </summary>
        [Export] public float knockbackLight;

        // The hit flash and the squash on the actor's sprite.
        [Export] public float flashDuration;
        [Export] public Color hitFlashColor;
        [Export] public Color invulnFlashColor;
        [Export] public float impactScaleDuration;
        [Export] public float impactScaleAmount;

        /// <summary>Linear gain a cue plays at when it has no volume of its own. 0..1.</summary>
        [Export] public float audioFallbackVolume;

        // Group combat. The three distances are Unity metres and are scaled in Load().
        [Export] public float preferredSpacing;
        [Export] public float spacingForce;
        [Export] public float spacingRadius;
        [Export] public float alignmentForce;

        /// <summary>
        /// Reads the authored numbers, converting the spatial ones to pixels once, here. Null when the
        /// file is missing.
        /// </summary>
        public static CombatTuningData Load(string path = "Design/" + FileName)
        {
            CombatTuningData data = Res.LoadJson<CombatTuningData>(path);
            if (data == null)
                return null;

            data.knockbackLight = World.U(data.knockbackLight);
            data.shakeIntensityLight = World.U(data.shakeIntensityLight);
            data.shakeIntensityMedium = World.U(data.shakeIntensityMedium);
            data.shakeIntensityHeavy = World.U(data.shakeIntensityHeavy);
            data.shakeIntensityInvulnerable = World.U(data.shakeIntensityInvulnerable);
            data.shakeIntensityBossPhase = World.U(data.shakeIntensityBossPhase);
            data.preferredSpacing = World.U(data.preferredSpacing);
            data.spacingForce = World.U(data.spacingForce);
            data.spacingRadius = World.U(data.spacingRadius);

            return data;
        }

        /// <summary>
        /// The file, read once per run. Every consumer initialises its fields from this, so the JSON
        /// is parsed a single time no matter how many actors are spawned. Null while the file is
        /// missing - and the bootstrap has stopped before any consumer runs.
        /// </summary>
        public static CombatTuningData Shared => _shared ??= Load();

        private static CombatTuningData _shared;
    }
}
