using System;
using Godot;

namespace MyGame.Enemy
{
    /// <summary>
    /// The two things a boss fight says to the rest of the game: it has begun, and it is over.
    ///
    /// It exists because the listeners live in the <c>Gameplay</c> namespace, which this one does not
    /// reference. A boss cannot call a director or a victory panel; it can only announce, and be held.
    /// Before this, both hooks were <see cref="WrathMiniBoss"/>'s concrete type, so an authored chapter
    /// boss could neither open the way chapter one does nor be worth beating.
    ///
    /// Unity's <c>UnityEvent</c> members are plain C# events here, which is why they are declared as
    /// <c>event</c> rather than as properties returning an event object.
    /// </summary>
    public interface IBossEncounter
    {
        /// <summary>
        /// Raised when the arrival begins - which is when a player is first known, not on ready. A
        /// listener that wants the boss to wait calls <see cref="HoldIntro"/> from inside this, before
        /// the raiser reads the flag.
        /// </summary>
        event Action IntroStarted;

        /// <summary>
        /// Raised once the death has been read on screen, not on the killing blow. The gap between the
        /// two is the beat the kill needs before a panel covers it, so this is the one the victory
        /// sequence hangs off.
        /// </summary>
        event Action Defeated;

        /// <summary>
        /// The boss's own node, for a director that needs to bind a camera role to it. Unity's
        /// <c>GameObject</c>; typed <see cref="Node2D"/> here because every caller of it wants the
        /// boss's position.
        /// </summary>
        Node2D BossObject { get; }

        /// <summary>
        /// What to call the thing that just fell. The victory panel used to say "Wrath has fallen" in
        /// every chapter, because the only boss that existed when it was written was Wrath's.
        /// </summary>
        string BossName { get; }

        bool IsInIntro { get; }

        void HoldIntro();

        void ReleaseIntroHold();
    }
}
