using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    public enum GraphicsPreset
    {
        High,
        Low,
        Custom,
    }

    /// <summary>
    /// Player-facing graphics options. High and Low set every value at once; changing a single
    /// value on its own leaves the preset reading Custom. Values persist in PlayerPrefs.
    /// </summary>
    /// <remarks>
    /// Unity ran <see cref="LoadAndApply"/> off <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c>.
    /// Godot has no equivalent hook, so the gameplay/title bootstrap calls it once at startup. Until it
    /// is called the properties hold their defaults, which are the High preset.
    /// </remarks>
    public static class GraphicsOptions
    {
        public static readonly int[] MsaaSteps = { 1, 2, 4, 8 };
        public static readonly float[] RenderScaleSteps = { 0.6f, 0.8f, 1f };

        private const string Prefix = "MyGame.Graphics.";

        public static bool ScreenShake { get; private set; } = true;
        public static bool HitStop { get; private set; } = true;
        public static bool HitFlash { get; private set; } = true;
        public static int Msaa { get; private set; } = 4;
        public static float RenderScale { get; private set; } = 1f;
        public static bool VSync { get; private set; } = true;

        /// <summary>High or Low when every value matches that preset, Custom otherwise.</summary>
        public static GraphicsPreset Preset => Matches(true) ? GraphicsPreset.High
            : Matches(false) ? GraphicsPreset.Low
            : GraphicsPreset.Custom;

        public static void LoadAndApply()
        {
            ScreenShake = GetBool(nameof(ScreenShake), ScreenShake);
            HitStop = GetBool(nameof(HitStop), HitStop);
            HitFlash = GetBool(nameof(HitFlash), HitFlash);
            Msaa = PlayerPrefs.GetInt(Prefix + nameof(Msaa), Msaa);
            RenderScale = PlayerPrefs.GetFloat(Prefix + nameof(RenderScale), RenderScale);
            // Falls back to the project's own vsync so an untouched install never rewrites the display.
            VSync = GetBool(nameof(VSync), DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled);
            Apply();
        }

        public static void ApplyPreset(GraphicsPreset preset)
        {
            if (preset == GraphicsPreset.Custom)
                return;

            bool high = preset == GraphicsPreset.High;
            ScreenShake = high;
            HitStop = high;
            HitFlash = high;
            Msaa = high ? 4 : 1;
            RenderScale = high ? 1f : 0.6f;
            VSync = high;
            Save();
        }

        public static void SetScreenShake(bool value)
        {
            ScreenShake = value;
            Save();
        }

        public static void SetHitStop(bool value)
        {
            HitStop = value;
            Save();
        }

        public static void SetHitFlash(bool value)
        {
            HitFlash = value;
            Save();
        }

        public static void SetMsaa(int value)
        {
            Msaa = value;
            Save();
        }

        public static void SetRenderScale(float value)
        {
            RenderScale = Mathf.Clamp(value, 0.1f, 2f);
            Save();
        }

        public static void SetVSync(bool value)
        {
            VSync = value;
            Save();
        }

        /// <summary>Next value in the list, wrapping at the end. Used by the cycling option buttons.</summary>
        public static int NextStep(int[] steps, int current)
        {
            int index = System.Array.IndexOf(steps, current);
            return steps[(index + 1) % steps.Length];
        }

        public static float NextStep(float[] steps, float current)
        {
            int index = System.Array.FindIndex(steps, step => Mathf.IsEqualApprox(step, current));
            return steps[(index + 1) % steps.Length];
        }

        private static bool Matches(bool high)
        {
            return ScreenShake == high
                   && HitStop == high
                   && HitFlash == high
                   && Msaa == (high ? 4 : 1)
                   && Mathf.IsEqualApprox(RenderScale, high ? 1f : 0.6f)
                   && VSync == high;
        }

        private static void Save()
        {
            SetBool(nameof(ScreenShake), ScreenShake);
            SetBool(nameof(HitStop), HitStop);
            SetBool(nameof(HitFlash), HitFlash);
            PlayerPrefs.SetInt(Prefix + nameof(Msaa), Msaa);
            PlayerPrefs.SetFloat(Prefix + nameof(RenderScale), RenderScale);
            SetBool(nameof(VSync), VSync);
            PlayerPrefs.Save();
            Apply();
        }

        private static void Apply()
        {
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

            // URP's msaaSampleCount is the root viewport's 2D MSAA here. RenderScale has no counterpart:
            // URP's renderScale resized the render target, and Godot's equivalent (Viewport.Scaling3DScale)
            // is 3D-only, so on a 2D game the value is stored and shown but changes nothing until someone
            // wires it to the window's content-scale size. Kept rather than dropped so the options menu,
            // the saved prefs and any later renderer work all still agree on one number.
            if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
            {
                tree.Root.Msaa2D = Msaa switch
                {
                    >= 8 => Viewport.Msaa.Msaa8X,
                    >= 4 => Viewport.Msaa.Msaa4X,
                    >= 2 => Viewport.Msaa.Msaa2X,
                    _ => Viewport.Msaa.Disabled,
                };
            }
        }

        private static bool GetBool(string key, bool fallback)
        {
            return PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) != 0;
        }

        private static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
        }
    }
}
