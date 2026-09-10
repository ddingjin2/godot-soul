#if TOOLS
using System;
using System.IO;
using Godot;

namespace MyGame.EditorTools
{
    /// <summary>
    /// Bakes the procedurally generated actor and prop sprites into PNG assets under
    /// <c>res://Resources/Art</c>. Baking them lets a human replace any of the shapes by editing a PNG,
    /// and it is what makes the sprites referenceable from a saved resource rather than only from the
    /// code that draws them.
    /// </summary>
    /// <remarks>
    /// <b>This tool overwrites the PNGs in that folder unconditionally.</b> That was true of the Unity
    /// version and is kept: the baker is the generator's output, so a bake that skipped existing files
    /// would leave the folder half-generated and half-stale, which is worse than either. Hand-painted
    /// replacements belong in version control before anyone presses this.
    ///
    /// <b>Ported from Unity.</b> The shape code is <c>GameplayVisualFactory</c>'s, duplicated here
    /// rather than called: this addon has to build while <c>MyGame.Gameplay</c> is still being ported,
    /// and an editor plugin that fails to compile takes the whole build with it. If the shapes ever
    /// change, they change in both places.
    ///
    /// Two Unity concepts have no PNG-level counterpart in Godot and moved to the code that draws the
    /// sprite: the import <c>spritePixelsPerUnit</c> (Godot draws textures 1:1 and <c>World.Ppu</c>
    /// carries the scale) and the sprite pivot (a <c>Sprite2D</c> has <c>Offset</c>). The pivots the
    /// Unity importer wrote are recorded beside each request below so the spawner can apply them.
    ///
    /// Point versus bilinear filtering is likewise not in the file: it is
    /// <c>rendering/textures/canvas_textures/default_texture_filter</c> in project.godot, already set
    /// to Nearest for the pixel art, overridable per <c>CanvasItem</c> for the two soft props.
    /// </remarks>
    public static class GameplaySpriteBaker
    {
        public const string BakedSpriteFolder = "res://Resources/Art";

        // Shading constants, from GameplayVisualFactory. Generated sprites stay greyscale on purpose:
        // the sprite's modulate is the gameplay readability channel (telegraph yellow, stun cyan, hit
        // flash) and it multiplies the texture, so a luminance ramp keeps every tinted state readable
        // while giving the shape volume.
        private const float EmptyTone = -1f;
        private const float OutlineTone = 0.10f;
        private const float GroundShadeTone = 0.38f;
        private const float RimLift = 0.22f;

        private enum ActorSpriteKind
        {
            Player,
            Grunt,
            Leaper,
            Caster,
            Boss
        }

        public static void BakeAll()
        {
            Directory.CreateDirectory(DesignDataFiles.Abs(BakedSpriteFolder));

            var baked = 0;
            foreach (ActorSpriteKind kind in Enum.GetValues<ActorSpriteKind>())
            {
                // Pivot (0.5, 0.45) in Unity - just below centre, so the actor stands on its feet.
                baked += Save("Actor" + kind, GenerateActorSprite(kind));
            }

            baked += Save("Disc", GenerateDiscSprite());   // pivot (0.5, 0.5)
            baked += Save("Sword", GenerateSwordSprite()); // pivot (0.1, 0.5) - rotates about the grip
            baked += Save("Arch", GenerateArchSprite());   // pivot (0.5, 0.0) - stands on the ground line

            GD.Print($"GameplaySpriteBaker: baked {baked} generated sprites into {BakedSpriteFolder} (existing files were overwritten).");
        }

        private static int Save(string spriteName, Image image)
        {
            if (image == null)
            {
                return 0;
            }

            string path = DesignDataFiles.Abs(BakedSpriteFolder + "/" + spriteName + ".png");
            Error error = image.SavePng(path);
            if (error != Error.Ok)
            {
                GD.PushError($"GameplaySpriteBaker: could not write {spriteName}.png ({error}).");
                return 0;
            }

            return 1;
        }

        // --- Shape generators, ported from GameplayVisualFactory ----------------------------------
        //
        // Shapes are drawn into a tone buffer (-1 = empty, 0..1 = luminance) and ApplyDepth adds the
        // depth cues afterwards, so the per-actor shape code stays flat and editable.
        //
        // The one change from the Unity source is the vertical flip in ToImage. Unity's Texture2D has
        // pixel (0,0) at the bottom-left; a Godot Image has it at the top-left. The coordinates below
        // are Unity's untouched, so the buffer is flipped exactly once, on the way out - which is also
        // what keeps a re-bake byte-identical to the PNGs that came over from the Unity project.

