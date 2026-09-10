namespace MyGame.Combat
{
    /// <summary>
    /// Implemented by actors that react to a broken <see cref="Poise"/> gauge, so
    /// <see cref="CombatResolver"/> can stagger a target without knowing which actor type it is.
    /// Same trick as <see cref="IDamageGuard"/>: it keeps Combat from having to reference Player or Enemy.
    /// </summary>
    public interface IStaggerable
    {
        void OnPoiseBroken();
    }
}
