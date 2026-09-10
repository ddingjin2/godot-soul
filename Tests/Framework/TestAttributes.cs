using System;

namespace MyGame.Tests
{
    /// <summary>
    /// The NUnit surface the ported Unity tests used, cut down to what they actually call.
    ///
    /// Godot has no test framework in the box and the Unity Test Framework does not exist here, so
    /// these four attributes plus <see cref="Assert"/> and <see cref="TestRunner"/> are the whole
    /// harness. A test method is either <c>void</c> (Unity's <c>[Test]</c>) or returns
    /// <c>Task</c> (Unity's <c>[UnityTest]</c> coroutine, which needed frames to pass).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
    }

    /// <summary>Runs before every test in the class. Unity's <c>[SetUp]</c>.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SetUpAttribute : Attribute
    {
    }

    /// <summary>Runs after every test in the class, pass or fail. Unity's <c>[TearDown]</c>.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TearDownAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a whole class as holding tests. Unity found tests by assembly definition; here the
    /// runner reflects over the loaded assembly and this is what it looks for.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class TestFixtureAttribute : Attribute
    {
    }
}
