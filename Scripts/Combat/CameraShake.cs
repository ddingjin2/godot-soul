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
    /// Shake intensities are authored in Unity units and converted to pixels here, at the one place the
    /// offset is written - which is why <see cref="CombatTuningData"/> leaves them unscaled.
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
            TriggerShake(intensity, 0.2f);
        }

        public void TriggerShake(CameraShakePreset preset)
        {
            switch (preset)
            {
                case CameraShakePreset.LightHit:
                    TriggerShake(0.15f, 0.12f);
                    break;
                case CameraShakePreset.HeavyHit:
                    TriggerShake(0.25f, 0.12f);
                    break;
                case CameraShakePreset.Invulnerable:
                    TriggerShake(0.05f, 0.08f);
                    break;
                case CameraShakePreset.BossPhase:
                    TriggerShake(0.5f, 0.5f);
                    break;
                case CameraShakePreset.BossSlam:
                    TriggerShake(0.4f, 0.25f);
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
            float intensity = World.U(_currentIntensity * decay);

            float x = _noise.GetNoise2D(GameClock.Time * 25f, 0f) * intensity;
            float y = _noise.GetNoise2D(0f, GameClock.Time * 25f) * intensity;

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
