namespace MyGame.Combat
{
    /// <summary>
    /// Implemented by actors that can refuse incoming damage, so <see cref="CombatResolver"/> can ask
    /// about parry and invulnerability without knowing which actor type is in front of it.
    /// </summary>
    public interface IDamageGuard
    {
        bool IsInParryWindow();

        bool IsInPerfectParryWindow();

        bool IsInvulnerable();

        void NotifyParrySuccess(bool perfect);
    }
}
