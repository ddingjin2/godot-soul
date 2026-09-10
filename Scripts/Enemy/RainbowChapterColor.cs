using Godot;

namespace MyGame.Enemy
{
    public enum RainbowChapterColor
    {
        Red,
        Orange,
        Yellow,
        Green,
        Blue,
        Indigo,
        Violet,

        /// <summary>
        /// Chapter eight, which is not a gate. The road is seven gates and the seventh is rebirth
        /// ([[WorldSetting]]); this is what comes after that, so it sits past the spectrum rather than
        /// inside it. Appended last because the design JSON stores this enum as its integer, and
        /// inserting a value anywhere else would repaint every authored chapter.
        /// </summary>
        White
    }

    public static class RainbowChapterColorPalette
    {
        public static Color ToColor(RainbowChapterColor chapterColor)
        {
            return chapterColor switch
            {
                RainbowChapterColor.Red => new Color(0.9f, 0.08f, 0.06f),
                RainbowChapterColor.Orange => new Color(1f, 0.42f, 0.06f),
                RainbowChapterColor.Yellow => new Color(1f, 0.82f, 0.12f),
                RainbowChapterColor.Green => new Color(0.12f, 0.75f, 0.28f),
                RainbowChapterColor.Blue => new Color(0.12f, 0.42f, 1f),
                RainbowChapterColor.Indigo => new Color(0.25f, 0.18f, 0.78f),
                RainbowChapterColor.Violet => new Color(0.65f, 0.2f, 0.9f),
                RainbowChapterColor.White => new Color(0.97f, 0.96f, 0.88f),
                _ => Colors.White,
            };
        }
    }
}
