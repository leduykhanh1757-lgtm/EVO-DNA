using UnityEngine;
using UnityEngine.InputSystem;
using EvoDNA.AI;
using EvoDNA.Interaction;
using EvoDNA.Systems;
using EvoDNA.UI;

namespace EvoDNA.Combat
{
    public class HandgunWeapon : MonoBehaviour
    {
        [Header("Handgun Rules (GDD: Limited Ammo, R Equip, LMB Shoot, RMB Reload)")]
        [SerializeField] private int clipCapacity = 7;
        [SerializeField] private int currentClipAmmo = 7;
        [SerializeField] private int reserveAmmo = 14;
        [SerializeField] private float bulletDamage = 35f;
        [SerializeField] private float fireRange = 35f;
        [SerializeField] private float fireCooldown = 0.28f;
        [SerializeField] private float reloadDuration = 1.15f;

        [Header("First-Person Viewmodel")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private GameObject handgunPrefab;
        [SerializeField] private Vector3 hipLocalPos = new Vector3(0.24f, -0.22f, 0.46f);
        [SerializeField] private Vector3 hipLocalEuler = new Vector3(0f, 0f, 0f);
        [SerializeField] private Vector3 hipLocalScale = new Vector3(1.1f, 1.1f, 1.1f);

        private InteractionRaycast interactionRaycast;
        private KnifeWeapon knifeWeapon;
        private GameObject handgunInstance;
        private SimpleShoot simpleShoot;

        private bool isEquipped;
        private bool isReloading;
        private float lastFireTime;
        private float reloadTimer;
        private float recoilOffset;

        public bool IsEquipped => isEquipped;
        public int CurrentClipAmmo => currentClipAmmo;
        public int ReserveAmmo => reserveAmmo;

        private void Awake()
        {
            interactionRaycast = GetComponent<InteractionRaycast>();
            knifeWeapon = GetComponent<KnifeWeapon>();

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (cameraTransform != null)
            {
                Camera cam = cameraTransform.GetComponent<Camera>();
                if (cam != null && cam.nearClipPlane > 0.05f)
                {
                    cam.nearClipPlane = 0.05f;
                }
            }

            EnsureHandgunViewmodel();
            SetEquipped(false);
        }

        private void EnsureHandgunViewmodel()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Transform existing = cameraTransform.Find("FPS_Handgun_M1911");
            if (existing != null)
            {
                handgunInstance = existing.gameObject;
                simpleShoot = handgunInstance.GetComponentInChildren<SimpleShoot>();
                return;
            }

#if UNITY_EDITOR
            if (handgunPrefab == null)
            {
                handgunPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/evo-dna/wepon/Modern Guns - Handgun/_Prefabs/Handgun Black/M1911 Handgun_Black (Shooting).prefab");
            }
#endif

            if (handgunPrefab != null)
            {
                handgunInstance = Instantiate(handgunPrefab, cameraTransform);
                handgunInstance.name = "FPS_Handgun_M1911";
                handgunInstance.transform.localPosition = hipLocalPos;
                handgunInstance.transform.localEulerAngles = hipLocalEuler;
                handgunInstance.transform.localScale = hipLocalScale;

                Collider[] cols = handgunInstance.GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null)
                    {
                        cols[i].enabled = false;
                    }
                }

