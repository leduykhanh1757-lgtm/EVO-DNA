using UnityEngine;
using UnityEngine.AI;
using EvoDNA.Player;
using EvoDNA.AI;
using EvoDNA.Systems;

namespace EvoDNA.Interaction
{
    public class DraggableProp : MonoBehaviour, IInteractable
    {
        [Header("Carry & Throw Settings")]
        [SerializeField] private string propName = "Đồ vật";
        [SerializeField] private float followSpeed = 16f;
        [SerializeField] private float throwSpeed = 13f;
        [SerializeField] private float throwGravity = 22f;
        [SerializeField] private float throwDamage = 25f;

        private Renderer propRenderer;
        private Collider[] propColliders;
        private Transform holderCamera;
        private GameObject currentInteractor;

        private Vector3 centerToPivotOffset;
        private Vector3 propExtents;
        private float horizontalRadius;
        private float bottomToPivotY;
        private float holdDistance;
        private float defaultFloorY;

        private bool isCarried;
        private bool isThrown;
        private Vector3 throwVelocity;
        private float throwTimer;

        private NavMeshObstacle propNavObstacle;
        private Transform navObstacleTransform;

        private readonly RaycastHit[] castHits = new RaycastHit[16];
        private readonly Collider[] overlapHits = new Collider[16];

        public bool IsCarried => isCarried;
        public string PropName => propName;

        private void Awake()
        {
            propRenderer = GetComponentInChildren<Renderer>();
            propColliders = GetComponentsInChildren<Collider>();

            if (propRenderer != null)
            {
                Bounds b = propRenderer.bounds;
                // Nếu Mesh trong FBX là một cụm gộp nhiều phòng (kích thước ngang > 60 đơn vị):
                // Xóa BoxCollider bao trùm hành lang (thay bằng MeshCollider chuẩn xác) và hủy DraggableProp trên cụm gộp này.
                if (b.extents.x > 30f || b.extents.z > 30f)
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

                    Destroy(this);
                    return;
                }

                centerToPivotOffset = transform.position - b.center;
                propExtents = b.extents;
                horizontalRadius = Mathf.Max(0.1f, Mathf.Max(propExtents.x, propExtents.z));
                bottomToPivotY = transform.position.y - b.min.y;
                defaultFloorY = b.min.y;
            }
            else
            {
                centerToPivotOffset = Vector3.zero;
                propExtents = new Vector3(3f, 5f, 3f);
                horizontalRadius = 3f;
                bottomToPivotY = 0f;
                defaultFloorY = transform.position.y;
            }

            CreatePropNavMeshObstacle();
        }

        private void CreatePropNavMeshObstacle()
        {
            // Tạo NavMeshObstacle độc lập với Scale của FBX để khoét lưới NavMesh đúng vị trí đồ vật đặt xuống
            GameObject obsObj = new GameObject($"PropNavObstacle_{gameObject.name}");
            navObstacleTransform = obsObj.transform;
            navObstacleTransform.SetPositionAndRotation(transform.position - centerToPivotOffset, Quaternion.identity);

            propNavObstacle = obsObj.AddComponent<NavMeshObstacle>();
            propNavObstacle.shape = NavMeshObstacleShape.Box;
            propNavObstacle.center = Vector3.zero;
            propNavObstacle.size = new Vector3(
                Mathf.Max(3.5f, propExtents.x * 2.05f),
                Mathf.Max(6.0f, propExtents.y * 2.05f),
                Mathf.Max(3.5f, propExtents.z * 2.05f)
            );
            propNavObstacle.carving = true;
            propNavObstacle.carveOnlyStationary = false;
            propNavObstacle.carvingMoveThreshold = 0.5f;
            propNavObstacle.carvingTimeToStationary = 0.1f;
            propNavObstacle.enabled = true;
        }

        private void OnDestroy()
        {
            if (navObstacleTransform != null)
            {
                Destroy(navObstacleTransform.gameObject);
            }
        }

        public void SetPropName(string newName)
        {
            propName = newName;
        }

        public Vector3 GetWorldCenter()
        {
            return transform.position - centerToPivotOffset;
        }

