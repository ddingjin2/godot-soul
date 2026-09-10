using System;
using System.Collections.Generic;

namespace UnityTestAgent.Observation
{
    [Serializable]
    public sealed class AgentObservation
    {
        public int FrameIndex;
        public float ElapsedSeconds;
        public PlayerObservation Player = new PlayerObservation();
        public SceneObservation Scene = new SceneObservation();
        public List<EnemyObservation> Enemies = new List<EnemyObservation>();
        public List<BossObservation> Bosses = new List<BossObservation>();
        public List<HazardObservation> Hazards = new List<HazardObservation>();
        public List<ProjectileObservation> Projectiles = new List<ProjectileObservation>();
        public List<InteractableObservation> Interactables = new List<InteractableObservation>();
        public UiObservation Ui = new UiObservation();

        public static AgentObservation Empty(int frameIndex)
        {
            return new AgentObservation
            {
                FrameIndex = frameIndex
            };
        }

        public AgentObservation WithEnemy(EnemyObservation enemy)
        {
            Enemies.Add(enemy);
            return this;
        }
    }
}
