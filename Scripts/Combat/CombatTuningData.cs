using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// Designer-owned combat numbers. Was a Unity ScriptableObject asset; here it is a Godot
    /// <see cref="Resource"/> whose values are loaded from <c>Resources/Design/CombatTuning.json</c>
    /// when that file exists, and otherwise stay at the shipped defaults below.
    /// </summary>
    /// <remarks>
    /// Unit scaling (see PORTING_GUIDE "Units and axes"). Scaled by <see cref="World.Ppu"/> in
    /// <see cref="Load"/>, because they are distances or speeds authored in Unity metres:
    /// <c>knockbackLight</c>, <c>knockbackMedium</c>, <c>knockbackHeavy</c>,
    /// <c>preferredSpacing</c>, <c>spacingForce</c>, <c>spacingRadius</c>.
    ///
    /// NOT scaled, on purpose: every duration, the stamina numbers, <c>knockbackDecayRate</c> and the
    /// scale/telegraph multipliers (all dimensionless), the two colours, and the
    /// <c>shakeIntensity*</c> values - <see cref="CameraShake"/> converts a shake intensity to pixels
    /// itself at the moment it writes the camera offset, so scaling it here would apply Ppu twice.
    /// </remarks>
    public partial class CombatTuningData : Resource
    {
        // Hit Stop
        [Export] public float hitStopDurationLight = 0.04f;
        [Export] public float hitStopDurationHeavy = 0.08f;
        [Export] public float hitStopDurationBoss = 0.12f;

        // Camera Shake (intensity is in Unity units; CameraShake scales it to pixels)
        [Export] public float shakeIntensityLight = 0.15f;
        [Export] public float shakeIntensityMedium = 0.25f;
        [Export] public float shakeIntensityHeavy = 0.4f;
        [Export] public float shakeDurationLight = 0.12f;
        [Export] public float shakeDurationMedium = 0.2f;
        [Export] public float shakeDurationHeavy = 0.35f;

        // Player Knockback
        [Export] public float knockbackLight = 4f;
        [Export] public float knockbackMedium = 7f;
        [Export] public float knockbackHeavy = 10f;
        [Export] public float knockbackDecayRate = 0.85f;

        // Invulnerability Flash
        [Export] public float flashDuration = 0.08f;
        [Export] public Color invulnFlashColor = new Color(0.5f, 0.5f, 1f, 1f);
        [Export] public Color hitFlashColor = Colors.White;

        // Attack Impact Scale
        [Export] public float impactScaleDuration = 0.1f;
        [Export] public float impactScaleAmount = 1.3f;

        // Stamina Costs
        [Export] public float attackCost = 20f;
        [Export] public float dodgeCost = 25f;
        [Export] public float parryCost = 15f;
        [Export] public float maxStamina = 100f;
        [Export] public float staminaRegenRate = 30f;
        [Export] public float staminaRegenDelay = 1.5f;

        // Telegraph Timing
        [Export] public float telegraphScalePulseMin = 1.0f;
        [Export] public float telegraphScalePulseMax = 1.2f;
        [Export] public float telegraphColorFlashInterval = 0.15f;

        // Group Combat
        [Export] public float preferredSpacing = 2f;
        [Export] public float spacingForce = 2f;
        [Export] public float spacingRadius = 3f;

        /// <summary>
        /// Reads the authored numbers, converting the spatial ones to pixels once, here. A missing file
        /// is not an error - it yields the shipped defaults, scaled the same way.
        /// </summary>
        public static CombatTuningData Load(string path = "Design/CombatTuning")
        {
            CombatTuningData data = Res.LoadJson<CombatTuningData>(path) ?? new CombatTuningData();

            data.knockbackLight = World.U(data.knockbackLight);
            data.knockbackMedium = World.U(data.knockbackMedium);
            data.knockbackHeavy = World.U(data.knockbackHeavy);
            data.preferredSpacing = World.U(data.preferredSpacing);
            data.spacingForce = World.U(data.spacingForce);
            data.spacingRadius = World.U(data.spacingRadius);

            return data;
        }
    }
}
