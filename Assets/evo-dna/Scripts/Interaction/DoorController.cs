using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using EvoDNA.Player;
using EvoDNA.UI;

namespace EvoDNA.Interaction
{
    public enum DoorType
    {
        Normal = 0,   // Cửa thường: Đến gần hiện "F : Mở", bấm F mở 90 độ
        Passcode = 1  // Cửa mật mã: Đến gần hiện Popup nhập mật mã, nhập đúng mở 90 độ
    }

    public class DoorController : MonoBehaviour
    {
        public static DoorController ActiveTriggerDoor { get; private set; }
        public static bool IsPasscodePopupOpen { get; private set; }
        public static bool IsAnyDoorCapturingInput => ActiveTriggerDoor != null || IsPasscodePopupOpen;

        [Header("1. Phân loại Cửa")]
        [SerializeField] private DoorType doorType = DoorType.Normal;
        [SerializeField] private string doorTitle = "Cửa Phòng Lab";

        [Header("2. Bản lề & Góc mở 90 độ")]
        [Tooltip("Kéo GameObject 'hinge' vào đây (để 'hinge' làm con bên trong cửa như bạn đang làm là chuẩn nhất!).")]
        [SerializeField] private Transform hingePivot;
        [Tooltip("Góc mở cửa (Điền 90 hoặc -90 để đổi hướng mở vào trong / ra ngoài)")]
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float openSpeed = 160f;

        [Header("3. Cài đặt Mật mã (Chỉ dùng khi Door Type = Passcode)")]
        [SerializeField] private string correctPasscode = "1211";
        [Tooltip("Tự động bật Popup nhập mật mã ngay khi nhân vật bước vào BoxCollider Trigger của cửa")]
        [SerializeField] private bool autoPopupOnTriggerEnter = true;

        private Vector3 hingeWorldPos;
        private Vector3 closedCenterOffsetFromHinge;
        private Vector3 initialBladeDir = Vector3.forward;
        private float doorWidth = 24f;
        private float doorThickness = 2.5f;
        private float doorHeight = 42f;

        private NavMeshObstacle doorNavObstacle;
        private Transform navObstacleTransform;
        private readonly Collider[] overlapHits = new Collider[16];
        private float lastBlockedNotifyTime;

        private bool isOpen;
        private bool isUnlocked;
        private bool isPlayerInsideTrigger;
        private bool isShowingPopup;
        private float currentAngle;
        private bool isBlockedStopped;

        private string enteredInput = string.Empty;
        private FirstPersonController cachedPlayer;

        public void ConfigureDoor(DoorType type, string title, Transform hinge, float angle = 90f, string code = "1211")
        {
            doorType = type;
            doorTitle = title;
            hingePivot = hinge;
            openAngle = angle;
            correctPasscode = code;
        }

        private void Awake()
        {
            // Tự động gỡ bỏ nếu nhầm vào Cửa Thang Máy / Cửa Trượt Điện (logic thang máy riêng, không mở kiểu bản lề 90 độ)
            if (IsElevatorOrSlidingDoor(gameObject))
            {
                BoxCollider[] boxes = GetComponents<BoxCollider>();
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (boxes[i] != null && boxes[i].isTrigger)
                    {
                        Destroy(boxes[i]);
                    }
                }
                Destroy(this);
                return;
            }

            if (hingePivot == null)
            {
                Transform childHinge = transform.Find("hinge");
                if (childHinge == null) childHinge = transform.Find("Hinge");
                hingePivot = childHinge != null ? childHinge : transform;
            }

            hingeWorldPos = hingePivot.position;
            Renderer rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Bounds wb = rend.bounds;
                if (hingePivot == transform)
                {
                    Vector3 pivotCheck = wb.center - transform.position;
                    pivotCheck.y = 0f;
                    if (pivotCheck.magnitude < Mathf.Max(wb.extents.x, wb.extents.z) * 0.45f)
                    {
                        hingeWorldPos = wb.size.x >= wb.size.z
                            ? new Vector3(wb.min.x, wb.center.y, wb.center.z)
                            : new Vector3(wb.center.x, wb.center.y, wb.min.z);
                    }
                }

                closedCenterOffsetFromHinge = wb.center - hingeWorldPos;
                Vector3 horiz = new Vector3(closedCenterOffsetFromHinge.x, 0f, closedCenterOffsetFromHinge.z);
                initialBladeDir = horiz.sqrMagnitude > 0.01f ? horiz.normalized : transform.forward;
                doorWidth = Mathf.Max(8f, Mathf.Max(wb.size.x, wb.size.z));
                doorThickness = Mathf.Clamp(Mathf.Min(wb.size.x, wb.size.z), 1.5f, 8f);
                doorHeight = Mathf.Max(15f, wb.size.y);
            }
            else
            {
                closedCenterOffsetFromHinge = transform.forward * 12f;
                initialBladeDir = transform.forward;
            }

