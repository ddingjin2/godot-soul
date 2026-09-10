using System;
using Godot;

namespace MyGame.Combat
{
    /// <summary>
    /// Soul currency. Every actor carries one wallet and it means the same thing on both sides of a
    /// fight: what killing this actor is worth. An enemy is seeded with its reward, the player banks
    /// what it takes, and a kill moves the whole balance across. One concept, so there is no separate
    /// "drop table" to keep in sync with the wallet.
    ///
    /// Losing the balance on death and leaving it where you fell is handled by the Gameplay layer,
    /// which owns the pickup that has to exist in the world.
    /// </summary>
    public partial class SoulsWallet : Node
    {
        [Export] private int souls;

        public event Action<int> OnSoulsChanged;

        public int Souls => souls;

        public void SetSouls(int value)
        {
            souls = Mathf.Max(0, value);
            OnSoulsChanged?.Invoke(souls);
        }

        public void Add(int amount)
        {
            if (amount <= 0)
                return;

            souls += amount;
            OnSoulsChanged?.Invoke(souls);
        }

        public bool TrySpend(int amount)
        {
            if (amount <= 0 || souls < amount)
                return false;

            souls -= amount;
            OnSoulsChanged?.Invoke(souls);
            return true;
        }

        /// <summary>Empties the wallet and returns what it held.</summary>
        public int Drain()
        {
            int held = souls;
            if (held <= 0)
                return 0;

            souls = 0;
            OnSoulsChanged?.Invoke(souls);
            return held;
        }
    }
}
