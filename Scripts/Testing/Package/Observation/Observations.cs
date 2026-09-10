using System;

namespace UnityTestAgent.Observation
{
    [Serializable]
    public struct Vector2Observation
    {
        public float X;
        public float Y;

        public Vector2Observation(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    [Serializable]
    public sealed class PlayerObservation
    {
        public Vector2Observation Position;
        public Vector2Observation Velocity;
        public float FacingDirection = 1f;
        public int HitPoints;
        public int MaxHitPoints;
        public float Stamina;
        public float MaxStamina;
        public bool IsAlive = true;
        public bool IsGrounded;
        public bool IsInvulnerable;
        public string MovementState = "";
        public string CombatState = "";
        public string CheckpointId = "";
    }

    [Serializable]
    public sealed class EnemyObservation
    {
        public string Id = "";
        public Vector2Observation Position;
        public float FacingDirection = 1f;
        public int HitPoints;
        public bool IsAlive = true;
        public bool IsAttacking;
        public string TelegraphState = "";
    }

    [Serializable]
    public sealed class BossObservation
    {
        public string Id = "";
        public int Phase;
        public int HitPoints;
        public bool IsAlive = true;
        public string CurrentPattern = "";
        public string TelegraphState = "";
    }

    [Serializable]
    public sealed class HazardObservation
    {
        public string Id = "";
        public Vector2Observation Position;
        public string HazardType = "";
        public bool IsActive = true;
    }

    [Serializable]
    public sealed class ProjectileObservation
    {
        public string Id = "";
        public Vector2Observation Position;
        public Vector2Observation Velocity;
        public bool IsHostile = true;
    }

    [Serializable]
    public sealed class InteractableObservation
    {
        public string Id = "";
        public Vector2Observation Position;
        public string InteractableType = "";
        public bool CanInteract;
    }

    [Serializable]
    public sealed class UiObservation
    {
        public bool IsMenuOpen;
        public bool IsDialogueOpen;
        public string ActivePanel = "";
    }

    [Serializable]
    public sealed class SceneObservation
    {
        public string SceneName = "";
        public string RoomId = "";
        public bool IsLoading;
    }
}
