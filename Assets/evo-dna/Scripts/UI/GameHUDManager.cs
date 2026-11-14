using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace EvoDNA.UI
{
    /// <summary>
    /// Hệ thống UI tập trung duy nhất trên Canvas (Thay thế toàn bộ OnGUI() trên 6 script để đạt 0 GC Alloc mỗi frame).
    /// </summary>
    public class GameHUDManager : MonoBehaviour
    {
        private static GameHUDManager instance;
        public static GameHUDManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<GameHUDManager>();
                    if (instance == null)
                    {
                        GameObject go = new GameObject("GameHUD_CanvasManager");
                        instance = go.AddComponent<GameHUDManager>();
                    }
                }
                return instance;
            }
        }

        private Canvas hudCanvas;
        private RawImage bloodOverlayImage;

        private GameObject promptPanel;
        private Text promptText;
        private string lastPromptValue = string.Empty;

        private GameObject notificationPanel;
        private Text notificationText;
        private float notificationTimer;
        private string lastNotificationValue = string.Empty;

        private GameObject weaponPanel;
        private Text weaponText;
        private string lastWeaponValue = string.Empty;

        // Passcode Popup UI
        private GameObject passcodePopupRoot;
        private Text passcodeTitleText;
        private Text passcodeStatusText;
        private Text passcodeScreenText;
        private Action<char> onDigitClicked;
        private Action onClearClicked;
        private Action onSubmitClicked;
        private Action onCloseClicked;

        private Font defaultFont;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            BuildHUDCanvas();
        }

        private void Update()
        {
            if (notificationTimer > 0f)
            {
                notificationTimer -= Time.deltaTime;
                if (notificationTimer <= 0f && notificationPanel != null && notificationPanel.activeSelf)
                {
                    notificationPanel.SetActive(false);
                    lastNotificationValue = string.Empty;
                }
            }
        }

        public void SetInteractPrompt(string prompt)
        {
            if (promptPanel == null)
            {
                return;
            }

            bool shouldShow = !string.IsNullOrEmpty(prompt);
            if (promptPanel.activeSelf != shouldShow)
            {
                promptPanel.SetActive(shouldShow);
            }

            if (shouldShow && !string.Equals(lastPromptValue, prompt, StringComparison.Ordinal))
            {
                lastPromptValue = prompt;
                promptText.text = prompt;
            }
        }

        public void ShowNotification(string message, float duration = 1.6f)
        {
            if (notificationPanel == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            notificationTimer = duration;
            if (!notificationPanel.activeSelf)
            {
                notificationPanel.SetActive(true);
            }

            if (!string.Equals(lastNotificationValue, message, StringComparison.Ordinal))
            {
                lastNotificationValue = message;
                notificationText.text = message;
            }
        }

        public void SetWeaponHUD(bool visible, string statusText)
        {
            if (weaponPanel == null)
            {
                return;
            }

            if (weaponPanel.activeSelf != visible)
            {
                weaponPanel.SetActive(visible);
            }

            if (visible && !string.Equals(lastWeaponValue, statusText, StringComparison.Ordinal))
            {
                lastWeaponValue = statusText;
                weaponText.text = statusText;
            }
        }

        public void SetBloodOverlay(Texture2D texture, float alpha)
        {
            if (bloodOverlayImage == null)
            {
                return;
            }

            bool show = alpha > 0.005f && texture != null;
            if (bloodOverlayImage.gameObject.activeSelf != show)
            {
                bloodOverlayImage.gameObject.SetActive(show);
            }

            if (show)
            {
                if (bloodOverlayImage.texture != texture)
                {
                    bloodOverlayImage.texture = texture;
                }
                bloodOverlayImage.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        public void OpenPasscodePopup(
            string doorTitle,
            Action<char> digitCallback,
            Action clearCallback,
            Action submitCallback,
            Action closeCallback)
        {
            if (passcodePopupRoot == null)
            {
                return;
            }

            onDigitClicked = digitCallback;
            onClearClicked = clearCallback;
            onSubmitClicked = submitCallback;
            onCloseClicked = closeCallback;

            passcodeTitleText.text = $"KHÓA ĐIỆN TỬ - {doorTitle.ToUpper()}";
            UpdatePasscodeDisplay(string.Empty, "NHẬP MẬT MÃ ĐỂ MỞ CỬA", new Color(0.8f, 0.95f, 1f, 1f));
            passcodePopupRoot.SetActive(true);
        }

        public void UpdatePasscodeDisplay(string currentCode, string statusMsg, Color statusColor)
        {
            if (passcodePopupRoot == null)
            {
                return;
            }

            passcodeScreenText.text = string.IsNullOrEmpty(currentCode) ? "_ _ _ _" : currentCode;
            passcodeStatusText.text = statusMsg;
            passcodeStatusText.color = statusColor;
        }

        public void ClosePasscodePopup()
        {
            if (passcodePopupRoot != null && passcodePopupRoot.activeSelf)
            {
                passcodePopupRoot.SetActive(false);
            }
        }

        private void BuildHUDCanvas()
        {
            defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null)
            {
                defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            // Đảm bảo có EventSystem hỗ trợ InputSystem để bấm nút trên Popup mật mã
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.transform.SetParent(transform, false);
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<InputSystemUIInputModule>();
            }

            hudCanvas = gameObject.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 100;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            // 1. Lớp hiệu ứng máu văng toàn màn hình
            GameObject bloodObj = CreateUIObject("BloodOverlay", transform);
            RectTransform bloodRect = bloodObj.GetComponent<RectTransform>();
            bloodRect.anchorMin = Vector2.zero;
            bloodRect.anchorMax = Vector2.one;
            bloodRect.offsetMin = Vector2.zero;
            bloodRect.offsetMax = Vector2.zero;
            bloodOverlayImage = bloodObj.AddComponent<RawImage>();
            bloodOverlayImage.raycastTarget = false;
            bloodObj.SetActive(false);

            // 2. Tâm ngắm (Crosshair) chính giữa màn hình
            GameObject crosshairObj = CreateUIObject("CrosshairDot", transform);
            RectTransform chRect = crosshairObj.GetComponent<RectTransform>();
            chRect.anchorMin = new Vector2(0.5f, 0.5f);
            chRect.anchorMax = new Vector2(0.5f, 0.5f);
            chRect.sizeDelta = new Vector2(5f, 5f);
            Image chImg = crosshairObj.AddComponent<Image>();
            chImg.color = new Color(1f, 1f, 1f, 0.85f);
            chImg.raycastTarget = false;

            // 3. Hộp nhắc tương tác [F]
            promptPanel = CreatePanel("InteractPromptPanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -55f), new Vector2(520f, 48f), new Color(0.05f, 0.07f, 0.1f, 0.86f));
            promptText = CreateText("PromptText", promptPanel.transform, 22, new Color(1f, 0.92f, 0.25f, 1f));
            promptPanel.SetActive(false);

            // 4. Hộp thông báo trạng thái ngắn (Đèn pin / Trúng đạn / Nạp đạn)
            notificationPanel = CreatePanel("NotificationPanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -112f), new Vector2(460f, 40f), new Color(0.06f, 0.08f, 0.12f, 0.82f));
            notificationText = CreateText("NotificationText", notificationPanel.transform, 19, new Color(0.95f, 0.96f, 0.98f, 1f));
            notificationPanel.SetActive(false);

            // 5. Hộp thông tin Vũ khí ở góc dưới phải
            weaponPanel = CreatePanel("WeaponHUDPanel", transform, new Vector2(1f, 0f), new Vector2(-280f, 42f), new Vector2(520f, 46f), new Color(0.05f, 0.07f, 0.1f, 0.85f));
            weaponText = CreateText("WeaponHUDText", weaponPanel.transform, 19, Color.white);
            weaponPanel.SetActive(false);

            // 6. Bảng Popup Nhập Mật Mã Cửa
            BuildPasscodePopupUI();
        }

        private void BuildPasscodePopupUI()
        {
            passcodePopupRoot = CreatePanel("PasscodePopupPanel", transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 490f), new Color(0.08f, 0.1f, 0.14f, 0.96f));

            // Tiêu đề
            GameObject titleObj = CreateUIObject("Title", passcodePopupRoot.transform);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);
            titleRect.sizeDelta = new Vector2(340f, 34f);
            passcodeTitleText = titleObj.AddComponent<Text>();
            ConfigureTextComponent(passcodeTitleText, 19, Color.white);

            // Trạng thái
            GameObject statusObj = CreateUIObject("Status", passcodePopupRoot.transform);
            RectTransform statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.5f, 1f);
            statusRect.anchorMax = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -60f);
            statusRect.sizeDelta = new Vector2(340f, 28f);
            passcodeStatusText = statusObj.AddComponent<Text>();
            ConfigureTextComponent(passcodeStatusText, 15, new Color(0.8f, 0.95f, 1f, 1f));

            // Màn hình số
            GameObject screenPanel = CreatePanel("ScreenBox", passcodePopupRoot.transform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(316f, 56f), new Color(0.02f, 0.16f, 0.08f, 1f));
            passcodeScreenText = CreateText("ScreenText", screenPanel.transform, 30, new Color(0.25f, 1f, 0.48f, 1f));

            // Lưới phím 3x4
            float startX = -102f;
            float startY = 58f;
            float btnW = 94f;
            float btnH = 58f;
            float gap = 10f;

            for (int i = 1; i <= 9; i++)
            {
                int row = (i - 1) / 3;
                int col = (i - 1) % 3;
                char digitChar = (char)('0' + i);
                Vector2 pos = new Vector2(startX + col * (btnW + gap), startY - row * (btnH + gap));
                CreateKeypadButton(passcodePopupRoot.transform, i.ToString(), pos, new Vector2(btnW, btnH), () =>
                {
                    onDigitClicked?.Invoke(digitChar);
                });
            }

            float row4Y = startY - 3 * (btnH + gap);
            CreateKeypadButton(passcodePopupRoot.transform, "XÓA", new Vector2(startX, row4Y), new Vector2(btnW, btnH), () =>
            {
                onClearClicked?.Invoke();
            });
            CreateKeypadButton(passcodePopupRoot.transform, "0", new Vector2(startX + (btnW + gap), row4Y), new Vector2(btnW, btnH), () =>
            {
                onDigitClicked?.Invoke('0');
            });
            CreateKeypadButton(passcodePopupRoot.transform, "OK", new Vector2(startX + 2 * (btnW + gap), row4Y), new Vector2(btnW, btnH), () =>
            {
                onSubmitClicked?.Invoke();
            });

            CreateKeypadButton(passcodePopupRoot.transform, "ĐÓNG [ESC]", new Vector2(0f, -210f), new Vector2(302f, 40f), () =>
            {
                onCloseClicked?.Invoke();
            });

            passcodePopupRoot.SetActive(false);
        }

        private void CreateKeypadButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnObj = CreatePanel($"Btn_{label}", parent, new Vector2(0.5f, 0.5f), anchoredPos, size, new Color(0.18f, 0.22f, 0.28f, 1f));
            Image img = btnObj.GetComponent<Image>();
            img.raycastTarget = true;

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            CreateText("Label", btnObj.transform, 20, Color.white).text = label;
        }

        private GameObject CreatePanel(string objName, Transform parent, Vector2 anchor, Vector2 anchoredPos, Vector2 size, Color bgColor)
        {
            GameObject panelObj = CreateUIObject(objName, parent);
            RectTransform rt = panelObj.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Image bg = panelObj.AddComponent<Image>();
            bg.color = bgColor;
            bg.raycastTarget = false;
            return panelObj;
        }

        private Text CreateText(string objName, Transform parent, int fontSize, Color textColor)
        {
            GameObject txtObj = CreateUIObject(objName, parent);
            RectTransform rt = txtObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 2f);
            rt.offsetMax = new Vector2(-8f, -2f);

            Text txt = txtObj.AddComponent<Text>();
            ConfigureTextComponent(txt, fontSize, textColor);
            return txt;
        }

        private void ConfigureTextComponent(Text txt, int fontSize, Color textColor)
        {
            txt.font = defaultFont;
            txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = textColor;
            txt.raycastTarget = false;
        }

        private static GameObject CreateUIObject(string objName, Transform parent)
        {
            GameObject go = new GameObject(objName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
