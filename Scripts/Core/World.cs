using Godot;

namespace MyGame.Core
{
    /// <summary>
    /// The one place the Unity-to-Godot unit and layer conventions live.
    ///
    /// Unity 2D worked in metres with +Y up; Godot 2D works in pixels with +Y down. Both differences
    /// are handled the same way everywhere in this port: distances authored in the Unity project are
    /// multiplied by <see cref="Ppu"/>, and anything vertical flips sign.
    /// </summary>
    public static class World
    {
        /// <summary>Pixels per Unity world unit. Every authored distance, speed and acceleration scales by this.</summary>
        public const float Ppu = 100f;

        /// <summary>Scale a distance/speed/acceleration authored in Unity units into Godot pixels.</summary>
        public static float U(float unityUnits) => unityUnits * Ppu;

        /// <summary>
        /// Convert a Unity-space vector (metres, +Y up) into Godot space (pixels, +Y down).
        /// </summary>
        public static Vector2 V(Vector2 unityVector) => new Vector2(unityVector.X * Ppu, -unityVector.Y * Ppu);

        /// <summary>Back to Unity units, for numbers that are reported to save data or design JSON.</summary>
        public static float ToUnits(float pixels) => pixels / Ppu;

        // Collision layers, matching [layer_names] in project.godot. Bit values, ready for
        // CollisionLayer / CollisionMask / query masks.
        public static class Layer
        {
            public const uint World = 1 << 0;
            public const uint Player = 1 << 1;
            public const uint Enemy = 1 << 2;
            public const uint Ground = 1 << 3;
            public const uint PlayerHitbox = 1 << 4;
            public const uint EnemyHitbox = 1 << 5;
            public const uint Trigger = 1 << 6;

            /// <summary>Unity's <c>LayerMask.GetMask("Default", "Ground")</c> - what a ground probe tests.</summary>
            public const uint GroundProbe = World | Ground;
        }

        // Unity tags become Godot groups. Membership is added in each actor's _Ready.
        public static class Group
        {
            public const string Player = "player";
            public const string Enemy = "enemy";
        }
    }
}
