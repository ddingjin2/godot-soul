using Godot;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// Every sprite the greybox draws, either loaded from a baked PNG or generated on the spot.
    ///
    /// Unity's <c>Sprite</c> is a <see cref="Texture2D"/> here - Godot's Sprite2D takes the texture
    /// directly - and Unity's per-sprite <c>pixelsPerUnit</c> is <see cref="World.Ppu"/>, applied once
    /// by <see cref="Dress"/> rather than baked into the asset.
    ///
    /// The tone buffers below are indexed the way the Unity source wrote them, with y counting
    /// <b>upwards</b> from the bottom of the texture. Godot's <see cref="Image"/> counts downwards, so
    /// <see cref="ToTexture"/> flips the rows on the way out and nothing else in this file has to think
    /// about it - which is also why the shading passes read exactly as they did in Unity.
    /// </summary>
    public static class GameplayVisualFactory
    {
        /// <summary>
        /// Resources folder holding the baked sprite assets. A baked asset always wins over the
        /// procedural generator, so replacing a PNG in that folder is enough to reskin the game
        /// without touching code.
        /// </summary>
        public const string BakedSpriteResourceFolder = "Art/";

        public enum ActorSpriteKind
        {
            Player,
            Grunt,
            Leaper,
            Caster,
            Boss
        }

        public enum SpriteKind
        {
            Square,
            Disc,
            Sword,
            Arch
        }

        public static Texture2D CreateSprite(SpriteKind kind)
        {
            return kind switch
            {
                SpriteKind.Disc => CreateDiscSprite(),
                SpriteKind.Sword => CreateSwordSprite(),
                SpriteKind.Arch => CreateArchSprite(),
                _ => CreateSquareSprite(Colors.White),
            };
        }

        public static string BakedSpriteName(ActorSpriteKind kind) => "Actor" + kind;

        public static string BakedSpriteName(SpriteKind kind) => kind.ToString();

        private static Texture2D LoadBaked(string spriteName)
        {
            return Res.Load<Texture2D>(BakedSpriteResourceFolder + spriteName, ".png");
        }

        // --- Pivots ----------------------------------------------------------
        //
        // Unity gave each Sprite a normalized pivot measured from the texture's bottom-left, and drew it
        // at the object's origin. Godot's Sprite2D is centred and offset instead, so the pivot lives
        // here as data and Dress turns it into an Offset.

        /// <summary>The Unity pivot each generated actor sprite was created with. Feet-heavy, not centred.</summary>
        public static Vector2 Pivot(ActorSpriteKind kind) => new Vector2(0.5f, 0.45f);

        /// <summary>
        /// The Unity pivot each generated shape was created with. The sword turns about its hilt and the
        /// arch stands on its base, so neither is centred.
        /// </summary>
        public static Vector2 Pivot(SpriteKind kind) => kind switch
        {
            SpriteKind.Sword => new Vector2(0.1f, 0.5f),
            SpriteKind.Arch => new Vector2(0.5f, 0f),
            _ => new Vector2(0.5f, 0.5f),
        };

        /// <summary>
        /// Unity's <c>SpriteRenderer</c> with <c>drawMode = Sliced</c> and an explicit <c>size</c>, plus
        /// a pivot: give the sprite its texture, stretch it to <paramref name="sizePx"/>, and move its
        /// origin to where Unity's pivot was.
        ///
        /// The offset is in the texture's own pixels and so is scaled along with the sprite, exactly as
        /// a Unity pivot was. A <paramref name="sizePx"/> of zero leaves the texture at 1:1.
        /// </summary>
        public static void Dress(Sprite2D sprite, Texture2D texture, Vector2 sizePx, Vector2 pivot)
        {
            if (sprite == null)
                return;

            sprite.Texture = texture;
            sprite.Centered = true;

            if (texture == null)
                return;

            Vector2 textureSize = texture.GetSize();
            if (textureSize.X <= 0f || textureSize.Y <= 0f)
                return;

            // Godot pixel (0,0) is top-left, Unity's pivot is measured from bottom-left - hence the y
            // term reads (pivot.Y - 0.5) rather than (0.5 - pivot.Y).
            sprite.Offset = new Vector2(
                (0.5f - pivot.X) * textureSize.X,
                (pivot.Y - 0.5f) * textureSize.Y);

            if (sizePx.X > 0f && sizePx.Y > 0f)
                sprite.Scale = new Vector2(sizePx.X / textureSize.X, sizePx.Y / textureSize.Y);
        }

        public static Texture2D CreateSquareSprite(Color color)
        {
            const int size = 64;
            Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            image.Fill(color);
            return ImageTexture.CreateFromImage(image);
        }

        // --- Shading helpers -------------------------------------------------
        //
        // Generated sprites stay greyscale on purpose: the sprite's modulate is the gameplay
        // readability channel (telegraph yellow, stun cyan, hit flash), and the tint multiplies the
        // texture. Storing a luminance ramp instead of flat white therefore keeps every tinted state
        // readable while giving the shape volume - a yellow telegraph now reads as a lit yellow figure
        // rather than a yellow blob.
        //
        // Shapes are drawn into a tone buffer (-1 = empty, 0..1 = luminance) and this pass adds
        // the depth cues afterwards, so the per-actor shape code stays flat and editable.

        private const float EmptyTone = -1f;
        private const float OutlineTone = 0.10f;
        private const float GroundShadeTone = 0.38f;
        private const float RimLift = 0.22f;

        private static float[] NewToneBuffer(int length)
        {
            var tone = new float[length];
            for (int i = 0; i < length; i++)
                tone[i] = EmptyTone;
            return tone;
        }

        /// <summary>
        /// The tone buffer as a texture. The buffer's y counts up from the bottom, as Unity's textures
        /// did; Godot's image rows count down from the top, so the rows are written in reverse. Getting
        /// this wrong stands every actor on its head, which is the one bug in this file that is obvious
        /// on sight.
        /// </summary>
        private static Texture2D ToTexture(float[] tone, int width, int height)
        {
            Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float v = tone[y * width + x];
                    Color pixel = v < 0f ? new Color(0f, 0f, 0f, 0f) : new Color(v, v, v, 1f);
                    image.SetPixel(x, height - 1 - y, pixel);
                }
            }

            return ImageTexture.CreateFromImage(image);
        }

        /// <summary>
        /// Turns a flat tone silhouette into a shaded one: a top-left light ramp, a rim highlight
        /// on upward and leftward facing edges, and a dark outline hugging the silhouette.
        /// </summary>
        private static void ApplyDepth(float[] tone, int width, int height)
        {
            var filled = new bool[tone.Length];
            int yMin = height;
            int yMax = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (tone[y * width + x] < 0f)
                        continue;

                    filled[y * width + x] = true;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
            }

            if (yMax < yMin)
                return;

            bool Filled(int x, int y) =>
                x >= 0 && x < width && y >= 0 && y < height && filled[y * width + x];

            // Vertical light ramp: bright at the top of the silhouette, shadowed at its feet.
            float span = Mathf.Max(1, yMax - yMin);
            for (int y = yMin; y <= yMax; y++)
            {
                float shade = Mathf.Lerp(GroundShadeTone, 1f, (y - yMin) / span);
                for (int x = 0; x < width; x++)
                {
                    if (filled[y * width + x])
                        tone[y * width + x] *= shade;
                }
            }

            // Rim light on the faces the key light hits, sampled from the pre-shade silhouette.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!filled[y * width + x])
                        continue;
                    if (Filled(x, y + 1) && Filled(x - 1, y))
                        continue;

                    int i = y * width + x;
                    tone[i] = Mathf.Min(1f, tone[i] + RimLift);
                }
            }

            // Outline last: it only writes pixels that were empty, so it never eats the silhouette.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (filled[y * width + x])
                        continue;
                    if (!Filled(x - 1, y) && !Filled(x + 1, y) && !Filled(x, y - 1) && !Filled(x, y + 1))
                        continue;

                    tone[y * width + x] = OutlineTone;
                }
            }
        }

        public static Texture2D CreateActorSprite(ActorSpriteKind kind)
        {
            return LoadBaked(BakedSpriteName(kind)) ?? GenerateActorSprite(kind);
        }

        public static Texture2D GenerateActorSprite(ActorSpriteKind kind)
        {
            const int width = 64;
            const int height = 128;
            float[] tone = NewToneBuffer(width * height);

            void Rect(int xMin, int yMin, int xMax, int yMax, float value)
            {
                for (int y = Mathf.Clamp(yMin, 0, height - 1); y <= Mathf.Clamp(yMax, 0, height - 1); y++)
                {
                    for (int x = Mathf.Clamp(xMin, 0, width - 1); x <= Mathf.Clamp(xMax, 0, width - 1); x++)
                        tone[y * width + x] = value;
                }
            }

            void Ellipse(int cx, int cy, int rx, int ry, float value)
            {
                for (int y = Mathf.Max(0, cy - ry); y <= Mathf.Min(height - 1, cy + ry); y++)
                {
                    for (int x = Mathf.Max(0, cx - rx); x <= Mathf.Min(width - 1, cx + rx); x++)
                    {
                        float dx = (x - cx) / (float)rx;
                        float dy = (y - cy) / (float)ry;
                        if (dx * dx + dy * dy <= 1f)
                            tone[y * width + x] = value;
                    }
                }
            }

            void Triangle(Vector2 a, Vector2 b, Vector2 c, float value)
            {
                float Sign(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);

                int minX = Mathf.FloorToInt(Mathf.Min(a.X, Mathf.Min(b.X, c.X)));
                int maxX = Mathf.CeilToInt(Mathf.Max(a.X, Mathf.Max(b.X, c.X)));
                int minY = Mathf.FloorToInt(Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)));
                int maxY = Mathf.CeilToInt(Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)));

                for (int y = Mathf.Max(0, minY); y <= Mathf.Min(height - 1, maxY); y++)
                {
                    for (int x = Mathf.Max(0, minX); x <= Mathf.Min(width - 1, maxX); x++)
                    {
                        var p = new Vector2(x, y);
                        float d1 = Sign(p, a, b);
                        float d2 = Sign(p, b, c);
                        float d3 = Sign(p, c, a);
                        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
                        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
                        if (!(hasNeg && hasPos))
                            tone[y * width + x] = value;
                    }
                }
            }

            const float solid = 1f;
            const float dim = 0.62f;

            switch (kind)
            {
                case ActorSpriteKind.Player:
                    Triangle(new Vector2(18, 36), new Vector2(46, 36), new Vector2(32, 98), dim);
                    Ellipse(32, 96, 10, 12, solid);
                    Rect(27, 52, 37, 86, solid);
                    Rect(14, 60, 23, 68, solid);
                    Rect(41, 60, 50, 68, solid);
                    Rect(24, 24, 30, 54, solid);
                    Rect(34, 24, 40, 54, solid);
                    Rect(42, 36, 48, 88, solid);
                    break;
                case ActorSpriteKind.Grunt:
                    Ellipse(32, 92, 12, 12, solid);
                    Rect(20, 42, 44, 82, solid);
                    Rect(15, 48, 23, 66, dim);
                    Rect(41, 48, 49, 66, dim);
                    Rect(23, 22, 30, 44, solid);
                    Rect(34, 22, 41, 44, solid);
                    Rect(41, 58, 54, 64, solid);
                    break;
                case ActorSpriteKind.Leaper:
                    Ellipse(32, 90, 10, 11, solid);
                    Triangle(new Vector2(18, 42), new Vector2(48, 42), new Vector2(32, 86), solid);
                    Rect(12, 66, 24, 74, dim);
                    Rect(40, 66, 52, 74, dim);
                    Rect(20, 22, 28, 42, solid);
                    Rect(36, 22, 48, 34, solid);
                    break;
                case ActorSpriteKind.Caster:
                    Ellipse(32, 92, 10, 12, solid);
                    Triangle(new Vector2(16, 20), new Vector2(48, 20), new Vector2(32, 86), solid);
                    Rect(48, 42, 53, 92, dim);
                    Ellipse(50, 96, 6, 6, solid);
                    Rect(20, 52, 44, 58, dim);
                    break;
                case ActorSpriteKind.Boss:
                    Triangle(new Vector2(8, 28), new Vector2(56, 28), new Vector2(32, 104), dim);
                    Ellipse(32, 92, 15, 16, solid);
                    Triangle(new Vector2(18, 104), new Vector2(26, 104), new Vector2(18, 122), solid);
                    Triangle(new Vector2(38, 104), new Vector2(46, 104), new Vector2(46, 122), solid);
                    Rect(18, 38, 46, 82, solid);
                    Rect(5, 54, 21, 68, solid);
                    Rect(43, 54, 59, 68, solid);
                    Rect(20, 14, 29, 40, solid);
                    Rect(35, 14, 44, 40, solid);
                    break;
            }

            ApplyDepth(tone, width, height);

            // Unity set filterMode = Point here. Godot filters per CanvasItem, not per texture, so the
            // nearest-neighbour look is a project-wide default rather than something this file sets.
            return ToTexture(tone, width, height);
        }

        public static Texture2D CreateDiscSprite()
        {
            return LoadBaked(BakedSpriteName(SpriteKind.Disc)) ?? GenerateDiscSprite();
        }

        public static Texture2D GenerateDiscSprite()
        {
            const int size = 64;
            Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.48f;

            // Radially symmetric, so no row flip is needed on the way out.
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = new Vector2(x, y).DistanceTo(center);
                    if (distance > radius)
                    {
                        image.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                        continue;
                    }

                    // Self-lit orb: hot core falling off to a dark rim, so the projectile reads as
                    // a volume instead of a flat coin once the sprite is tinted.
                    float t = distance / radius;
                    float v = Mathf.Lerp(1f, 0.4f, t * t);
                    image.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            }

            return ImageTexture.CreateFromImage(image);
        }

        public static Texture2D CreateSwordSprite()
        {
            return LoadBaked(BakedSpriteName(SpriteKind.Sword)) ?? GenerateSwordSprite();
        }

        public static Texture2D GenerateSwordSprite()
        {
            const int width = 96;
            const int height = 24;
            float[] tone = NewToneBuffer(width * height);

            // Blade, with a bright fuller line down its spine.
            for (int y = 8; y <= 15; y++)
            {
                for (int x = 8; x <= 78; x++)
                    tone[y * width + x] = y == 12 ? 1f : 0.78f;
            }

            for (int y = 6; y <= 17; y++)
            {
                for (int x = 78; x <= 90; x++)
                {
                    if (Mathf.Abs(y - 12) <= 90 - x)
                        tone[y * width + x] = 1f;
                }
            }

            // Guard reads darker than the blade so the weapon silhouette has a readable hilt.
            for (int y = 3; y <= 20; y++)
            {
                for (int x = 5; x <= 10; x++)
                    tone[y * width + x] = 0.6f;
            }

            ApplyDepth(tone, width, height);
            return ToTexture(tone, width, height);
        }

        public static Texture2D CreateArchSprite()
        {
            return LoadBaked(BakedSpriteName(SpriteKind.Arch)) ?? GenerateArchSprite();
        }

        public static Texture2D GenerateArchSprite()
        {
            const int width = 64;
            const int height = 128;
            float[] tone = NewToneBuffer(width * height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool side = x < 12 || x > 51;
                    bool top = y > 84 && Mathf.Pow((x - 32) / 28f, 2f) + Mathf.Pow((y - 84) / 38f, 2f) <= 1f;
                    bool innerCut = x > 20 && x < 44 && y < 86;
                    if (!(side || top) || innerCut)
                        continue;

                    // Course the masonry so the pillars do not read as one poured slab.
                    bool mortar = y % 14 == 0 || (side && x % 11 == (y / 14) % 2 * 5);
                    tone[y * width + x] = mortar ? 0.55f : 0.85f;
                }
            }

            ApplyDepth(tone, width, height);
            return ToTexture(tone, width, height);
        }
    }
}
