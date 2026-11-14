using UnityEngine;
using EvoDNA.Systems;

namespace EvoDNA.Interaction
{
    public class PickupItem : MonoBehaviour, IInteractable
    {
        [Header("Item Info")]
        [SerializeField] private string itemId = "sample_vial";
        [SerializeField] private string displayName = "Ống Mẫu Vật DNA";
        [SerializeField] private ItemCategory category = ItemCategory.SampleVial;
        [SerializeField] private int amount = 1;

        public string GetInteractPrompt()
        {
            return $"[Click / F] Nhặt {displayName}";
        }

        public void Interact(GameObject interactor)
        {
            if (interactor == null)
            {
                return;
            }

            InventoryManager inv = interactor.GetComponent<InventoryManager>();
            if (inv == null)
            {
                inv = interactor.AddComponent<InventoryManager>();
            }

            inv.AddItem(itemId, displayName, category, amount);
            gameObject.SetActive(false);
        }
    }
}

