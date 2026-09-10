namespace MyGame.Combat
{
    /// <summary>
    /// One sin's worth of combat modifiers. Every knob here is one the code already applied when the
    /// three shipped sins were hardcoded fields on <see cref="SinResonanceController"/>; nothing new
    /// was invented for the table. Neutral is 1.0 / false, which is what an entry left alone means.
    ///
    /// A plain C# class, not a Godot Resource: it is a row in a JSON table, never a node and never an
    /// asset on disk of its own.
    /// </summary>
    public class SinModifiers
    {
        public SinState sin;

        public float speedMultiplier = 1f;
        public float damageMultiplier = 1f;
        public float dodgeDurationMultiplier = 1f;
        public float parryWindowMultiplier = 1f;
        public float damageTakenMultiplier = 1f;

        public bool disableHeal;
        public bool perfectParryBonus;
    }
}