        public string GetInteractPrompt()
        {
            return isCarried
                ? $"[F] Đặt {propName} xuống  |  [G / Chuột Trái] Ném"
                : $"[F] Cầm {propName}";
        }

        public void Interact(GameObject interactor)
        {
            if (!isCarried)
            {
                BeginCarry(interactor);
            }
            else
            {
                DropProp();
            }
        }

        private void BeginCarry(GameObject interactor)
        {
            currentInteractor = interactor;
            FirstPersonController fp = interactor != null ? interactor.GetComponent<FirstPersonController>() : null;
            holderCamera = (fp != null && fp.CameraRoot != null) ? fp.CameraRoot : (Camera.main != null ? Camera.main.transform : null);

            if (holderCamera == null)
            {
                return;
            }

            float scale = GetWorldScale();
            Vector3 worldCenter = GetWorldCenter();
            centerToPivotOffset = transform.position - worldCenter;

            if (propRenderer != null)
            {
                Bounds b = propRenderer.bounds;
                propExtents = b.extents;
                horizontalRadius = Mathf.Max(0.15f * scale, Mathf.Max(propExtents.x, propExtents.z));
                bottomToPivotY = transform.position.y - b.min.y;
            }

            Vector2 camXZ = new Vector2(holderCamera.position.x, holderCamera.position.z);
            Vector2 objXZ = new Vector2(worldCenter.x, worldCenter.z);
            float currentHorizontalDist = Vector2.Distance(camXZ, objXZ);
            holdDistance = Mathf.Clamp(currentHorizontalDist, 0.9f * scale, 1.75f * scale);

            isThrown = false;
            SetCollidersEnabled(false);
            isCarried = true;
        }

        public void DropProp()
        {
            if (!isCarried && !isThrown)
            {
                return;
            }

            float scale = GetWorldScale();
            Vector3 currentCenter = GetWorldCenter();
            float checkTopY = holderCamera != null
                ? Mathf.Max(holderCamera.position.y, currentCenter.y + propExtents.y)
                : currentCenter.y + propExtents.y + (0.5f * scale);

            float surfaceY = FindHighestSupportSurfaceY(currentCenter.x, currentCenter.z, checkTopY, scale);

            Vector3 finalPos = transform.position;
            finalPos.y = surfaceY + bottomToPivotY;
            transform.position = finalPos;

            isCarried = false;
            isThrown = false;
            holderCamera = null;
            SetCollidersEnabled(true);
        }

        public void ThrowProp(Vector3 throwDirection)
        {
            if (!isCarried)
            {
                return;
            }

            float scale = GetWorldScale();
            isCarried = false;
            isThrown = true;
            throwTimer = 0f;

            Vector3 dir = throwDirection.sqrMagnitude > 0.001f ? throwDirection.normalized : Vector3.forward;
            throwVelocity = dir * (throwSpeed * scale) + Vector3.up * (1.8f * scale);
            holderCamera = null;
        }

        private void Update()
        {
            if (isCarried && holderCamera != null)
            {
                UpdateCarriedMovement();
            }
            else if (isThrown)
            {
                UpdateThrownFlight();
            }
        }

