namespace MyGame.Gameplay
{
    /// <summary>
    /// Shared names for the gameplay actors, so everything that builds one and everything that looks
    /// one up by name cannot drift apart.
    ///
    /// In Unity these named extracted prefab assets under <c>Resources/Prefabs</c>. There are no
    /// prefabs in this port - the spawners build every actor in code, which the Unity project already
    /// did whenever the prefab was missing - so <see cref="ResourceFolder"/> and the prefab-loading
    /// path are gone. The names stay, because they are still the node names the spawners give the
    /// actors they build, and the save data, the debug jump menu and the tests all read those.
    /// </summary>
    public static class GameplayPrefabNames
    {
        public const string Player = "Player";
        public const string MeleeGrunt = "MeleeGrunt";
        public const string LeapingAttacker = "LeapingAttacker";
        public const string RangedCaster = "RangedCaster";
        public const string WrathMiniBoss = "WrathMiniBoss";
        public const string EnemyProjectile = "EnemyProjectile";
    }
}
