using System.Collections.Generic;
using UnityEngine;
using EvoDNA.Player;
using EvoDNA.Combat;

namespace EvoDNA.Systems
{
    public enum ItemCategory
    {
        Medkit = 0,
        Knife = 1,
        Keycard = 2,
        Fuse = 3,
        SampleVial = 4
    }

    [System.Serializable]
    public struct InventoryEntry
    {
        public string itemId;
        public string displayName;
        public ItemCategory category;
        public int count;
    }

    public class InventoryManager : MonoBehaviour
    {
        [Header("Collected Items in Bag")]
        [SerializeField] private List<InventoryEntry> items = new List<InventoryEntry>();

        private List<InventoryEntry> savedCheckpointItems = new List<InventoryEntry>();
        private PlayerHealth playerHealth;
        private KnifeWeapon knifeWeapon;

        public IReadOnlyList<InventoryEntry> Items => items;

        private void Awake()
        {
            playerHealth = GetComponent<PlayerHealth>();
            knifeWeapon = GetComponent<KnifeWeapon>();
        }

        public void AddItem(string itemId, string displayName, ItemCategory category, int amount = 1)
        {
            if (category == ItemCategory.Medkit && playerHealth != null)
            {
                playerHealth.AddMedkit(amount);
            }
            else if (category == ItemCategory.Knife && knifeWeapon != null)
            {
                knifeWeapon.PickupNewKnife();
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].itemId == itemId)
                {
                    InventoryEntry updated = items[i];
                    updated.count += amount;
                    items[i] = updated;
                    Debug.Log($"[Túi đồ] Đã cất thêm '{displayName}' (Tổng: {updated.count})");
                    return;
                }
            }

            items.Add(new InventoryEntry
            {
                itemId = itemId,
                displayName = displayName,
                category = category,
                count = amount
            });
            Debug.Log($"[Túi đồ] Đã thu thập '{displayName}' vào túi đồ!");
        }

        public bool HasItem(string itemKey)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].count > 0 &&
                    (string.Equals(items[i].itemId, itemKey, System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(items[i].displayName, itemKey, System.StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }

        public void SaveInventoryAtCheckpoint()
        {
            savedCheckpointItems = new List<InventoryEntry>(items);
        }

        public void RestoreInventoryOnDeath()
        {
            // Luật GDD: Khi tử vong mất toàn bộ vật phẩm nhặt được tính từ lần Save gần nhất
            items = new List<InventoryEntry>(savedCheckpointItems);
        }
    }
}

