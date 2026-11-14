using UnityEngine;
using EvoDNA.Player;

namespace EvoDNA.Interaction
{
    public class HidingSpot : MonoBehaviour, IInteractable
    {
        [Header("Hiding Spot Settings (Giường / Gầm Bàn / Tủ Locker)")]
        [SerializeField] private string spotName = "Gầm Giường / Tủ";
        [SerializeField] private bool isUnderBedOrTable = true;
        [SerializeField] private float transitionSpeed = 10f;

        private FirstPersonController currentHidingPlayer;
        private Vector3 savedExitWorldPos;
        private Vector3 targetHideWorldPos;
        private Vector3 lastInteractHitPoint;
        private bool hasValidHitPoint;

        public bool IsPlayerInside => currentHidingPlayer != null;

        private void Awake()
        {
            // Khắc phục triệt để lỗi Mesh gộp (Combined Mesh như SM_Couch_M_Mattress_0 trải dài qua nhiều phòng):
            // Nếu gắn BoxCollider lên Mesh gộp, hình hộp BoxCollider sẽ bao trùm cả hành lang và xuyên qua tường.
            // Bắt buộc xóa BoxCollider trên các Mesh gộp và thay bằng MeshCollider bám sát từng chiếc giường/tủ thực tế.
            Renderer rend = GetComponent<Renderer>();
            if (rend != null && (rend.bounds.extents.x > 30f || rend.bounds.extents.z > 30f))
            {
                BoxCollider[] boxes = GetComponents<BoxCollider>();
                for (int i = 0; i < boxes.Length; i++)
                {
                    Destroy(boxes[i]);
                }

                if (GetComponent<MeshCollider>() == null)
                {
                    MeshFilter mf = GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        MeshCollider mc = gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }
            }
        }

        public void ConfigureSpot(string newName, bool underBedOrTable)
        {
            spotName = newName;
            isUnderBedOrTable = underBedOrTable;
        }

        public void SetInteractionHitPoint(Vector3 hitPoint)
        {
            lastInteractHitPoint = hitPoint;
            hasValidHitPoint = true;
        }

        public string GetInteractPrompt()
        {
            return currentHidingPlayer == null
                ? $"[F] Ẩn nấp ({spotName})"
                : $"[F] Chui ra khỏi {spotName}";
        }

        public void Interact(GameObject interactor)
        {
            if (currentHidingPlayer == null)
            {
                EnterHiding(interactor);
            }
            else
            {
                ExitHiding();
            }
        }

        private void EnterHiding(GameObject interactor)
        {
            if (interactor == null)
            {
                return;
            }

            FirstPersonController fp = interactor.GetComponent<FirstPersonController>();
            if (fp == null)
            {
                return;
            }

            savedExitWorldPos = fp.transform.position;
            float scale = Mathf.Max(1f, fp.transform.localScale.y);

            // Tính vị trí ẩn nấp ngay tại chiếc giường / tủ trước mặt người chơi (dựa trên điểm tia nhìn chạm vào),
            // tuyệt đối KHÔNG dùng Renderer.bounds.center vì các giường trong FBX bị gộp chung 1 Mesh giữa nhiều phòng!
            Vector3 refPoint = hasValidHitPoint
                ? lastInteractHitPoint
                : (savedExitWorldPos + fp.transform.forward * (0.85f * scale));

            Vector3 horizontalOffset = refPoint - savedExitWorldPos;
            horizontalOffset.y = 0f;
            float maxStep = 1.15f * scale;
            if (horizontalOffset.magnitude > maxStep)
            {
                horizontalOffset = horizontalOffset.normalized * maxStep;
            }

            if (isUnderBedOrTable)
            {
                // Chui xuống ngay dưới gầm chiếc giường / bàn trước mặt
                targetHideWorldPos = new Vector3(
                    savedExitWorldPos.x + horizontalOffset.x * 0.75f,
                    savedExitWorldPos.y - (1.15f * scale),
                    savedExitWorldPos.z + horizontalOffset.z * 0.75f
                );
            }
            else
            {
                // Bước vào trong chiếc tủ Locker ngay trước mặt
                targetHideWorldPos = new Vector3(
                    savedExitWorldPos.x + horizontalOffset.x * 0.65f,
                    savedExitWorldPos.y,
                    savedExitWorldPos.z + horizontalOffset.z * 0.65f
                );
            }

            fp.SetHiddenState(true, true);
            currentHidingPlayer = fp;
        }

        public void ExitHiding()
        {
            if (currentHidingPlayer == null)
            {
                return;
            }

            currentHidingPlayer.transform.position = savedExitWorldPos;
            currentHidingPlayer.SetHiddenState(false, false);
            currentHidingPlayer = null;
        }

        private void Update()
        {
            if (currentHidingPlayer != null)
            {
                currentHidingPlayer.transform.position = Vector3.Lerp(
                    currentHidingPlayer.transform.position,
                    targetHideWorldPos,
                    Time.deltaTime * transitionSpeed
                );
            }
        }
    }
}
