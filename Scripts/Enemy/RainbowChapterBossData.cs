using System.Collections.Generic;
using Godot;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// One authored chapter boss: who it is, what it is worth, and the rows
    /// <see cref="RainbowChapterBossBehaviour"/> swings.
    ///
    /// The backing fields were <c>[SerializeField] private</c> in Unity. They are public here for the
    /// same reason <see cref="BossAttackProfile"/>'s are - the design JSON is now the only author, and
    /// <c>System.Text.Json</c> only binds public members. The properties below are still how the boss
    /// loop reads them.
    ///
    /// UNITS: <see cref="ScaleToPixels"/> scales <c>moveSpeed</c>, <c>detectionRange</c>,
    /// <c>attackRange</c>, <c>afterimageSpread</c>, <c>approachHopImpulse</c> and <c>bodySize</c>, and
    /// hands each attack row its own conversion. Health, the phase thresholds and multipliers, every
    /// chant/stance time in seconds, the poise block, <c>soulReward</c> and the colours stay as authored.
    /// </summary>
    public sealed partial class RainbowChapterBossData : Resource
    {
        // Chapter
        [Export] public RainbowChapterColor chapterColor;
        [Export] public int chapterIndex = 1;
        [Export] public string chapterName = "Red Chapter";

        // Boss Identity
        [Export] public string bossId = "red_boss";
        [Export] public string bossName = "The Red Gatekeeper";
        [Export(PropertyHint.MultilineText)] public string designPillar = "Pressure, restraint, and readable punishment.";

        // Combat Stats
        [Export] public float maxHealth = 240f;
        [Export] public float moveSpeed = 2.5f;
        [Export] public float detectionRange = 8f;
        [Export] public float attackRange = 2f;
        [Export] public float phaseTwoHealthThreshold = 0.5f;
        [Export] public float phaseTwoSpeedMultiplier = 1.2f;
        [Export] public float phaseTwoCooldownMultiplier = 0.75f;

        // Approach
        /// <summary>
        /// Seconds between hops while closing on the player. Zero is a boss that walks, which is every
        /// fight shaped like chapter one. Above zero is chapter two's pursuit: the gap closes in bounds
        /// the player can read and time, rather than at a constant crawl.
        /// </summary>
        [Export] public float approachHopInterval;

        /// <summary>
        /// Upward launch speed of each hop, authored positive the way Unity's +Y-up world wanted it.
        /// The sign flip into Godot's +Y-down space happens where the hop is applied, not here. Only
        /// read when approachHopInterval is above zero.
        /// </summary>
        [Export] public float approachHopImpulse = 5f;

        // Chant
        /// <summary>
        /// Seconds between chants. Zero is a boss that never heals, which is every fight but chapter
        /// four's. The chant is the priority window: it is the one thing worth dropping a punish to
        /// interrupt.
        /// </summary>
        [Export] public float chantInterval;

        /// <summary>
        /// How long a chant runs if nobody stops it. Long enough to be reached from across the arena, or
        /// interrupting it is a coin flip rather than a decision.
        /// </summary>
        [Export] public float chantDuration = 2.5f;

        [Export] public float chantHealPerSecond = 12f;

        // Afterimages
        /// <summary>
        /// Copies thrown off when an attack begins. They read as the boss and they never damage anyone -
        /// the lesson is to track the source, not the spectacle. Zero is every boss but chapter six's.
        /// </summary>
        [Export] public int afterimageCount;

        /// <summary>How far to either side the copies stand.</summary>
        [Export] public float afterimageSpread = 2.2f;

        /// <summary>
        /// How long a copy lasts. Shorter than the telegraph means the player never has to choose; much
        /// longer means the arena is never legible again.
        /// </summary>
        [Export] public float afterimageLifetime = 0.9f;

        // Stances
        /// <summary>
        /// Seconds between stance changes, for a boss whose attacks are grouped by stanceId. Zero keeps
        /// every attack available at once, which is every boss that has only one stance.
        /// </summary>
        [Export] public float stanceRotationInterval;

        // Poise And Reward
        /// <summary>
        /// Deep enough that chip damage never staggers a boss: breaking it should take committed heavy
        /// attacks, which is the trade the poise gauge exists to force.
        /// </summary>
        [Export] public float maxPoise = 90f;

        [Export] public float poiseHeavyMultiplier = 2f;
        [Export] public float poiseRegenDelay = 3f;
        [Export] public float poiseRegenRate = 30f;

        /// <summary>Souls the kill is worth. The wallet is the drop table, so this is the reward.</summary>
        [Export] public int soulReward = 300;

        // Presentation
        [Export] public Color primaryColor = Colors.Red;
        [Export] public Color secondaryColor = Colors.Black;
        [Export] public Vector2 bodySize = new Vector2(1.6f, 2.3f);

        // Attacks
        [Export] public BossAttackProfile[] attacks = System.Array.Empty<BossAttackProfile>();

        private bool _scaledToPixels;

        public RainbowChapterColor ChapterColor => chapterColor;
        public int ChapterIndex => chapterIndex;
        public string ChapterName => chapterName;
        public string BossId => bossId;
        public string BossName => bossName;
        public string DesignPillar => designPillar;
        public float MaxHealth => maxHealth;
        public float MoveSpeed => moveSpeed;
        public float DetectionRange => detectionRange;
        public float AttackRange => attackRange;
        public float PhaseTwoHealthThreshold => phaseTwoHealthThreshold;
        public float PhaseTwoSpeedMultiplier => phaseTwoSpeedMultiplier;
        public float PhaseTwoCooldownMultiplier => phaseTwoCooldownMultiplier;
        public float ChantInterval => Mathf.Max(0f, chantInterval);
        public float ChantDuration => Mathf.Max(0f, chantDuration);
        public float ChantHealPerSecond => Mathf.Max(0f, chantHealPerSecond);
        public bool ChantsWhileFighting => ChantInterval > 0f && ChantDuration > 0f;

        public int AfterimageCount => Mathf.Max(0, afterimageCount);
        public float AfterimageSpread => Mathf.Max(0f, afterimageSpread);
        public float AfterimageLifetime => Mathf.Max(0f, afterimageLifetime);
        public bool CastsAfterimages => AfterimageCount > 0 && AfterimageLifetime > 0f;

        public float StanceRotationInterval => Mathf.Max(0f, stanceRotationInterval);
        public bool RotatesStances => StanceRotationInterval > 0f;

        /// <summary>
        /// Every distinct stanceId across the attack rows, in the order they first appear. The order is
        /// the rotation: a boss walks its stances rather than rolling them, so the player can learn what
        /// comes next.
        /// </summary>
        public string[] StanceIds()
        {
            var ids = new List<string>();
            if (attacks == null)
            {
                return ids.ToArray();
            }

            foreach (BossAttackProfile profile in attacks)
            {
                if (profile == null || string.IsNullOrEmpty(profile.StanceId))
                {
                    continue;
                }

                if (!ids.Contains(profile.StanceId))
                {
                    ids.Add(profile.StanceId);
                }
            }

            return ids.ToArray();
        }

        public float ApproachHopInterval => Mathf.Max(0f, approachHopInterval);
        public float ApproachHopImpulse => Mathf.Max(0f, approachHopImpulse);
        public bool HopsWhileApproaching => ApproachHopInterval > 0f && ApproachHopImpulse > 0f;
        public float MaxPoise => maxPoise;
        public float PoiseHeavyMultiplier => poiseHeavyMultiplier;
        public float PoiseRegenDelay => poiseRegenDelay;
        public float PoiseRegenRate => poiseRegenRate;
        public int SoulReward => soulReward;
        public Color PrimaryColor => primaryColor;
        public Color SecondaryColor => secondaryColor;
        public Vector2 BodySize => bodySize;
        public BossAttackProfile[] Attacks => attacks;

        /// <summary>
        /// The attack row with this id, or null. Chains name their next step by id rather than by index
        /// so reordering the array never silently repoints a chain at a different attack.
        /// </summary>
        public BossAttackProfile FindAttack(string attackId)
        {
            if (string.IsNullOrEmpty(attackId) || attacks == null)
            {
                return null;
            }

            foreach (BossAttackProfile profile in attacks)
            {
                if (profile != null && profile.AttackId == attackId)
                {
                    return profile;
                }
            }

            return null;
        }

        /// <summary>
        /// Unity's <c>OnValidate</c>, which the editor ran on every field edit. There is no editor to
        /// run it here, so <see cref="Load"/> calls it once against the authored JSON.
        /// </summary>
        public void OnValidate()
        {
            if (primaryColor == default)
            {
                primaryColor = RainbowChapterColorPalette.ToColor(chapterColor);
            }

            // Eight, not seven: the road is seven gates, and chapter eight is what stands after the
            // seventh - past the road rather than one more stop on it.
            chapterIndex = Mathf.Clamp(chapterIndex, 1, 8);
            maxHealth = Mathf.Max(1f, maxHealth);
            moveSpeed = Mathf.Max(0f, moveSpeed);
            detectionRange = Mathf.Max(0f, detectionRange);
            attackRange = Mathf.Max(0f, attackRange);
            phaseTwoHealthThreshold = Mathf.Clamp(phaseTwoHealthThreshold, 0f, 1f);
            phaseTwoSpeedMultiplier = Mathf.Max(0.01f, phaseTwoSpeedMultiplier);
            phaseTwoCooldownMultiplier = Mathf.Max(0.01f, phaseTwoCooldownMultiplier);
        }

        /// <summary>Metres -> pixels, once, for this boss and every row it owns.</summary>
        public void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            _scaledToPixels = true;

            moveSpeed = World.U(moveSpeed);
            detectionRange = World.U(detectionRange);
            attackRange = World.U(attackRange);
            afterimageSpread = World.U(afterimageSpread);
            approachHopImpulse = World.U(approachHopImpulse);
            bodySize = new Vector2(World.U(bodySize.X), World.U(bodySize.Y));

            if (attacks == null)
            {
                return;
            }

            foreach (BossAttackProfile profile in attacks)
            {
                profile?.ScaleToPixels();
            }
        }

        /// <summary>
        /// The authored chapter boss at <paramref name="designPath"/> - for example
        /// <c>"Design/Chapter02_Orange_EmberPilgrim"</c> - clamped and converted to pixels.
        /// </summary>
        public static RainbowChapterBossData Load(string designPath)
        {
            RainbowChapterBossData data = Res.LoadJson<RainbowChapterBossData>(designPath);
            if (data == null)
            {
                return null;
            }

            data.OnValidate();
            data.ScaleToPixels();
            return data;
        }
    }
}
