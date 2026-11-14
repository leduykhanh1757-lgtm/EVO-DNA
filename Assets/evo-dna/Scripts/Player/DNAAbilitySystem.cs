using UnityEngine;
using UnityEngine.InputSystem;
using EvoDNA.Systems;

namespace EvoDNA.Player
{
    [RequireComponent(typeof(FirstPersonController))]
    public class DNAAbilitySystem : MonoBehaviour
    {
        [Header("Current Copied DNA Ability (Only 1 Slot)")]
        [SerializeField] private MonsterAbilityType currentAbility = MonsterAbilityType.None;

        [Header("Crawler Ability: Dash")]
        [SerializeField] private float dashSpeedMultiplier = 2.6f;
        [SerializeField] private float dashDuration = 0.55f;
        [SerializeField] private float dashCooldown = 4.0f;

        [Header("Infiltrator Ability: Cloak (Invisibility)")]
        [SerializeField] private float cloakDuration = 4.0f;
        [SerializeField] private float cloakCooldown = 8.0f;

        private FirstPersonController fpController;
        private float nextReadyTime;
        private bool isCloaked;
        private float cloakEndTime;

        public MonsterAbilityType CurrentAbility => currentAbility;
        public bool IsCloaked => isCloaked;
        public float CooldownRemaining => Mathf.Max(0f, nextReadyTime - Time.time);

        private void Awake()
        {
            fpController = GetComponent<FirstPersonController>();
        }

        private void Update()
        {
            if (isCloaked && Time.time >= cloakEndTime)
            {
                isCloaked = false;
                if (!fpController.IsHidden)
                {
                    GameEvents.RaisePlayerHiddenChanged(false);
                }
            }

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame)
            {
                TryUseAbility();
            }
        }

        public void CopyAbility(MonsterAbilityType newAbility)
        {
            // Ràng buộc GDD: Chỉ lưu 1 năng lực, sao chép mới sẽ ghi đè và mất năng lực cũ
            currentAbility = newAbility;
            nextReadyTime = 0f;
            GameEvents.RaiseAbilityCopied(currentAbility);
            Debug.Log($"[DNA System] Đã hấp thụ năng lực mới: {currentAbility} (Năng lực cũ đã bị ghi đè).");
        }

        public void TryUseAbility()
        {
            if (currentAbility == MonsterAbilityType.None || Time.time < nextReadyTime)
            {
                return;
            }

            if (currentAbility == MonsterAbilityType.CrawlerDash)
            {
                fpController.TriggerDash(dashSpeedMultiplier, dashDuration);
                nextReadyTime = Time.time + dashCooldown;
            }
            else if (currentAbility == MonsterAbilityType.InfiltratorCloak)
            {
                isCloaked = true;
                cloakEndTime = Time.time + cloakDuration;
                nextReadyTime = Time.time + cloakCooldown;
                GameEvents.RaisePlayerHiddenChanged(true);
            }
        }
    }
}

