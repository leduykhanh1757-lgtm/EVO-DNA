using UnityEngine;
using UnityEngine.InputSystem;
using EvoDNA.Systems;
using EvoDNA.UI;

namespace EvoDNA.Player
{
    public class FlashlightController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Light spotLight;

        [Header("Light Settings (Base 1m Scale)")]
        [SerializeField] private Color normalLightColor = new Color(1.0f, 0.98f, 0.9f, 1f);
        [SerializeField] private Color uvLightColor = new Color(0.58f, 0.18f, 1.0f, 1f);
        [SerializeField] private float normalIntensity = 4.5f;
        [SerializeField] private float uvIntensity = 3.5f;
        [SerializeField] private float baseRange = 24f;

        private bool isFlashlightOn = true;
        private bool isUVMode;

        public bool IsFlashlightOn => isFlashlightOn;
        public bool IsUVMode => isUVMode;

        private void Awake()
        {
            EnsureSpotLight();
            ApplyLightState();
        }

        private void EnsureSpotLight()
        {
            if (spotLight == null)
            {
                spotLight = GetComponentInChildren<Light>(true);
            }

            if (spotLight == null && Camera.main != null)
            {
                Transform existing = Camera.main.transform.Find("Flashlight_SpotLight");
                if (existing != null)
                {
                    spotLight = existing.GetComponent<Light>();
                }

                if (spotLight == null)
                {
                    GameObject lightObj = new GameObject("Flashlight_SpotLight");
                    lightObj.transform.SetParent(Camera.main.transform, false);
                    lightObj.transform.localPosition = new Vector3(0.12f, -0.08f, 0.15f);
                    lightObj.transform.localRotation = Quaternion.identity;
                    spotLight = lightObj.AddComponent<Light>();
                }
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            // Phím Q: Bật / Tắt đèn pin theo đúng GDD
            if (kb.qKey.wasPressedThisFrame)
            {
                isFlashlightOn = !isFlashlightOn;
                ApplyLightState();
                GameHUDManager.Instance.ShowNotification(isFlashlightOn ? "Đèn pin [Q]: BẬT" : "Đèn pin [Q]: TẮT", 1.2f);
            }

            // Phím U: Chuyển chế độ Đèn UV để soi quái tàng hình (Infiltrator Tầng 2)
            if (kb.uKey.wasPressedThisFrame && isFlashlightOn)
            {
                isUVMode = !isUVMode;
                ApplyLightState();
                GameHUDManager.Instance.ShowNotification(isUVMode ? "Chế độ Đèn UV [U]: BẬT" : "Chế độ Đèn Pin Thường [U]", 1.2f);
            }

            if (spotLight != null)
            {
                bool uvActive = isFlashlightOn && isUVMode;
                GameEvents.RaiseUVLightUpdated(uvActive, spotLight.transform, spotLight.range, spotLight.spotAngle);
            }
        }

        private void ApplyLightState()
        {
            if (spotLight == null)
            {
                return;
            }

            float scale = Mathf.Max(1f, transform.localScale.y);
            float distanceAttenuationCompensation = scale * scale;

            spotLight.type = LightType.Spot;
            spotLight.renderMode = LightRenderMode.ForcePixel;
            spotLight.shadows = LightShadows.Soft;
            spotLight.spotAngle = 58f;
            spotLight.innerSpotAngle = 32f;
            spotLight.range = baseRange * scale;

            spotLight.enabled = isFlashlightOn;
            spotLight.color = isUVMode ? uvLightColor : normalLightColor;

            float basePower = isUVMode ? uvIntensity : normalIntensity;
            spotLight.intensity = basePower * distanceAttenuationCompensation;
        }
    }
}
