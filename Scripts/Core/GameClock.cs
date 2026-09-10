using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// Autoload that stands in for Unity's <c>Time</c> statics. Registered as <c>GameClock</c> in
    /// project.godot so the static fields are valid from the first frame of any scene.
    ///
    /// Prefer the <c>delta</c> argument of _Process/_PhysicsProcess where one is in scope; these
    /// statics exist for the ported code that reads the clock from a helper with no delta to hand.
    /// </summary>
    public partial class GameClock : Node
    {
        /// <summary>Unity <c>Time.deltaTime</c> - scaled frame time.</summary>
        public static float DeltaTime { get; private set; }

        /// <summary>Unity <c>Time.unscaledDeltaTime</c>.</summary>
        public static float UnscaledDeltaTime { get; private set; }

        /// <summary>Unity <c>Time.time</c> - scaled seconds since start.</summary>
        public static float Time { get; private set; }

        /// <summary>Unity <c>Time.unscaledTime</c>.</summary>
        public static float UnscaledTime { get; private set; }

        /// <summary>Unity <c>Time.fixedDeltaTime</c>. Godot's physics step is fixed by project settings.</summary>
        public static float FixedDeltaTime => 1f / Engine.PhysicsTicksPerSecond;

        /// <summary>
        /// Unity <c>Time.timeScale</c>. Godot scales both frame and physics time from
        /// <see cref="Engine.TimeScale"/>, which is what hit-stop wants.
        /// </summary>
        public static float TimeScale
        {
            get => (float)Engine.TimeScale;
            set => Engine.TimeScale = Mathf.Max(0f, value);
        }

        private ulong _lastTicksUsec;

        public override void _Ready()
        {
            // Unscaled time has to come from the OS clock, not from delta: Godot scales the delta it
            // hands _Process by Engine.TimeScale, and hit-stop drives that to 0, which would freeze an
            // unscaled clock derived from it - exactly when hit-stop needs it to keep running.
            ProcessMode = ProcessModeEnum.Always;
            _lastTicksUsec = Godot.Time.GetTicksUsec();
        }

        public override void _Process(double delta)
        {
            ulong now = Godot.Time.GetTicksUsec();
            UnscaledDeltaTime = (now - _lastTicksUsec) / 1_000_000f;
            _lastTicksUsec = now;

            DeltaTime = (float)delta;
            UnscaledTime += UnscaledDeltaTime;
            Time += DeltaTime;
        }
    }
}
