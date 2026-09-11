using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// One boss attack, start to finish: how long it is read for, how long it can hit, how long the
    /// punish window after it lasts, and what it does on contact.
    ///
    /// This is the unit a chapter boss is authored out of. <see cref="RainbowChapterBossBehaviour"/>
    /// runs a profile without knowing what it represents, so a new attack is a row in
    /// <see cref="RainbowChapterBossData"/> rather than a new state in a boss class.
    ///
    /// The backing fields were <c>[SerializeField] private</c> in Unity, where the editor could reach
    /// them anyway. They are public here because the rows are now read straight out of the design JSON
    /// and <c>System.Text.Json</c> only binds public members. The read-only properties below stay the
    /// way the boss loop talks to a row.
    ///
    /// No field below carries an initialiser any more (K5, decision D1): a row is what the design file
    /// says and nothing else, so a key left out of a row reads as zero rather than as a number this
    /// class made up. The shipped rows all spell out every value that used to have one; the six that
    /// never did - the lunge pair, the feint pause and the three flags - are still legitimately absent
    /// from a row that does not use them.
    ///
    /// UNITS: <see cref="ScaleToPixels"/> scales <c>knockback</c>, <c>range</c>, <c>forwardOffset</c>,
    /// <c>lungeSpeed</c>, <c>hazardRadius</c> and <c>hazardForwardOffset</c>. Damage, every time in
    /// seconds, the weights and the colours are untouched.
    /// </summary>
    public sealed partial class BossAttackProfile : Resource
    {
        [Export] public string attackId;
        [Export] public string displayName;
        [Export] public float damage;
        [Export] public float knockback;
        [Export] public float telegraphTime;
        [Export] public float activeTime;
        [Export] public float recoveryTime;
        [Export] public float range;
        [Export] public Color telegraphColor;

        /// <summary>
        /// Standard is parryable and light on poise; Heavy is the committed one. Chosen per attack
        /// because a boss whose every swing is Heavy has no wrong answer for the player to find.
        /// </summary>
        [Export] public DamageType damageType;

        /// <summary>
        /// Relative pick weight once the boss is in phase two. Weights are summed and normalised, so any
        /// scale works. Phase one rotates in array order instead, which is what makes the opening
        /// learnable.
        /// </summary>
        [Export] public float phaseTwoWeight;

        /// <summary>
        /// Hit test is centred this far ahead of the boss, in facing units. Zero centres it on the body,
        /// which is what a slam wants.
        /// </summary>
        [Export] public float forwardOffset;

        /// <summary>
        /// How fast the boss drives forward through its own active window, and for how long. Zero is no
        /// lunge and is what every attack authored before 2026-08-18 gets, so nothing already shipped
        /// moves - but chapter two has an attack literally called `rolling_lunge` that stood still,
        /// because until now the loop had no way to make a swing travel.
        /// </summary>
        [Export] public float lungeSpeed;

        [Export] public float lungeDuration;

        /// <summary>
        /// Which stance this attack belongs to, or empty for an attack the boss can always reach. A
        /// stance is how the final chapters claim the earlier ones: the boss stands in one colour's
        /// shape at a time and only its rows are available.
        /// </summary>
        [Export] public string stanceId;

        // Feint
        /// <summary>
        /// A pause between the telegraph and the active window where the boss looks like it already
        /// finished - resting colour, no pulse - before the attack snaps out. Zero is an honest attack.
        /// This is what makes a tell readable as timing rather than as colour: the flash ended and the
        /// hit has not landed yet.
        /// </summary>
        [Export] public float feintPauseTime;

        // Chain
        /// <summary>
        /// attackId of the row this one leads into, or empty. The chain is what turns single swings into
        /// a wave the player has to learn the end of; an id that names nothing just ends the chain.
        /// </summary>
        [Export] public string chainNextAttackId;

        /// <summary>
        /// Beat between this attack finishing and the chained one starting. It replaces the recovery for
        /// that gap, so a short delay is a chain the player cannot punish in the middle of.
        /// </summary>
        [Export] public float chainDelay;

        /// <summary>
        /// Phase two multiplies the chain delay by this. Above 1 is the late beat - the chain the player
        /// learned now arrives after they have already dodged.
        /// </summary>
        [Export] public float chainDelayPhaseTwoScale;

        /// <summary>
        /// Copies this row raises, overriding the boss's own count. Negative leaves the boss's number
        /// alone. A stance quoting the chapter of shadows needs its copies without giving them to the
        /// other six stances, and the boss-level count cannot say that.
        /// </summary>
        [Export] public int afterimageCountOverride;

        /// <summary>
        /// Only reachable as the end of a chain, never picked on its own. A finisher with no stance
        /// would otherwise be available in every stance at once, which turns the payoff of a combo into
        /// every other swing.
        /// </summary>
        [Export] public bool chainOnly;

        // Pull
        /// <summary>
        /// Drags the target toward the boss instead of away. The knockback number is unchanged; only the
        /// direction flips. Player knockback ignores a negative force outright, so a pull has to be a
        /// direction, not a sign.
        /// </summary>
        [Export] public bool pullsTarget;

        // Hazard
        /// <summary>
        /// Leaves a burning strip where the attack ended. Off by default, so every attack authored
        /// before this existed still behaves exactly as it did.
        /// </summary>
        [Export] public bool leavesHazard;

        /// <summary>
        /// Hold the strip back until phase two. On means the first half of the fight teaches the attack
        /// and the second half adds what it costs to stand near it.
        /// </summary>
        [Export] public bool hazardPhaseTwoOnly;

        /// <summary>
        /// Damage per tick. Chip, not a second swing: the strip is meant to move the player, not to kill
        /// one who is already committed.
        /// </summary>
        [Export] public float hazardDamage;

        [Export] public float hazardRadius;
        [Export] public float hazardTickInterval;

        /// <summary>
        /// How long the strip burns. Long enough to deny the ground for the next exchange is the point;
        /// long enough to stack several is an arena the player cannot re-enter.
        /// </summary>
        [Export] public float hazardDuration;

        /// <summary>
        /// Where the strip lands, in facing units ahead of the boss. Usually further out than the hit
        /// test, so the strip covers the ground the lunge crossed rather than the boss's own feet.
        /// </summary>
        [Export] public float hazardForwardOffset;

        [Export] public Color hazardColor;

        private bool _scaledToPixels;

        public string AttackId => attackId;
        public string DisplayName => displayName;
        public float Damage => damage;
        public float Knockback => knockback;
        public float TelegraphTime => telegraphTime;
        public float ActiveTime => activeTime;
        public float RecoveryTime => recoveryTime;
        public float Range => range;
        public Color TelegraphColor => telegraphColor;
        public DamageType DamageType => damageType;
        public float PhaseTwoWeight => Mathf.Max(0f, phaseTwoWeight);
        public float ForwardOffset => forwardOffset;
        public float LungeSpeed => Mathf.Max(0f, lungeSpeed);
        public float LungeDuration => Mathf.Max(0f, lungeDuration);
        public bool Lunges => LungeSpeed > 0f && LungeDuration > 0f;
        public string StanceId => stanceId;
        public float FeintPauseTime => Mathf.Max(0f, feintPauseTime);
        public bool HasFeint => FeintPauseTime > 0f;
        public string ChainNextAttackId => chainNextAttackId;
        public bool HasChain => !string.IsNullOrEmpty(chainNextAttackId);
        public bool ChainOnly => chainOnly;

        /// <summary>How many copies this attack raises, given the boss's own count as the fallback.</summary>
        public int AfterimageCountOr(int bossCount) =>
            afterimageCountOverride >= 0 ? afterimageCountOverride : bossCount;

        public bool PullsTarget => pullsTarget;

        /// <summary>How long after this attack the chained one starts, late by design in phase two.</summary>
        public float ChainDelayFor(bool isPhaseTwo)
        {
            float delay = Mathf.Max(0f, chainDelay);
            return isPhaseTwo ? delay * Mathf.Max(0f, chainDelayPhaseTwoScale) : delay;
        }

        public bool LeavesHazard => leavesHazard;
        public bool HazardPhaseTwoOnly => hazardPhaseTwoOnly;
        public float HazardDamage => Mathf.Max(0f, hazardDamage);
        public float HazardRadius => Mathf.Max(0.01f, hazardRadius);
        public float HazardTickInterval => Mathf.Max(0.05f, hazardTickInterval);
        public float HazardDuration => Mathf.Max(0f, hazardDuration);
        public float HazardForwardOffset => hazardForwardOffset;
        public Color HazardColor => hazardColor;

        /// <summary>
        /// Whether this attack should drop a strip right now. Asked at the end of the swing rather than
        /// at its start, so an attack interrupted mid-telegraph pays nothing - the strip is the reward
        /// for a commitment the boss actually finished.
        /// </summary>
        public bool ShouldLeaveHazard(bool isPhaseTwo)
        {
            if (!leavesHazard || HazardDuration <= 0f)
            {
                return false;
            }

            return !hazardPhaseTwoOnly || isPhaseTwo;
        }

        /// <summary>Metres -> pixels, once. Driven by <see cref="RainbowChapterBossData.Load"/>.</summary>
        public void ScaleToPixels()
        {
            if (_scaledToPixels)
            {
                return;
            }

            _scaledToPixels = true;

            knockback = World.U(knockback);
            range = World.U(range);
            forwardOffset = World.U(forwardOffset);
            lungeSpeed = World.U(lungeSpeed);
            hazardRadius = World.U(hazardRadius);
            hazardForwardOffset = World.U(hazardForwardOffset);
        }
    }
}
