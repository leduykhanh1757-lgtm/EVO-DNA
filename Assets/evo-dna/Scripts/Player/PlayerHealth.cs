using UnityEngine;
using UnityEngine.InputSystem;
using EvoDNA.Systems;
using EvoDNA.UI;

namespace EvoDNA.Player
{
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Health & Healing (GDD Rules)")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;
        [SerializeField] private int medkitCount = 2;
        [SerializeField] private float healAmount = 45f;
        [SerializeField] private float combatCooldownBeforeHeal = 3.0f;

        [Header("UI Blood Overlay (Khi bị Quái quẹt trúng)")]
        [SerializeField] private Texture2D bloodOverlayTexture;
        [SerializeField] private float bloodMaxAlpha = 0.88f;
        [SerializeField] private float bloodFadeSpeed = 0.65f;

        [Header("Checkpoint")]
        [SerializeField] private Vector3 checkpointPosition;

        private float lastAttackedTime = -999f;
        private float currentBloodAlpha;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public int MedkitCount => medkitCount;
        public bool IsUnderAttack => (Time.time - lastAttackedTime) < combatCooldownBeforeHeal;

        private void Start()
        {
            checkpointPosition = transform.position;
            GameEvents.RaisePlayerHealthChanged(currentHealth, maxHealth);
        }

        private void Update()
        {
            if (currentBloodAlpha > 0f)
            {
                currentBloodAlpha = Mathf.MoveTowards(currentBloodAlpha, 0f, Time.deltaTime * bloodFadeSpeed);
                GameHUDManager.Instance.SetBloodOverlay(bloodOverlayTexture, currentBloodAlpha);
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            // Phím H: Sử dụng nhanh vật phẩm hồi máu
            if (kb.hKey.wasPressedThisFrame)
            {
                TryHeal();
            }
        }

        public void TakeDamage(float damage)
        {
            if (currentHealth <= 0f)
            {
                return;
            }

            lastAttackedTime = Time.time;
            currentHealth = Mathf.Max(0f, currentHealth - damage);

            currentBloodAlpha = bloodMaxAlpha;
            GameHUDManager.Instance.SetBloodOverlay(bloodOverlayTexture, currentBloodAlpha);

            GameEvents.RaisePlayerHealthChanged(currentHealth, maxHealth);

            if (currentHealth <= 0f)
            {
                HandleDeath();
            }
        }

        public bool TryHeal()
        {
            if (currentHealth >= maxHealth || IsUnderAttack || medkitCount <= 0)
            {
                return false;
            }

            medkitCount--;
            currentHealth = Mathf.Min(maxHealth, currentHealth + healAmount);
            currentBloodAlpha = 0f;
            GameHUDManager.Instance.SetBloodOverlay(bloodOverlayTexture, 0f);
            GameHUDManager.Instance.ShowNotification($"Đã dùng Túi máu! (HP: {Mathf.RoundToInt(currentHealth)}/{Mathf.RoundToInt(maxHealth)})");
            GameEvents.RaisePlayerHealthChanged(currentHealth, maxHealth);
            return true;
        }

        public void SetBloodTexture(Texture2D tex)
        {
            bloodOverlayTexture = tex;
        }

        public void AddMedkit(int count = 1)
        {
            medkitCount += count;
        }

        private void HandleDeath()
        {
            Time.timeScale = 1f;
            medkitCount = Mathf.Min(medkitCount, 1);
            currentHealth = maxHealth;
            currentBloodAlpha = 0f;
            GameHUDManager.Instance.SetBloodOverlay(bloodOverlayTexture, 0f);

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = checkpointPosition;
            if (cc != null) cc.enabled = true;

            GameEvents.RaisePlayerHealthChanged(currentHealth, maxHealth);
        }
    }
}
