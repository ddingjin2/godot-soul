using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    public enum CameraShakePreset
    {
        LightHit,
        HeavyHit,
        Invulnerable,
        BossPhase,
        BossSlam,
    }

    /// <summary>
    /// Screen shake. In Unity this component sat on the camera and pushed its own transform; here it is
    /// a plain node that writes <see cref="Camera2D.Offset"/> on whatever camera is current, so it does
    /// not have to be re-parented every time the camera is rebuilt.
    ///
    /// UNITS: shake intensities are authored in Unity metres in <c>CombatTuning.json</c> and converted
    /// to pixels once, inside <see cref="CombatTuningData.Load"/>. They used to be converted down here
    /// instead, at the moment the offset is written; the conversion moved to the boundary the project's
    /// rule names, so <see cref="TriggerShake(float, float)"/> now takes <b>pixels</b>. Durations are
    /// seconds and <c>shakeFrequency</c> is a unitless noise sampling rate; neither is scaled.
    /// </summary>
    public partial class CameraShake : Node
    {
        public static CameraShake Instance { get; private set; }

        private float _currentIntensity;
        private float _currentDuration;
        private float _elapsed;
        private bool _isShaking;
        private Vector2 _lastOffset;

        // Unity used Mathf.PerlinNoise(x, y); the guide's stand-in is a FastNoiseLite at unit frequency,
        // which already returns roughly -1..1 - so the source's (noise - 0.5) * 2 remap is gone.
        private readonly FastNoiseLite _noise = new()
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Value,
            Frequency = 1f,
        };

        private static readonly CombatTuningData Tuning = CombatTuningData.Shared;

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }
            Instance = this;

            // Unity's [DefaultExecutionOrder(1000)] plus LateUpdate: the shake has to be applied after
            // everything that moves the camera this frame.
            ProcessPriority = 1000;
            ProcessMode = ProcessModeEnum.Always;
        }

        /// <param name="intensity">Peak offset in <b>pixels</b> - already through World.U.</param>
        /// <param name="duration">Seconds.</param>
        public void TriggerShake(float intensity, float duration)
        {
            if (intensity <= 0f || duration <= 0f) return;
            if (!GraphicsOptions.ScreenShake) return;

            _currentIntensity = Mathf.Max(_currentIntensity, intensity);
            _currentDuration = duration;
            _elapsed = 0f;
            _isShaking = true;
        }

        public void TriggerShake(float intensity)
        {
            TriggerShake(intensity, Tuning.shakeDefaultDuration);
        }

        public void TriggerShake(CameraShakePreset preset)
        {
            switch (preset)
            {
                case CameraShakePreset.LightHit:
                    TriggerShake(Tuning.shakeIntensityLight, Tuning.shakeDurationLight);
                    break;
                case CameraShakePreset.HeavyHit:
                    TriggerShake(Tuning.shakeIntensityMedium, Tuning.shakeDurationMedium);
                    break;
                case CameraShakePreset.Invulnerable:
                    TriggerShake(Tuning.shakeIntensityInvulnerable, Tuning.shakeDurationInvulnerable);
                    break;
                case CameraShakePreset.BossPhase:
                    TriggerShake(Tuning.shakeIntensityBossPhase, Tuning.shakeDurationBossPhase);
                    break;
                case CameraShakePreset.BossSlam:
                    TriggerShake(Tuning.shakeIntensityHeavy, Tuning.shakeDurationHeavy);
                    break;
            }
        }

        public override void _Process(double delta)
        {
            if (!_isShaking) return;

            Camera2D camera = GetViewport()?.GetCamera2D();
            if (camera == null) return;

            // Take last frame's push back off before adding this one, so the shake never accumulates into
            // the camera's real position.
            camera.Offset -= _lastOffset;
            _lastOffset = Vector2.Zero;

            _elapsed += GameClock.UnscaledDeltaTime;
            float progress = _elapsed / _currentDuration;

            if (progress >= 1f)
            {
                _isShaking = false;
                _currentIntensity = 0f;
                _lastOffset = Vector2.Zero;
                return;
            }

            float decay = 1f - (_elapsed / _currentDuration);

            // Already pixels: CombatTuningData.Load did the World.U pass. Do not re-scale here.
            float intensity = _currentIntensity * decay;

            float x = _noise.GetNoise2D(GameClock.Time * Tuning.shakeFrequency, 0f) * intensity;
            float y = _noise.GetNoise2D(0f, GameClock.Time * Tuning.shakeFrequency) * intensity;

            _lastOffset = new Vector2(x, y);
            camera.Offset += _lastOffset;
        }

        public override void _ExitTree()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