                simpleShoot = handgunInstance.GetComponentInChildren<SimpleShoot>();
            }
        }

        public void SetEquipped(bool equippedState)
        {
            isEquipped = equippedState;
            if (!isEquipped)
            {
                isReloading = false;
            }

            if (handgunInstance != null)
            {
                handgunInstance.SetActive(isEquipped);
            }

            if (isEquipped && knifeWeapon != null && knifeWeapon.IsKnifeEquipped)
            {
                knifeWeapon.SetEquipped(false);
            }

            RefreshWeaponHUD();
        }

        public void AddAmmo(int amount)
        {
            reserveAmmo += Mathf.Max(0, amount);
            GameHUDManager.Instance.ShowNotification($"+{amount} Đạn súng lục (Dự trữ: {reserveAmmo})");
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

            // Phím R: Trang bị / Cất súng
            if (kb.rKey.wasPressedThisFrame)
            {
                SetEquipped(!isEquipped);
                return;
            }

            if (!isEquipped)
            {
                return;
            }

            if (isReloading)
            {
                reloadTimer -= Time.deltaTime;
                if (reloadTimer <= 0f)
                {
                    FinishReload();
                }
                UpdateViewmodelTransform();
                return;
            }

            // Chuột phải (Right Click): Nạp đạn
            if (mouse.rightButton.wasPressedThisFrame)
            {
                TryStartReload();
                UpdateViewmodelTransform();
                return;
            }

            // Chuột trái (Left Click): Bắn súng
            if (mouse.leftButton.wasPressedThisFrame && Time.time - lastFireTime >= fireCooldown)
            {
                TryFire();
            }

            UpdateViewmodelTransform();
        }

        private void TryFire()
        {
            if (currentClipAmmo <= 0)
            {
                if (reserveAmmo > 0)
                {
                    GameHUDManager.Instance.ShowNotification("Hết đạn trong băng! Bấm [Chuột Phải] để nạp đạn");
                }
                else
                {
                    GameHUDManager.Instance.ShowNotification("Đã cạn sạch đạn súng lục!");
                }
                return;
            }

            lastFireTime = Time.time;
            currentClipAmmo--;
            recoilOffset = 1f;
            RefreshWeaponHUD();

            float scale = Mathf.Max(1f, transform.localScale.y);

            // 1. Hiệu ứng giật slide, lửa đầu nòng, văng vỏ đạn (Object Pooled)
            if (simpleShoot != null)
            {
                simpleShoot.TriggerFireEffect(scale);
            }

            // 2. Tia bắn chính xác từ tâm ngắm Camera (Hitscan duy nhất chống Double Damage)
            if (cameraTransform != null)
            {
                Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
                if (Physics.SphereCast(ray, 0.2f * scale, out RaycastHit hit, fireRange * scale, ~0, QueryTriggerInteraction.Ignore))
                {
                    MonsterAI monster = hit.collider.GetComponentInParent<MonsterAI>();
                    if (monster != null && !monster.IsDead)
                    {
                        monster.TakeDamage(bulletDamage);
                        GameHUDManager.Instance.ShowNotification($"Bắn trúng quái! (-{bulletDamage} HP)");
                    }
                }
            }

            // 3. Tiếng súng nổ lớn thu hút quái vật
            GameEvents.RaiseNoiseEmitted(transform.position, 18f * scale);
        }

        private void TryStartReload()
        {
            if (currentClipAmmo >= clipCapacity)
            {
                GameHUDManager.Instance.ShowNotification("Băng đạn đã đầy!");
                return;
            }

            if (reserveAmmo <= 0)
            {
                GameHUDManager.Instance.ShowNotification("Không còn đạn dự trữ để nạp!");
                return;
            }

            isReloading = true;
            reloadTimer = reloadDuration;
            RefreshWeaponHUD();
        }

        private void FinishReload()
        {
            isReloading = false;
            int needed = clipCapacity - currentClipAmmo;
            int toLoad = Mathf.Min(needed, reserveAmmo);
            currentClipAmmo += toLoad;
            reserveAmmo -= toLoad;
            RefreshWeaponHUD();
            GameHUDManager.Instance.ShowNotification($"Đã nạp đạn! ({currentClipAmmo}/{reserveAmmo})");
        }

        private void RefreshWeaponHUD()
        {
            if (!isEquipped)
            {
                if (knifeWeapon == null || !knifeWeapon.IsKnifeEquipped)
                {
                    GameHUDManager.Instance.SetWeaponHUD(false, string.Empty);
                }
                return;
            }

            string reloadState = isReloading ? "ĐANG NẠP ĐẠN..." : "[Chuột Trái] Bắn | [Chuột Phải] Nạp | [R] Cất";
            GameHUDManager.Instance.SetWeaponHUD(true, $"Súng M1911: {currentClipAmmo}/{reserveAmmo} viên  ({reloadState})");
        }

        private void UpdateViewmodelTransform()
        {
            if (handgunInstance == null)
            {
                return;
            }

            recoilOffset = Mathf.MoveTowards(recoilOffset, 0f, Time.deltaTime * 6.5f);

            Vector3 targetPos = hipLocalPos + new Vector3(0f, recoilOffset * 0.025f, -recoilOffset * 0.06f);
            Vector3 targetEuler = hipLocalEuler + new Vector3(-recoilOffset * 14f, 0f, 0f);

            if (isReloading)
            {
                float reloadWave = Mathf.Sin((1f - (reloadTimer / reloadDuration)) * Mathf.PI);
                targetPos += new Vector3(-0.05f * reloadWave, -0.08f * reloadWave, 0f);
                targetEuler += new Vector3(25f * reloadWave, 0f, 32f * reloadWave);
            }

            handgunInstance.transform.localPosition = Vector3.Lerp(handgunInstance.transform.localPosition, targetPos, Time.deltaTime * 18f);
            handgunInstance.transform.localRotation = Quaternion.Slerp(handgunInstance.transform.localRotation, Quaternion.Euler(targetEuler), Time.deltaTime * 18f);
        }
    }
}
