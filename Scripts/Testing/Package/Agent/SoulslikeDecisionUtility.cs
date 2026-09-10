using System;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;

namespace UnityTestAgent.Agent
{
    public static class SoulslikeDecisionUtility
    {
        public static float Distance(Vector2Observation a, Vector2Observation b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static float DangerScore(AgentObservation observation)
        {
            var score = 0f;
            for (var i = 0; i < observation.Hazards.Count; i++)
            {
                var hazard = observation.Hazards[i];
                if (hazard.IsActive)
                {
                    score += ScoreDistance(Distance(observation.Player.Position, hazard.Position), 3f);
                }
            }

            for (var i = 0; i < observation.Projectiles.Count; i++)
            {
                var projectile = observation.Projectiles[i];
                if (projectile.IsHostile)
                {
                    score += ScoreDistance(Distance(observation.Player.Position, projectile.Position), 4f);
                }
            }

            for (var i = 0; i < observation.Enemies.Count; i++)
            {
                var enemy = observation.Enemies[i];
                if (enemy.IsAlive && enemy.IsAttacking)
                {
                    score += ScoreDistance(Distance(observation.Player.Position, enemy.Position), 2f);
                }
            }

            return score;
        }

        public static AgentAction FilterByStamina(AgentObservation observation, AgentAction action)
        {
            var required = RequiredStamina(action);
            return observation.Player.Stamina >= required ? action : AgentAction.Idle();
        }

        public static bool ShouldHeal(AgentObservation observation, int minHitPoints, float minSafeDistance)
        {
            if (observation.Player.HitPoints > minHitPoints)
            {
                return false;
            }

            for (var i = 0; i < observation.Enemies.Count; i++)
            {
                if (observation.Enemies[i].IsAlive
                    && Distance(observation.Player.Position, observation.Enemies[i].Position) < minSafeDistance)
                {
                    return false;
                }
            }

            return DangerScore(observation) < 2f;
        }

        private static float RequiredStamina(AgentAction action)
        {
            switch (action.Type)
            {
                case AgentActionType.Dodge:
                    return 20f;
                case AgentActionType.LightAttack:
                    return 10f;
                case AgentActionType.HeavyAttack:
                    return 25f;
                case AgentActionType.Guard:
                case AgentActionType.Parry:
                    return 5f;
                default:
                    return 0f;
            }
        }

        private static float ScoreDistance(float distance, float radius)
        {
            if (distance >= radius)
            {
                return 0f;
            }

            return 1f + (radius - distance) / radius;
        }
    }
}