        private static Image GenerateActorSprite(ActorSpriteKind kind)
        {
            const int width = 64;
            const int height = 128;
            float[] tone = NewToneBuffer(width * height);

            void Rect(int xMin, int yMin, int xMax, int yMax, float value)
            {
                for (int y = Mathf.Clamp(yMin, 0, height - 1); y <= Mathf.Clamp(yMax, 0, height - 1); y++)
                {
                    for (int x = Mathf.Clamp(xMin, 0, width - 1); x <= Mathf.Clamp(xMax, 0, width - 1); x++)
                    {
                        tone[y * width + x] = value;
                    }
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
                        {
                            tone[y * width + x] = value;
                        }
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
                        {
                            tone[y * width + x] = value;
                        }
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
            return ToImage(tone, width, height);
        }

        private static Image GenerateDiscSprite()
        {
            const int size = 64;
            var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.48f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    float distance = new Vector2(x, y).DistanceTo(center);
                    if (distance > radius)
                    {
                        image.SetPixel(x, size - 1 - y, new Color(0f, 0f, 0f, 0f));
                        continue;
                    }

                    // Self-lit orb: hot core falling off to a dark rim, so the projectile reads as
                    // a volume instead of a flat coin once the renderer tints it.
                    float t = distance / radius;
                    float v = Mathf.Lerp(1f, 0.4f, t * t);
                    image.SetPixel(x, size - 1 - y, new Color(v, v, v, 1f));
                }
            }

            return image;
        }

        private static Image GenerateSwordSprite()
        {
            const int width = 96;
            const int height = 24;
            float[] tone = NewToneBuffer(width * height);

            // Blade, with a bright fuller line down its spine.
            for (var y = 8; y <= 15; y++)
            {
                for (var x = 8; x <= 78; x++)
                {
                    tone[y * width + x] = y == 12 ? 1f : 0.78f;
                }
            }

            for (var y = 6; y <= 17; y++)
            {
                for (var x = 78; x <= 90; x++)
                {
                    if (Mathf.Abs(y - 12) <= 90 - x)
                    {
                        tone[y * width + x] = 1f;
                    }
                }
            }

            // Guard reads darker than the blade so the weapon silhouette has a readable hilt.
            for (var y = 3; y <= 20; y++)
            {
                for (var x = 5; x <= 10; x++)
                {
                    tone[y * width + x] = 0.6f;
                }
            }

            ApplyDepth(tone, width, height);
            return ToImage(tone, width, height);
        }

        private static Image GenerateArchSprite()
        {
            const int width = 64;
            const int height = 128;
            float[] tone = NewToneBuffer(width * height);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    bool side = x < 12 || x > 51;
                    bool top = y > 84 && Mathf.Pow((x - 32) / 28f, 2f) + Mathf.Pow((y - 84) / 38f, 2f) <= 1f;
                    bool innerCut = x > 20 && x < 44 && y < 86;
                    if (!(side || top) || innerCut)
                    {
                        continue;
                    }

                    // Course the masonry so the pillars do not read as one poured slab.
                    bool mortar = y % 14 == 0 || (side && x % 11 == (y / 14) % 2 * 5);
                    tone[y * width + x] = mortar ? 0.55f : 0.85f;
                }
            }

            ApplyDepth(tone, width, height);
            return ToImage(tone, width, height);
        }

        // --- Shading helpers ----------------------------------------------------------------------

        private static float[] NewToneBuffer(int length)
        {
            var tone = new float[length];
            for (var i = 0; i < length; i++)
            {
                tone[i] = EmptyTone;
            }

            return tone;
        }

        /// <summary>Tone buffer to a Godot image, flipping Unity's bottom-up rows to Godot's top-down.</summary>
        private static Image ToImage(float[] tone, int width, int height)
        {
            var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    float v = tone[y * width + x];
                    image.SetPixel(x, height - 1 - y, v < 0f ? new Color(0f, 0f, 0f, 0f) : new Color(v, v, v, 1f));
                }
            }

            return image;
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
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (tone[y * width + x] < 0f)
                    {
                        continue;
                    }

                    filled[y * width + x] = true;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
            }

            if (yMax < yMin)
            {
                return;
            }

            bool Filled(int x, int y) =>
                x >= 0 && x < width && y >= 0 && y < height && filled[y * width + x];

            // Vertical light ramp: bright at the top of the silhouette, shadowed at its feet.
            float span = Mathf.Max(1, yMax - yMin);
            for (int y = yMin; y <= yMax; y++)
            {
                float shade = Mathf.Lerp(GroundShadeTone, 1f, (y - yMin) / span);
                for (var x = 0; x < width; x++)
                {
                    if (filled[y * width + x])
                    {
                        tone[y * width + x] *= shade;
                    }
                }
            }

            // Rim light on the faces the key light hits, sampled from the pre-shade silhouette.
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!filled[y * width + x])
                    {
                        continue;
                    }

                    if (Filled(x, y + 1) && Filled(x - 1, y))
                    {
                        continue;
                    }

                    int i = y * width + x;
                    tone[i] = Mathf.Min(1f, tone[i] + RimLift);
                }
            }

            // Outline last: it only writes pixels that were empty, so it never eats the silhouette.
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (filled[y * width + x])
                    {
                        continue;
                    }

                    if (!Filled(x - 1, y) && !Filled(x + 1, y) && !Filled(x, y - 1) && !Filled(x, y + 1))
                    {
                        continue;
                    }

                    tone[y * width + x] = OutlineTone;
                }
            }
        }
    }
}
#endif
