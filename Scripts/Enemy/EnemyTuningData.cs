using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// Unity <c>ScriptableObject</c> tuning asset -> Godot <see cref="Resource"/>. The authored numbers
    /// still come from <c>Resources/Design/*.json</c>, which is the source of truth the Unity project
    /// already treated it as; the <c>.asset</c> files were not ported.
    ///
    /// UNITS: the JSON is authored in Unity metres. <see cref="ScaleToPixels"/> runs once at load and
    /// multiplies the spatial fields by <see cref="World.Ppu"/>:
    /// <c>moveSpeed</c>, <c>attackKnockback</c>, <c>detectionRange</c>, <c>attackRange</c>,
    /// <c>bodySize</c>.
    /// Left alone, because they are not distances: <c>maxHealth</c>, <c>attackDamage</c>, every time in
    /// seconds, the whole poise block, <c>soulReward</c> and <c>enemyColor</c>.
    /// </summary>
    public partial class EnemyTuningData : Resource
    {
        // Base Stats
        [Export] public float maxHealth = 50f;
        [Export] public float moveSpeed = 2f;
        [Export] public float attackDamage = 10f;
        [Export] public float attackKnockback = 5f;

        // Detection
        [Export] public float detectionRange = 5f;
        [Export] public float attackRange = 1.5f;

        // Combat
        [Export] public float telegraphTime = 0.85f;
        [Export] public float attackDuration = 0.35f;
        [Export] public float attackCooldown = 1.5f;
        [Export] public float stunDuration = 0.8f;
        [Export] public Color enemyColor = Colors.Red;

        // Poise
        /// <summary>Stagger resistance. Zero means this enemy cannot be staggered at all.</summary>
        [Export] public float maxPoise = 30f;

        /// <summary>How much harder a heavy attack hits poise than a light one.</summary>
        [Export] public float poiseHeavyMultiplier = 2f;

        [Export] public float poiseRegenDelay = 2f;
        [Export] public float poiseRegenRate = 25f;

        // Reward
        /// <summary>Souls handed to whoever lands the killing blow.</summary>
        [Export] public int soulReward = 20;

        // Visuals
        [Export] public Vector2 bodySize = new Vector2(0.6f, 1f);

        /// <summary>
        /// Guards against a second pass over the same asset. The scaling is destructive - it rewrites the
        /// authored fields in place rather than exposing a parallel set of pixel properties - so running
        /// it twice would put every distance a hundred times too far out.
        /// </summary>
        protected bool _scaledToPixels;

        /// <summary>Metres -> pixels, once. Subclasses call <c>base.ScaleToPixels()</c> first.</summary>
        public virtual void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            _scaledToPixels = true;

            moveSpeed = World.U(moveSpeed);
            attackKnockback = World.U(attackKnockback);
            detectionRange = World.U(detectionRange);
            attackRange = World.U(attackRange);
            bodySize = new Vector2(World.U(bodySize.X), World.U(bodySize.Y));
        }

        /// <summary>
        /// Unity's <c>Resources.Load&lt;T&gt;("Design/MeleeGrunt")</c>, with the unit conversion folded
        /// in so no caller can forget it. Returns null when the file is missing, which every archetype
        /// already handles by falling back to its inline defaults.
        /// </summary>
        protected static T LoadFrom<T>(string designPath) where T : EnemyTuningData
        {
            T data = Res.LoadJson<T>(designPath);
            data?.ScaleToPixels();
            return data;
        }
    }
}
