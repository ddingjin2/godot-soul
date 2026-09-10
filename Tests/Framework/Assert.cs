using System;
using System.Collections;
using System.Collections.Generic;

namespace MyGame.Tests
{
    /// <summary>A test asserted something that was not true.</summary>
    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message)
        {
        }
    }

    /// <summary>A test decided at runtime that it does not apply. Reported separately from a failure.</summary>
    public sealed class IgnoreException : Exception
    {
        public IgnoreException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// NUnit's <c>Assert</c>, reimplemented over the subset the ported tests call. Argument order and
    /// semantics match NUnit exactly - <c>AreEqual(expected, actual)</c> - because 11,000 lines of
    /// ported test code depend on that order being unchanged.
    /// </summary>
    public static class Assert
    {
        private const float DefaultTolerance = 1e-4f;

        public static void Fail(string message = "Assert.Fail") => throw new AssertionException(message);

        public static void Ignore(string message = "Ignored") => throw new IgnoreException(message);

        public static void AreEqual(object expected, object actual, string message = null)
        {
            if (!ValuesEqual(expected, actual))
            {
                throw Failure($"Expected <{Describe(expected)}>, was <{Describe(actual)}>", message);
            }
        }

        /// <summary>Float comparison with an explicit tolerance, NUnit's three-argument overload.</summary>
        public static void AreEqual(double expected, double actual, double tolerance, string message = null)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw Failure($"Expected <{expected}> +/- {tolerance}, was <{actual}>", message);
            }
        }

        public static void AreNotEqual(object expected, object actual, string message = null)
        {
            if (ValuesEqual(expected, actual))
            {
                throw Failure($"Expected anything but <{Describe(expected)}>", message);
            }
        }

        public static void AreSame(object expected, object actual, string message = null)
        {
            if (!ReferenceEquals(expected, actual))
            {
                throw Failure("Expected the same instance", message);
            }
        }

        public static void AreNotSame(object expected, object actual, string message = null)
        {
            if (ReferenceEquals(expected, actual))
            {
                throw Failure("Expected a different instance", message);
            }
        }

        public static void IsTrue(bool condition, string message = null)
        {
            if (!condition)
            {
                throw Failure("Expected true, was false", message);
            }
        }

        /// <summary>NUnit spells this <c>Assert.That</c> for a plain boolean; same check.</summary>
        public static void That(bool condition, string message = null) => IsTrue(condition, message);

        public static void IsFalse(bool condition, string message = null)
        {
            if (condition)
            {
                throw Failure("Expected false, was true", message);
            }
        }

        public static void IsNull(object value, string message = null)
        {
            if (!IsNullOrFreed(value))
            {
                throw Failure($"Expected null, was <{Describe(value)}>", message);
            }
        }

        public static void NotNull(object value, string message = null)
        {
            if (IsNullOrFreed(value))
            {
                throw Failure("Expected a value, was null", message);
            }
        }

        public static void IsNotNull(object value, string message = null) => NotNull(value, message);

        public static void Greater(double actual, double floor, string message = null)
        {
            if (!(actual > floor))
            {
                throw Failure($"Expected greater than <{floor}>, was <{actual}>", message);
            }
        }

        public static void GreaterOrEqual(double actual, double floor, string message = null)
        {
            if (!(actual >= floor))
            {
                throw Failure($"Expected at least <{floor}>, was <{actual}>", message);
            }
        }

        public static void Less(double actual, double ceiling, string message = null)
        {
            if (!(actual < ceiling))
            {
                throw Failure($"Expected less than <{ceiling}>, was <{actual}>", message);
            }
        }

        public static void LessOrEqual(double actual, double ceiling, string message = null)
        {
            if (!(actual <= ceiling))
            {
                throw Failure($"Expected at most <{ceiling}>, was <{actual}>", message);
            }
        }

        public static void Zero(double actual, string message = null)
        {
            if (Math.Abs(actual) > DefaultTolerance)
            {
                throw Failure($"Expected zero, was <{actual}>", message);
            }
        }

        public static void IsNotEmpty(IEnumerable collection, string message = null)
        {
            if (collection == null || !collection.GetEnumerator().MoveNext())
            {
                throw Failure("Expected a non-empty collection", message);
            }
        }

        public static void IsNotEmpty(string value, string message = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw Failure("Expected a non-empty string", message);
            }
        }

        public static void Contains(object expected, IEnumerable collection, string message = null)
        {
            foreach (object item in collection ?? Array.Empty<object>())
            {
                if (ValuesEqual(expected, item))
                {
                    return;
                }
            }

            throw Failure($"Expected the collection to contain <{Describe(expected)}>", message);
        }

        public static void DoesNotContain(object unexpected, IEnumerable collection, string message = null)
        {
            foreach (object item in collection ?? Array.Empty<object>())
            {
                if (ValuesEqual(unexpected, item))
                {
                    throw Failure($"Expected the collection not to contain <{Describe(unexpected)}>", message);
                }
            }
        }

        public static void IsInstanceOf<T>(object value, string message = null)
        {
            if (value is not T)
            {
                throw Failure($"Expected an instance of {typeof(T).Name}, was <{Describe(value)}>", message);
            }
        }

        public static void DoesNotThrow(Action action, string message = null)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                throw Failure($"Expected no exception, got {e.GetType().Name}: {e.Message}", message);
            }
        }

        /// <summary>
        /// A freed Godot object is not null as far as C# is concerned, but every test that asks
        /// "is this gone" means the Godot sense. <c>GodotObject.IsInstanceValid</c> is the check that
        /// stands in for Unity's overloaded <c>== null</c> on a destroyed object.
        /// </summary>
        private static bool IsNullOrFreed(object value)
        {
            return value switch
            {
                null => true,
                Godot.GodotObject godotObject => !Godot.GodotObject.IsInstanceValid(godotObject),
                _ => false,
            };
        }

        private static bool ValuesEqual(object expected, object actual)
        {
            if (expected is null || actual is null)
            {
                return IsNullOrFreed(expected) && IsNullOrFreed(actual);
            }

            // Floating point comparisons go through a tolerance, the way NUnit's AreEqual does for
            // float and double arguments - exact equality would make most of the ported tuning tests
            // fail on the last bit.
            if (IsNumeric(expected) && IsNumeric(actual))
            {
                double a = Convert.ToDouble(expected);
                double b = Convert.ToDouble(actual);
                return Math.Abs(a - b) <= DefaultTolerance * Math.Max(1.0, Math.Abs(a));
            }

            return Equals(expected, actual);
        }

        private static bool IsNumeric(object value) =>
            value is float or double or decimal or int or long or short or byte or uint or ulong or ushort or sbyte;

        private static string Describe(object value)
        {
            if (value is null)
            {
                return "null";
            }

            if (value is IEnumerable enumerable and not string)
            {
                var parts = new List<string>();
                foreach (object item in enumerable)
                {
                    parts.Add(item?.ToString() ?? "null");
                }

                return "[" + string.Join(", ", parts) + "]";
            }

            return value.ToString();
        }

        private static AssertionException Failure(string detail, string message)
        {
            return new AssertionException(string.IsNullOrEmpty(message) ? detail : $"{message}\n  {detail}");
        }
    }
}