        private void UpdateCarriedMovement()
        {
            float scale = GetWorldScale();

            // 1. Vị trí mong muốn theo hướng nhìn của Camera (nhìn lên sẽ nâng vật lên bàn/xe đẩy, nhìn xuống sẽ hạ thấp)
            Vector3 desiredCenter = holderCamera.position + holderCamera.forward * holdDistance + Vector3.down * (0.2f * scale);

            // 2. Chống xuyên tường / chướng ngại vật phía trước bằng SphereCast từ Camera tới vật
            Vector3 camToCenter = desiredCenter - holderCamera.position;
            float castDist = camToCenter.magnitude;
            if (castDist > 0.01f)
            {
                Vector3 castDir = camToCenter / castDist;
                int hitCount = Physics.SphereCastNonAlloc(
                    holderCamera.position,
                    horizontalRadius * 0.65f,
                    castDir,
                    castHits,
                    castDist,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                float closestDist = castDist;
                RaycastHit closestHit = default;
                bool foundObstacle = false;

                for (int i = 0; i < hitCount; i++)
                {
                    Collider col = castHits[i].collider;
                    if (IsIgnoredCollider(col))
                    {
                        continue;
                    }

                    // Nếu va phải bàn/xe đẩy thấp hơn ngực người chơi -> cho phép nâng vật lên trên mặt bàn ở bước 3 thay vì chặn ngang
                    if (col.bounds.max.y <= holderCamera.position.y - (0.15f * scale))
                    {
                        continue;
                    }

                    if (castHits[i].distance < closestDist)
                    {
                        closestDist = castHits[i].distance;
                        closestHit = castHits[i];
                        foundObstacle = true;
                    }
                }

                if (foundObstacle)
                {
                    float safeDist = Mathf.Max(0.55f * scale, closestDist - (horizontalRadius * 0.4f));
                    desiredCenter = holderCamera.position + castDir * safeDist + closestHit.normal * (horizontalRadius * 0.3f);
                }
            }

            // 3. Chống xuyên mặt bàn / xe đẩy / mặt sàn: Tìm mặt phẳng cao nhất ngay dưới vật để đặt đáy vật nằm TRÊN mặt đó
            float checkTopY = Mathf.Max(holderCamera.position.y + (0.2f * scale), desiredCenter.y + propExtents.y);
            float supportSurfaceY = FindHighestSupportSurfaceY(desiredCenter.x, desiredCenter.z, checkTopY, scale);
            float minCenterY = supportSurfaceY + propExtents.y + (0.02f * scale);

            if (desiredCenter.y < minCenterY)
            {
                desiredCenter.y = minCenterY;
            }

            Vector3 targetPos = desiredCenter + centerToPivotOffset;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followSpeed);
        }

        private void UpdateThrownFlight()
        {
            float scale = GetWorldScale();
            float dt = Time.deltaTime;
            throwTimer += dt;

            throwVelocity += Vector3.down * (throwGravity * scale * dt);
            Vector3 currentCenter = GetWorldCenter();
            Vector3 step = throwVelocity * dt;
            float stepDist = step.magnitude;

            // Kiểm tra va chạm với Quái vật hoặc Tường/Sàn trên đường bay
            if (stepDist > 0.0001f)
            {
                Vector3 dir = step / stepDist;
                int hitCount = Physics.SphereCastNonAlloc(
                    currentCenter,
                    horizontalRadius * 0.85f,
                    dir,
                    castHits,
                    stepDist + (0.15f * scale),
                    ~0,
                    QueryTriggerInteraction.Collide);

                for (int i = 0; i < hitCount; i++)
                {
                    Collider col = castHits[i].collider;
                    if (IsIgnoredCollider(col))
                    {
                        continue;
                    }

                    MonsterAI monster = col.GetComponentInParent<MonsterAI>();
                    if (monster != null && !monster.IsDead)
                    {
                        monster.TakeDamage(throwDamage);
                        Debug.Log($"[Throw] Ném {propName} trúng quái vật! Gây {throwDamage} sát thương.");
                        LandAfterThrow(currentCenter, scale);
                        return;
                    }
                }

                // Kiểm tra chạm Tường / Bàn / Sàn vật lý
                for (int i = 0; i < hitCount; i++)
                {
                    Collider col = castHits[i].collider;
                    if (IsIgnoredCollider(col) || col.isTrigger)
                    {
                        continue;
                    }

                    Vector3 impactCenter = currentCenter + dir * Mathf.Max(0f, castHits[i].distance - horizontalRadius * 0.3f);
                    LandAfterThrow(impactCenter, scale);
                    return;
                }
            }

            // Quét thêm vùng lân cận đề phòng quái đang lao tới nhanh
            int nearbyCount = Physics.OverlapSphereNonAlloc(currentCenter, horizontalRadius * 1.15f, overlapHits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < nearbyCount; i++)
            {
                Collider col = overlapHits[i];
                if (IsIgnoredCollider(col))
                {
                    continue;
                }

                MonsterAI monster = col.GetComponentInParent<MonsterAI>();
                if (monster != null && !monster.IsDead)
                {
                    monster.TakeDamage(throwDamage);
                    Debug.Log($"[Throw] Ném {propName} trúng quái vật! Gây {throwDamage} sát thương.");
                    LandAfterThrow(currentCenter, scale);
                    return;
                }
            }

            transform.position += step;

            // Giới hạn thời gian bay hoặc khi chạm xuống dưới sàn mặc định
            if (GetWorldCenter().y - propExtents.y <= defaultFloorY || throwTimer > 3.5f)
            {
                LandAfterThrow(GetWorldCenter(), scale);
            }
        }

