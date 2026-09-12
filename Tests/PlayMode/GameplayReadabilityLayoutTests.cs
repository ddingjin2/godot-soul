using System;
using System.Linq;
using System.Reflection;
using Godot;
using MyGame.Core;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// The layout half of the read lives in a designer-owned file, the sibling of the artist's palette,
    /// with no copy in code. The same two silent failures apply: the file could stop being read, or a
    /// field could land on the wrong property - the caster's danger disc sized like the boss slam, say
    /// - and both produce a game that runs. (That the file names every field at all is
    /// <c>DesignFileCompletenessTests</c>' job.)
    ///
    /// UNITS: this is the one place the conversion is asserted rather than trusted. The file is Unity
    /// metres with +Y up; the second test hands every field a distinct metre value and demands the
    /// pixel value the property's kind implies - scaled for a size, scaled and flipped for an offset,
    /// untouched for a point size or a fraction.
    /// </summary>
    public sealed class GameplayReadabilityLayoutTests
    {
        [Test]
        public void ReadabilityLayoutJson_LoadsFromTheDesignersFolder()
        {
            GameplayReadabilityLayoutData layout = GameplayTuningCatalog.Load()?.ReadabilityLayout;
            Assert.NotNull(layout,
                "Resources/Design/ReadabilityLayout.json should load through the tuning catalog.");

            Assert.AreNotEqual(Vector2.Zero, layout.bossVisualSize, "A layout of all-zero sizes is an empty file, not a layout.");
        }

        /// <summary>
        /// The check the one above cannot make: a file that loads is also what an <c>ApplyTo</c> that
        /// did nothing would produce. Every field gets a value nothing else has and must arrive at its
        /// namesake property, converted the way that property's kind demands.
        /// </summary>
        [Test]
        public void EveryLayoutField_LandsOnItsNamesake_ThroughTheRightConversion()
        {
            var layout = new GameplayReadabilityLayoutData();
            FieldInfo[] fields = typeof(GameplayReadabilityLayoutData)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(Vector2) || f.FieldType == typeof(float) || f.FieldType == typeof(int))
                .ToArray();

            Assert.Greater(fields.Length, 40, "The layout should carry every size and offset the spawners bind.");

            for (var i = 0; i < fields.Length; i++)
            {
                // Distinct per field, distinct per component, and never zero - a flip that lost its sign
                // or a scale that was skipped both have to show.
                float v = 1f + i * 0.25f;
                fields[i].SetValue(layout, fields[i].FieldType == typeof(Vector2) ? new Vector2(v, v + 0.125f)
                    : fields[i].FieldType == typeof(int) ? (object)(10 + i) : v);
            }

            GameplayReadabilityDefaults defaults = GameplayReadabilityDefaults.Create();
            layout.ApplyTo(defaults);

            foreach (FieldInfo field in fields)
            {
                string propertyName = field.Name == "lockOnMarkerHeight"
                    ? nameof(GameplayReadabilityDefaults.LockOnMarkerOffset)
                    : char.ToUpperInvariant(field.Name[0]) + field.Name.Substring(1);
                PropertyInfo property = typeof(GameplayReadabilityDefaults).GetProperty(propertyName);

                Assert.NotNull(property,
                    $"Layout field '{field.Name}' has no '{propertyName}' to land on; it is a number nobody reads.");

                AssertNumber(Expected(field, field.GetValue(layout)), property.GetValue(defaults), $"{field.Name} -> {propertyName}");
            }
        }

        /// <summary>What the property must hold for an authored value, by the field's kind - the same rule GameplayReadabilityDefaults documents.</summary>
        private static object Expected(FieldInfo field, object authored)
        {
            string name = field.Name;

            if (name == "lockOnMarkerHeight")
                return World.V(new Vector2(0f, (float)authored));

            if (name.EndsWith("Offset", StringComparison.Ordinal) || name.EndsWith("LocalPosition", StringComparison.Ordinal))
                return World.V((Vector2)authored);

            if (name.EndsWith("FontSize", StringComparison.Ordinal)
                || name.EndsWith("Threshold", StringComparison.Ordinal)
                || name.EndsWith("TintBlend", StringComparison.Ordinal)
                || name.EndsWith("HeightFraction", StringComparison.Ordinal)
                || name.StartsWith("markerPulse", StringComparison.Ordinal))
                return authored;

            // Everything left is a size, a radius, a character size, a thickness or a margin: scaled, never flipped.
            return authored is Vector2 size ? new Vector2(World.U(size.X), World.U(size.Y)) : (object)World.U((float)authored);
        }

        private static void AssertNumber(object expected, object actual, string name)
        {
            switch (expected)
            {
                case Vector2 v:
                    Assert.AreEqual(v.X, ((Vector2)actual).X, 0.001f, $"{name} x.");
                    Assert.AreEqual(v.Y, ((Vector2)actual).Y, 0.001f, $"{name} y - the flipped axis.");
                    break;
                case float f:
                    Assert.AreEqual(f, (float)actual, 0.001f, name);
                    break;
                default:
                    Assert.AreEqual(expected, actual, name);
                    break;
            }
        }
    }
}
