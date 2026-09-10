namespace MyGame.Combat
{
    public enum DamageType
    {
        None,
        Standard,
        Heavy,
        Parryable
    }

    /// <summary>
    /// The seven sins the voyager carries. Names follow RainbowChapterBossDesign.
    /// The first four values keep their original names and order because the design JSON stores a sin
    /// as its integer, and P0CombatStabilityTestRunner parses nested enums by name; new values are
    /// appended in the chapter order [[RainbowChapterBossDesign]] gives them.
    ///
    /// Chapter number and enum integer are not the same number, and confusing them picks the wrong sin
    /// with no compile error: Gluttony is chapter 2 but enum 4, Greed chapter 3 / enum 5, Envy chapter 4 /
    /// enum 6, Lust chapter 5 / enum 7. The integers here are None 0, Wrath 1, Sloth 2, Pride 3.
    /// </summary>
    public enum SinState
    {
        None,
        Wrath,
        Sloth,
        Pride,
        Gluttony,
        Greed,
        Envy,
        Lust
    }

    public enum HitResult
    {
        None,
        Hit,
        Parry,
        PerfectParry
    }
}