            CreateDoorNavMeshObstacle();
            isUnlocked = (doorType == DoorType.Normal);
        }

        private void CreateDoorNavMeshObstacle()
        {
            // Tạo NavMeshObstacle độc lập với Scale 6031x của FBX để khoét NavMesh chính xác tại khung cửa khi cửa đóng
            GameObject obsObj = new GameObject($"DoorNavObstacle_{gameObject.name}");
            navObstacleTransform = obsObj.transform;
            UpdateNavObstacleTransform(0f);

            doorNavObstacle = obsObj.AddComponent<NavMeshObstacle>();
            doorNavObstacle.shape = NavMeshObstacleShape.Box;
            doorNavObstacle.center = Vector3.zero;
            doorNavObstacle.size = new Vector3(doorThickness + 1.5f, doorHeight * 0.9f, doorWidth * 0.72f);
            doorNavObstacle.carving = true;
            doorNavObstacle.carveOnlyStationary = false;
            doorNavObstacle.carvingMoveThreshold = 0.5f;
            doorNavObstacle.carvingTimeToStationary = 0.1f;
            doorNavObstacle.enabled = true;
        }

        private void UpdateNavObstacleTransform(float angle)
        {
            if (navObstacleTransform == null)
            {
                return;
            }

            Quaternion rot = Quaternion.Euler(0f, angle, 0f);
            Vector3 bladeCenter = hingeWorldPos + rot * closedCenterOffsetFromHinge;
            Quaternion bladeOrientation = Quaternion.LookRotation(rot * initialBladeDir, Vector3.up);
            navObstacleTransform.SetPositionAndRotation(bladeCenter, bladeOrientation);
        }

        private void OnDestroy()
        {
            if (navObstacleTransform != null)
            {
                Destroy(navObstacleTransform.gameObject);
            }
        }

        private void OnDisable()
        {
            if (isShowingPopup)
            {
                ClosePasscodePopup();
            }
            if (ActiveTriggerDoor == this)
            {
                ActiveTriggerDoor = null;
                GameHUDManager.Instance.SetInteractPrompt(string.Empty);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayerCollider(other, out FirstPersonController fp))
            {
                return;
            }

            cachedPlayer = fp;
            if (!isPlayerInsideTrigger)
            {
                isPlayerInsideTrigger = true;
                ActiveTriggerDoor = this;

                if (doorType == DoorType.Passcode && !isUnlocked && autoPopupOnTriggerEnter)
                {
                    OpenPasscodePopup();
                }
                else
                {
                    RefreshDoorPromptUI();
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayerCollider(other, out _))
            {
                return;
            }

            ExitDoorZone();
        }

        private void ExitDoorZone()
        {
            isPlayerInsideTrigger = false;
            if (ActiveTriggerDoor == this)
            {
                ActiveTriggerDoor = null;
                GameHUDManager.Instance.SetInteractPrompt(string.Empty);
            }

            if (isShowingPopup)
            {
                ClosePasscodePopup();
            }
        }

        private bool IsPlayerCollider(Collider col, out FirstPersonController fp)
        {
            fp = null;
            if (col == null)
            {
                return false;
            }

            if (col.CompareTag("Player"))
            {
                fp = col.GetComponentInParent<FirstPersonController>();
                return true;
            }

            fp = col.GetComponentInParent<FirstPersonController>();
            return fp != null;
        }

        private void Update()
        {
            UpdateDoorRotation();

            if (!isPlayerInsideTrigger)
            {
                return;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (isShowingPopup)
            {
                HandlePopupKeyboardInput(kb);
                return;
            }

            if (kb.fKey.wasPressedThisFrame)
            {
                if (doorType == DoorType.Passcode && !isUnlocked)
                {
                    OpenPasscodePopup();
                }
                else
                {
                    isBlockedStopped = false;
                    isOpen = !isOpen;
                    RefreshDoorPromptUI();
                }
            }
        }

        private void RefreshDoorPromptUI()
        {
            if (!isPlayerInsideTrigger || isShowingPopup)
            {
                GameHUDManager.Instance.SetInteractPrompt(string.Empty);
                return;
            }

            string promptText = (doorType == DoorType.Passcode && !isUnlocked)
                ? "F : Nhập mật mã mở cửa"
                : (isOpen ? "F : Đóng" : "F : Mở");

            GameHUDManager.Instance.SetInteractPrompt(promptText);
        }

        private void UpdateDoorRotation()
        {
            if (isBlockedStopped)
            {
                return;
            }

            float targetAngle = isOpen ? openAngle : 0f;
            if (Mathf.Approximately(currentAngle, targetAngle))
            {
                return;
            }

            float nextAngle = Mathf.MoveTowards(currentAngle, targetAngle, openSpeed * Time.deltaTime);

            // Cách 1 (Barricade Door): Kiểm tra xem cánh cửa ở góc nextAngle có va chạm với Đồ vật chèn cửa (DraggableProp) không
            if (IsBlockedByPropAtAngle(nextAngle, out DraggableProp blockingProp))
            {
                isBlockedStopped = true;

                // Nếu cửa đang mở ra mà bị bình gas/ghế chặn ngay từ đầu (< 35 độ) -> coi như chưa mở được
                if (isOpen && Mathf.Abs(currentAngle) < 35f)
                {
                    isOpen = false;
                    RefreshDoorPromptUI();
                }

                if (Time.time - lastBlockedNotifyTime > 1.2f)
                {
                    lastBlockedNotifyTime = Time.time;
                    GameHUDManager.Instance.ShowNotification($"Cửa đang bị chèn bởi {blockingProp.PropName}! Hãy dọn vật cản trước.", 2.2f);
                }

                UpdateNavMeshObstacleState();
                return;
            }

            float delta = nextAngle - currentAngle;
            currentAngle = nextAngle;

            transform.RotateAround(hingeWorldPos, Vector3.up, delta);
            UpdateNavObstacleTransform(currentAngle);
            UpdateNavMeshObstacleState();
        }

        private void UpdateNavMeshObstacleState()
        {
            if (doorNavObstacle == null)
            {
                return;
            }

            // Khi cửa mở rộng >= 60 độ thì tắt NavMeshObstacle để Quái vật đi qua được khung cửa;
            // Khi cửa đóng hoặc bị chèn chưa mở đủ rộng (< 60 độ) thì bật NavMeshObstacle chặn Quái vật.
            bool shouldBlockNavMesh = Mathf.Abs(currentAngle) < 60f;
            if (doorNavObstacle.enabled != shouldBlockNavMesh)
            {
                doorNavObstacle.enabled = shouldBlockNavMesh;
            }
        }

        private bool IsBlockedByPropAtAngle(float testAngle, out DraggableProp blockingProp)
        {
            blockingProp = null;

            Quaternion curRot = Quaternion.Euler(0f, currentAngle, 0f);
            Vector3 curBladeCenter = hingeWorldPos + curRot * closedCenterOffsetFromHinge;

            Quaternion nextRot = Quaternion.Euler(0f, testAngle, 0f);
            Vector3 nextBladeCenter = hingeWorldPos + nextRot * closedCenterOffsetFromHinge;
            Quaternion nextOrientation = Quaternion.LookRotation(nextRot * initialBladeDir, Vector3.up);

            // Hộp quét của cánh cửa (nâng nhẹ đáy lên khỏi mặt sàn để chỉ bắt đồ vật chèn cửa)
            Vector3 halfExtents = new Vector3(
                doorThickness * 0.5f + 1.1f,
                doorHeight * 0.44f,
                Mathf.Max(2f, doorWidth * 0.46f)
            );

            int hitCount = Physics.OverlapBoxNonAlloc(nextBladeCenter, halfExtents, overlapHits, nextOrientation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider c = overlapHits[i];
                if (c == null || c.transform == transform || c.transform.IsChildOf(transform))
                {
                    continue;
                }

                DraggableProp prop = c.GetComponentInParent<DraggableProp>();
                if (prop != null && !prop.IsCarried)
                {
                    Vector3 propCenter = prop.GetWorldCenter();
                    Vector2 propFlat = new Vector2(propCenter.x, propCenter.z);
                    Vector2 curFlat = new Vector2(curBladeCenter.x, curBladeCenter.z);
                    Vector2 nextFlat = new Vector2(nextBladeCenter.x, nextBladeCenter.z);

                    // Chỉ chặn khi hướng xoay của cửa đang ép sát vào đồ vật (cho phép xoay ngược lại để gỡ kẹt)
                    if ((nextFlat - propFlat).sqrMagnitude <= (curFlat - propFlat).sqrMagnitude + 0.05f)
                    {
                        blockingProp = prop;
                        return true;
                    }
                }
            }

            return false;
        }

        private void OpenPasscodePopup()
        {
            isShowingPopup = true;
            IsPasscodePopupOpen = true;
            enteredInput = string.Empty;

            GameHUDManager.Instance.SetInteractPrompt(string.Empty);
            GameHUDManager.Instance.OpenPasscodePopup(
                doorTitle,
                AppendDigit,
                ClearDigits,
                VerifyPasscode,
                ClosePasscodePopup
            );

            if (cachedPlayer != null)
            {
                cachedPlayer.SetHiddenState(cachedPlayer.IsHidden, true);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ClosePasscodePopup()
        {
            isShowingPopup = false;
            IsPasscodePopupOpen = false;
            GameHUDManager.Instance.ClosePasscodePopup();

            if (cachedPlayer != null)
            {
                cachedPlayer.SetHiddenState(cachedPlayer.IsHidden, false);
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (isPlayerInsideTrigger)
            {
                RefreshDoorPromptUI();
            }
        }

        private void HandlePopupKeyboardInput(Keyboard kb)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                ClosePasscodePopup();
                return;
            }

            if (kb.backspaceKey.wasPressedThisFrame && enteredInput.Length > 0)
            {
                enteredInput = enteredInput.Substring(0, enteredInput.Length - 1);
                GameHUDManager.Instance.UpdatePasscodeDisplay(enteredInput, "NHẬP MẬT MÃ ĐỂ MỞ CỬA", new Color(0.8f, 0.95f, 1f, 1f));
                return;
            }

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                VerifyPasscode();
                return;
            }

            AppendDigitIfPressed(kb.digit0Key, kb.numpad0Key, '0');
            AppendDigitIfPressed(kb.digit1Key, kb.numpad1Key, '1');
            AppendDigitIfPressed(kb.digit2Key, kb.numpad2Key, '2');
            AppendDigitIfPressed(kb.digit3Key, kb.numpad3Key, '3');
            AppendDigitIfPressed(kb.digit4Key, kb.numpad4Key, '4');
            AppendDigitIfPressed(kb.digit5Key, kb.numpad5Key, '5');
            AppendDigitIfPressed(kb.digit6Key, kb.numpad6Key, '6');
            AppendDigitIfPressed(kb.digit7Key, kb.numpad7Key, '7');
            AppendDigitIfPressed(kb.digit8Key, kb.numpad8Key, '8');
            AppendDigitIfPressed(kb.digit9Key, kb.numpad9Key, '9');
        }

        private void AppendDigitIfPressed(
            UnityEngine.InputSystem.Controls.KeyControl topRowKey,
            UnityEngine.InputSystem.Controls.KeyControl numpadKey,
            char digit)
        {
            if ((topRowKey != null && topRowKey.wasPressedThisFrame) ||
                (numpadKey != null && numpadKey.wasPressedThisFrame))
            {
                AppendDigit(digit);
            }
        }

        private void AppendDigit(char digit)
        {
            if (enteredInput.Length < 8)
            {
                enteredInput += digit;
                GameHUDManager.Instance.UpdatePasscodeDisplay(enteredInput, "NHẬP MẬT MÃ ĐỂ MỞ CỬA", new Color(0.8f, 0.95f, 1f, 1f));
            }
        }

        public static bool IsElevatorOrSlidingDoor(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            string n = go.name;
            if (n.Contains("Elevator") ||
                n.StartsWith("Door2_") ||
                n == "Door1" ||
                n == "Door2" ||
                n.StartsWith("electric_door"))
            {
                return true;
            }

            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterials != null)
            {
                for (int i = 0; i < rend.sharedMaterials.Length; i++)
                {
                    Material m = rend.sharedMaterials[i];
                    if (m != null && (m.name.Contains("Elevator") || m.name.StartsWith("electric_door")))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void ClearDigits()
        {
            enteredInput = string.Empty;
            GameHUDManager.Instance.UpdatePasscodeDisplay(enteredInput, "NHẬP MẬT MÃ ĐỂ MỞ CỬA", new Color(0.8f, 0.95f, 1f, 1f));
        }

        private void VerifyPasscode()
        {
            if (string.Equals(enteredInput.Trim(), correctPasscode.Trim(), System.StringComparison.Ordinal))
            {
                isUnlocked = true;
                isBlockedStopped = false;
                isOpen = true;
                ClosePasscodePopup();
                GameHUDManager.Instance.ShowNotification($"Mật mã chính xác! Đã mở {doorTitle}.", 2.0f);
            }
            else
            {
                enteredInput = string.Empty;
                GameHUDManager.Instance.UpdatePasscodeDisplay(enteredInput, "SAI MẬT MÃ! VUI LÒNG THỬ LẠI", new Color(1f, 0.35f, 0.35f, 1f));
            }
        }
    }
}
