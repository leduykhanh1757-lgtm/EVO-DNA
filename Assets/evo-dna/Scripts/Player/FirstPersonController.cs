using UnityEngine;
using UnityEngine.InputSystem;
using EvoDNA.Systems;

namespace EvoDNA.Player
{
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("World Scale Synchronization (Đồng bộ kích thước với Map)")]
        [Tooltip("Tỷ lệ kích thước nhân vật so với môi trường (Map đang ở tỷ lệ ~20-22)")]
        [SerializeField] private float playerWorldScale = 22f;

        [Header("References")]
        [SerializeField] private Transform cameraRoot;

        [Header("Movement Settings (Base 1m Scale)")]
        [SerializeField] private float walkSpeed = 4.2f;
        [SerializeField] private float sprintSpeed = 7.6f;
        [SerializeField] private float crouchSpeed = 2.2f;
        [SerializeField] private float jumpHeight = 1.25f;
        [SerializeField] private float gravity = -19.62f;

        [Header("Character Proportions (Base 1m Scale)")]
        [SerializeField] private float standingHeight = 1.85f;
        [SerializeField] private float crouchingHeight = 1.0f;
        [SerializeField] private float characterRadius = 0.32f;
        [SerializeField] private float crouchTransitionSpeed = 10f;

        [Header("Look Settings")]
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private float maxLookAngle = 85f;

        [Header("Stealth & Noise Radius (Base 1m Scale)")]
        [SerializeField] private float walkNoiseRadius = 6f;
        [SerializeField] private float sprintNoiseRadius = 18f;
        [SerializeField] private float noiseInterval = 0.35f;

        private CharacterController controller;
        private Vector3 velocity;
        private float verticalRotation;
        private float noiseTimer;
        private float dashSpeedMultiplier = 1f;
        private float dashEndTime;
        private bool isMovementLocked;
        private bool isHidden;

        public bool IsHidden => isHidden;
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public Transform CameraRoot => cameraRoot;
        public float PlayerWorldScale => playerWorldScale;

        private void Awake()
        {
            // Bắt buộc đồng bộ Scale của Player_POV với Map (Scale = 22) ngay lập tức
            if (playerWorldScale < 5f)
            {
                playerWorldScale = 22f;
            }
            transform.localScale = Vector3.one * playerWorldScale;

            controller = GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.height = standingHeight;
                controller.radius = characterRadius;
                controller.center = new Vector3(0f, standingHeight * 0.5f, 0f);
                controller.stepOffset = 0.4f;
            }

            // Nếu vị trí Y của Player bị đặt quá cao trên nóc trần nhà (VD: Y = 75) -> hạ xuống mặt sàn tầng 1 (Y = 4.5)
            if (transform.position.y > 45f)
            {
                bool wasEnabled = controller != null && controller.enabled;
                if (controller != null) controller.enabled = false;
                Vector3 pos = transform.position;
                pos.y = 4.5f;
                transform.position = pos;
                if (controller != null) controller.enabled = wasEnabled;
            }

            if (cameraRoot == null && Camera.main != null)
            {
                cameraRoot = Camera.main.transform;
            }

            if (cameraRoot != null)
            {
                cameraRoot.localPosition = new Vector3(0f, standingHeight * 0.9f, 0f);
            }
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            HandleCursorToggle();

            if (isMovementLocked)
            {
                HandleLook();
                return;
            }

            HandleLook();
            HandleMovement();
        }

        private void HandleCursorToggle()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
        }

        private void HandleLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked || cameraRoot == null)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
            verticalRotation -= delta.y;
            verticalRotation = Mathf.Clamp(verticalRotation, -maxLookAngle, maxLookAngle);

            cameraRoot.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
            transform.Rotate(Vector3.up * delta.x);
        }

        private void HandleMovement()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            float scaleFactor = Mathf.Max(1f, transform.localScale.y);
            bool grounded = controller.isGrounded;

            if (grounded && velocity.y < 0f)
            {
                velocity.y = -2f * scaleFactor;
            }

            float moveX = 0f;
            float moveZ = 0f;
            if (kb.dKey.isPressed) moveX += 1f;
            if (kb.aKey.isPressed) moveX -= 1f;
            if (kb.wKey.isPressed) moveZ += 1f;
            if (kb.sKey.isPressed) moveZ -= 1f;

            IsCrouching = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;
            bool isMovingInput = Mathf.Abs(moveX) > 0.01f || Mathf.Abs(moveZ) > 0.01f;
            IsSprinting = !IsCrouching && kb.leftShiftKey.isPressed && isMovingInput;

            float targetHeight = IsCrouching ? crouchingHeight : standingHeight;
            controller.height = Mathf.Lerp(controller.height, targetHeight, Time.deltaTime * crouchTransitionSpeed);
            controller.center = new Vector3(0f, controller.height * 0.5f, 0f);

            if (cameraRoot != null)
            {
                Vector3 camLocalPos = cameraRoot.localPosition;
                camLocalPos.y = Mathf.Lerp(camLocalPos.y, controller.height * 0.9f, Time.deltaTime * crouchTransitionSpeed);
                cameraRoot.localPosition = camLocalPos;
            }

            if (Time.time > dashEndTime)
            {
                dashSpeedMultiplier = 1f;
            }

            float currentSpeed = walkSpeed;
            if (IsCrouching)
            {
                currentSpeed = crouchSpeed;
            }
            else if (IsSprinting)
            {
                currentSpeed = sprintSpeed;
            }

            Vector3 moveDir = (transform.right * moveX + transform.forward * moveZ).normalized;
            controller.Move(moveDir * (currentSpeed * dashSpeedMultiplier * scaleFactor * Time.deltaTime));

            if (grounded && kb.spaceKey.wasPressedThisFrame && !IsCrouching)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * scaleFactor * -2f * (gravity * scaleFactor));
                GameEvents.RaiseNoiseEmitted(transform.position, walkNoiseRadius * scaleFactor);
            }

            velocity.y += (gravity * scaleFactor) * Time.deltaTime;
            controller.Move(velocity * Time.deltaTime);

            // Phát tiếng động cho Quái (Crawler nghe tiếng bước chân)
            if (grounded && moveDir.sqrMagnitude > 0.1f && !IsCrouching)
            {
                noiseTimer -= Time.deltaTime;
                if (noiseTimer <= 0f)
                {
                    noiseTimer = noiseInterval;
                    float radius = (IsSprinting ? sprintNoiseRadius : walkNoiseRadius) * scaleFactor;
                    GameEvents.RaiseNoiseEmitted(transform.position, radius);
                }
            }
        }

        public void TriggerDash(float multiplier, float duration)
        {
            dashSpeedMultiplier = multiplier;
            dashEndTime = Time.time + duration;
        }

        public void SetHiddenState(bool hidden, bool lockMovement)
        {
            isHidden = hidden;
            isMovementLocked = lockMovement;
            if (controller != null)
            {
                controller.enabled = !lockMovement;
            }
            GameEvents.RaisePlayerHiddenChanged(hidden);
        }
    }
}
