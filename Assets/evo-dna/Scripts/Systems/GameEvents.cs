using System;
using UnityEngine;

namespace EvoDNA.Systems
{
    public enum MonsterAbilityType
    {
        None = 0,
        CrawlerDash = 1,
        InfiltratorCloak = 2
    }

    public static class GameEvents
    {
        public static event Action<Vector3, float> OnNoiseEmitted;
        public static event Action<bool> OnPlayerHiddenChanged;
        public static event Action<MonsterAbilityType> OnAbilityCopied;
        public static event Action<bool, Transform, float, float> OnUVLightUpdated;
        public static event Action<float, float> OnPlayerHealthChanged;

        public static void RaiseNoiseEmitted(Vector3 position, float radius)
        {
            OnNoiseEmitted?.Invoke(position, radius);
        }

        public static void RaisePlayerHiddenChanged(bool isHidden)
        {
            OnPlayerHiddenChanged?.Invoke(isHidden);
        }

        public static void RaiseAbilityCopied(MonsterAbilityType ability)
        {
            OnAbilityCopied?.Invoke(ability);
        }

        public static void RaiseUVLightUpdated(bool isUVActive, Transform lightTransform, float range, float spotAngle)
        {
            OnUVLightUpdated?.Invoke(isUVActive, lightTransform, range, spotAngle);
        }

        public static void RaisePlayerHealthChanged(float currentHealth, float maxHealth)
        {
            OnPlayerHealthChanged?.Invoke(currentHealth, maxHealth);
        }
    }
}

