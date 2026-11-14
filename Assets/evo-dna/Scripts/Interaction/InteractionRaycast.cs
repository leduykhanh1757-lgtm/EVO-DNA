using UnityEngine;
using UnityEngine.InputSystem;
using EvoDNA.UI;

namespace EvoDNA.Interaction
{
    public class InteractionRaycast : MonoBehaviour
    {
        [Header("Direct Line-of-Sight Raycast Settings")]
        [SerializeField] private float interactDistance = 2.3f;
        [SerializeField] private LayerMask interactLayer = ~0;
        [SerializeField] private Transform cameraTransform;

        private IInteractable currentInteractable;
        private DraggableProp activeCarriedProp;
        private HidingSpot activeHidingSpot;
        private string currentPrompt = string.Empty;
        private bool wasInteractTriggeredThisFrame;

        public bool IsCarryingProp => activeCarriedProp != null && activeCarriedProp.IsCarried;
        public bool IsPlayerHiding => activeHidingSpot != null && activeHidingSpot.IsPlayerInside;
        public bool WasInteractTriggeredThisFrame => wasInteractTriggeredThisFrame;

        private void Awake()
        {
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        private void Start()
        {
            // Khởi tạo sẵn Canvas HUD tập trung
            _ = GameHUDManager.Instance;
        }

        private void Update()
        {
            wasInteractTriggeredThisFrame = false;

            if (cameraTransform == null || DoorController.IsAnyDoorCapturingInput)
            {
                if (!string.IsNullOrEmpty(currentPrompt))
                {
                    currentPrompt = string.Empty;
                }
                return;
            }

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null)
            {
                return;
            }

            // 1. Nếu đang trốn trong điểm ẩn nấp -> bấm F để chui ra
            if (activeHidingSpot != null && activeHidingSpot.IsPlayerInside)
            {
                currentInteractable = activeHidingSpot;
                UpdatePromptUI(activeHidingSpot.GetInteractPrompt());
                if (kb.fKey.wasPressedThisFrame)
                {
                    wasInteractTriggeredThisFrame = true;
                    activeHidingSpot.Interact(gameObject);
                    activeHidingSpot = null;
                }
                return;
            }

            // 2. Nếu đang cầm đồ vật trên tay -> bấm F để đặt xuống, hoặc bấm G / Chuột Trái để NÉM
            if (activeCarriedProp != null && activeCarriedProp.IsCarried)
            {
                currentInteractable = activeCarriedProp;
                UpdatePromptUI(activeCarriedProp.GetInteractPrompt());

                if (kb.fKey.wasPressedThisFrame)
                {
                    wasInteractTriggeredThisFrame = true;
                    activeCarriedProp.DropProp();
                    activeCarriedProp = null;
                }
                else if (kb.gKey.wasPressedThisFrame || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                {
                    wasInteractTriggeredThisFrame = true;
                    activeCarriedProp.ThrowProp(cameraTransform.forward);
                    activeCarriedProp = null;
                }
                return;
            }

            // 3. Chỉ nhận diện vật thể khi tia nhìn trực tiếp chạm vào vật đó (Không quét xuyên tường / không dùng Bounds gộp)
            float scale = Mathf.Max(1f, transform.localScale.y);
            currentInteractable = FindDirectLineOfSightInteractable(scale);
            UpdatePromptUI(currentInteractable != null ? currentInteractable.GetInteractPrompt() : string.Empty);

            if (currentInteractable != null && kb.fKey.wasPressedThisFrame)
            {
                wasInteractTriggeredThisFrame = true;

                if (currentInteractable is HidingSpot hs)
                {
                    activeHidingSpot = hs;
                }
                else if (currentInteractable is DraggableProp dp)
                {
                    activeCarriedProp = dp;
                }

                currentInteractable.Interact(gameObject);
            }
        }

        private void UpdatePromptUI(string newPrompt)
        {
            currentPrompt = newPrompt;
            GameHUDManager.Instance.SetInteractPrompt(currentPrompt);
        }

        private IInteractable FindDirectLineOfSightInteractable(float scale)
        {
            float maxDist = interactDistance * scale;
            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

            // Tia 1: Raycast chính xác từ tâm ngắm (Nếu vướng tường/cửa thì Raycast sẽ chạm tường trước và dừng lại ngay!)
            if (Physics.Raycast(ray, out RaycastHit hit, maxDist, interactLayer, QueryTriggerInteraction.Ignore))
            {
                IInteractable direct = ExtractValidInteractable(hit);
                if (direct != null)
                {
                    return direct;
                }
            }

            // Tia 2: SphereCast mảnh (bán kính nhỏ 0.15m) hỗ trợ nhìn các đồ vật nhỏ trên sàn nhưng vẫn bị chặn 100% bởi tường
            if (Physics.SphereCast(ray, 0.15f * scale, out RaycastHit sphereHit, maxDist, interactLayer, QueryTriggerInteraction.Ignore))
            {
                Vector3 toHit = sphereHit.point - cameraTransform.position;
                float dist = toHit.magnitude;
                if (dist > 0.01f && Physics.Raycast(cameraTransform.position, toHit / dist, out RaycastHit losHit, dist - 0.05f * scale, interactLayer, QueryTriggerInteraction.Ignore))
                {
                    if (losHit.collider != sphereHit.collider)
                    {
                        return null;
                    }
                }

                return ExtractValidInteractable(sphereHit);
            }

            return null;
        }

        private IInteractable ExtractValidInteractable(RaycastHit hit)
        {
            if (hit.collider == null)
            {
                return null;
            }

            IInteractable candidate = hit.collider.GetComponentInParent<IInteractable>();
            if (candidate == null)
            {
                return null;
            }

            string p = candidate.GetInteractPrompt();
            if (string.IsNullOrEmpty(p))
            {
                return null;
            }

            if (candidate is HidingSpot hs)
            {
                hs.SetInteractionHitPoint(hit.point);
            }

            return candidate;
        }
    }
}