        private void LandAfterThrow(Vector3 impactCenter, float scale)
        {
            isThrown = false;
            float checkTopY = impactCenter.y + propExtents.y + (0.6f * scale);
            float surfaceY = FindHighestSupportSurfaceY(impactCenter.x, impactCenter.z, checkTopY, scale);

            Vector3 landedCenter = impactCenter;
            landedCenter.y = surfaceY + propExtents.y;
            transform.position = landedCenter + centerToPivotOffset;

            SetCollidersEnabled(true);

            // Phát ra tiếng động thu hút hoặc đánh lạc hướng quái vật khi đồ vật rơi xuống
            GameEvents.RaiseNoiseEmitted(transform.position, 11f * scale);
        }

        private float FindHighestSupportSurfaceY(float worldX, float worldZ, float startTopY, float scale)
        {
            Vector3 rayOrigin = new Vector3(worldX, startTopY, worldZ);
            float maxRayDist = 3.5f * scale;
            int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, castHits, maxRayDist, ~0, QueryTriggerInteraction.Ignore);

            float highestY = defaultFloorY;
            bool foundSurface = false;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = castHits[i].collider;
                if (IsIgnoredCollider(col))
                {
                    continue;
                }

                // Chỉ nhận các bề mặt đỡ nằm ngang (mặt bàn, mặt xe đẩy, mặt giường, mặt sàn)
                if (castHits[i].normal.y < 0.45f)
                {
                    continue;
                }

                if (!foundSurface || castHits[i].point.y > highestY)
                {
                    highestY = castHits[i].point.y;
                    foundSurface = true;
                }
            }

            // Kiểm tra thêm bằng Bounds của các vật thể ngay bên dưới (chống lọt khe bàn/xe đẩy)
            Vector3 probeCenter = new Vector3(worldX, (startTopY + defaultFloorY) * 0.5f, worldZ);
            Vector3 probeHalfExtents = new Vector3(horizontalRadius * 0.55f, Mathf.Max(0.5f * scale, (startTopY - defaultFloorY) * 0.5f), horizontalRadius * 0.55f);
            int overlapCount = Physics.OverlapBoxNonAlloc(probeCenter, probeHalfExtents, overlapHits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < overlapCount; i++)
            {
                Collider col = overlapHits[i];
                if (IsIgnoredCollider(col))
                {
                    continue;
                }

                float topY = col.bounds.max.y;
                // Nếu vật cản là mặt bàn / xe đẩy thấp hơn tầm mắt người chơi -> lấy đỉnh của nó làm mặt đỡ
                if (topY <= startTopY - (0.05f * scale) && topY > highestY)
                {
                    highestY = topY;
                }
            }

            return highestY;
        }

        private bool IsIgnoredCollider(Collider col)
        {
            if (col == null)
            {
                return true;
            }

            if (col.transform.IsChildOf(transform))
            {
                return true;
            }

            if (currentInteractor != null && col.transform.IsChildOf(currentInteractor.transform))
            {
                return true;
            }

            return false;
        }

        private float GetWorldScale()
        {
            if (currentInteractor != null)
            {
                return Mathf.Max(1f, currentInteractor.transform.localScale.y);
            }
            return 20f;
        }

        private void SetCollidersEnabled(bool enabledState)
        {
            if (propColliders != null)
            {
                for (int i = 0; i < propColliders.Length; i++)
                {
                    if (propColliders[i] != null)
                    {
                        propColliders[i].enabled = enabledState;
                    }
                }
            }

            if (propNavObstacle != null)
            {
                if (enabledState && navObstacleTransform != null)
                {
                    navObstacleTransform.position = GetWorldCenter();
                }
                propNavObstacle.enabled = enabledState;
            }
        }
    }
}
