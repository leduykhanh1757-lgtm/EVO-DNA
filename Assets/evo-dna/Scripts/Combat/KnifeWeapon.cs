using UnityEngine;
using UnityEngine.InputSystem;
using EvoDNA.AI;
using EvoDNA.Interaction;
using EvoDNA.UI;

namespace EvoDNA.Combat
{
    public class KnifeWeapon : MonoBehaviour
    {
        [Header("Knife Rules (GDD: E to Draw, LMB to Stab, Max 3 Stabs Durability)")]
        [SerializeField] private int maxDurability = 3;
        [SerializeField] private int currentDurability = 3;
        [SerializeField] private float stabDamage = 35f;
        [SerializeField] private float stabRange = 2.6f;
        [SerializeField] private float stabCooldown = 0.55f;

        [Header("First-Person Viewmodel")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private GameObject knifePrefab;
        [SerializeField] private Vector3 hipLocalPos = new Vector3(0.24f, -0.19f, 0.42f);
        [SerializeField] private Vector3 hipLocalEuler = new Vector3(12f, 88f, -12f);
        [SerializeField] private Vector3 hipLocalScale = new Vector3(2.4f, 2.4f, 2.4f);

        private InteractionRaycast interactionRaycast;
        private HandgunWeapon handgunWeapon;
        private GameObject knifeInstance;

        private bool isKnifeEquipped;
        private float lastStabTime;
        private float stabAnimThrust;

        public bool IsKnifeEquipped => isKnifeEquipped && currentDurability > 0;
        public int CurrentDurability => currentDurability;
        public int MaxDurability => maxDurability;

        private void Awake()
        {
            interactionRaycast = GetComponent<InteractionRaycast>();
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            handgunWeapon = GetComponent<HandgunWeapon>();
            if (handgunWeapon == null)
            {
                handgunWeapon = gameObject.AddComponent<HandgunWeapon>();
            }

            EnsureKnifeViewmodel();
            SetEquipped(false);
        }

        private void EnsureKnifeViewmodel()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Transform existing = cameraTransform.Find("FPS_Knife");
            if (existing != null)
            {
                knifeInstance = existing.gameObject;
                return;
            }

#if UNITY_EDITOR
            if (knifePrefab == null)
            {
                knifePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/evo-dna/wepon/Knife/Model/Knife.prefab");
            }
#endif

            if (knifePrefab != null)
            {
                knifeInstance = Instantiate(knifePrefab, cameraTransform);
                knifeInstance.name = "FPS_Knife";
                knifeInstance.transform.localPosition = hipLocalPos;
                knifeInstance.transform.localEulerAngles = hipLocalEuler;
                knifeInstance.transform.localScale = hipLocalScale;

                Collider[] cols = knifeInstance.GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null)
                    {
                        cols[i].enabled = false;
                    }
                }
            }
        }

        public void SetEquipped(bool equippedState)
        {
            isKnifeEquipped = equippedState && currentDurability > 0;

            if (knifeInstance != null)
            {
                knifeInstance.SetActive(isKnifeEquipped);
            }

            if (isKnifeEquipped && handgunWeapon != null && handgunWeapon.IsEquipped)
            {
                handgunWeapon.SetEquipped(false);
            }

            RefreshWeaponHUD();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null)
            {
                return;
            }

            if (DoorController.IsPasscodePopupOpen ||
                (interactionRaycast != null && (interactionRaycast.IsCarryingProp || interactionRaycast.IsPlayerHiding)))
            {
                return;
            }

            // Phím E: Rút / Cất dao
            if (kb.eKey.wasPressedThisFrame)
            {
                if (currentDurability > 0)
                {
                    SetEquipped(!isKnifeEquipped);
                }
                else
                {
                    GameHUDManager.Instance.ShowNotification("Dao đã gãy sau 3 lần đâm! Hãy tìm con dao khác.");
                }
            }

            if (!IsKnifeEquipped)
            {
                return;
            }

            // Chuột trái: Đâm dao
            if (mouse.leftButton.wasPressedThisFrame && Time.time - lastStabTime >= stabCooldown)
            {
                PerformStab();
            }

            UpdateKnifeViewmodel();
        }

        private void PerformStab()
        {
            lastStabTime = Time.time;
            stabAnimThrust = 1f;

            if (cameraTransform == null)
            {
                return;
            }

            float scale = Mathf.Max(1f, transform.localScale.y);
            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

            if (Physics.SphereCast(ray, 0.35f * scale, out RaycastHit hit, stabRange * scale, ~0, QueryTriggerInteraction.Ignore))
            {
                MonsterAI monster = hit.collider.GetComponentInParent<MonsterAI>();
                if (monster != null && !monster.IsDead)
                {
                    monster.TakeDamage(stabDamage);
                    currentDurability--;
                    RefreshWeaponHUD();
                    GameHUDManager.Instance.ShowNotification($"Đâm trúng quái! (-{stabDamage} HP | Độ bền: {currentDurability}/{maxDurability})");

                    if (currentDurability <= 0)
                    {
                        SetEquipped(false);
                        GameHUDManager.Instance.ShowNotification("Con dao đã gãy sau 3 lần đâm!");
                    }
                }
            }
        }

        private void RefreshWeaponHUD()
        {
            if (!IsKnifeEquipped)
            {
                if (handgunWeapon == null || !handgunWeapon.IsEquipped)
                {
                    GameHUDManager.Instance.SetWeaponHUD(false, string.Empty);
                }
                return;
            }

            GameHUDManager.Instance.SetWeaponHUD(true, $"Dao sinh tồn: Độ bền {currentDurability}/{maxDurability} lần đâm  ([Chuột Trái] Đâm | [E] Cất)");
        }

        private void UpdateKnifeViewmodel()
        {
            if (knifeInstance == null)
            {
                return;
            }

            stabAnimThrust = Mathf.MoveTowards(stabAnimThrust, 0f, Time.deltaTime * 5.5f);
            float thrustCurve = Mathf.Sin(stabAnimThrust * Mathf.PI);

            Vector3 targetPos = hipLocalPos + new Vector3(-0.06f * thrustCurve, 0.03f * thrustCurve, 0.24f * thrustCurve);
            Vector3 targetEuler = hipLocalEuler + new Vector3(-18f * thrustCurve, -10f * thrustCurve, 0f);

            knifeInstance.transform.localPosition = Vector3.Lerp(knifeInstance.transform.localPosition, targetPos, Time.deltaTime * 22f);
            knifeInstance.transform.localRotation = Quaternion.Slerp(knifeInstance.transform.localRotation, Quaternion.Euler(targetEuler), Time.deltaTime * 22f);
        }

        public void PickupNewKnife()
        {
            currentDurability = maxDurability;
            SetEquipped(true);
            GameHUDManager.Instance.ShowNotification($"Đã nhặt Dao mới! (Độ bền {currentDurability}/{maxDurability})");
        }
    }
}
