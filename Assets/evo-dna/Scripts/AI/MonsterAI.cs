using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using EvoDNA.Systems;
using EvoDNA.Interaction;
using EvoDNA.Player;

namespace EvoDNA.AI
{
    public enum MonsterVariant
    {
        Crawler_Floor1 = 0,     // Nhạy tiếng động, tầm nhìn ngắn, cho năng lực Dash
        Infiltrator_Floor2 = 1  // Tàng hình trong bóng tối, lộ diện khi chiếu đèn UV, cho năng lực Cloak
    }

    public enum MonsterState
    {
        Patrol = 0,
        Investigate = 1,
        Chase = 2,
        Attack = 3,
        Dead = 4
    }

    [RequireComponent(typeof(Animator))]
    public class MonsterAI : MonoBehaviour, IInteractable
    {
        // Cached Animator Hashes (Zero GC)
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int SwipeAttackHash = Animator.StringToHash("SwipeAttack");
        private static readonly int JumpAttackHash = Animator.StringToHash("JumpAttack");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");

        [Header("Monster Classification (GDD)")]
        [SerializeField] private MonsterVariant variant = MonsterVariant.Crawler_Floor1;
        [SerializeField] private float maxHealth = 90f;
        [SerializeField] private float currentHealth = 90f;

        [Header("Ground Alignment (Chống lún chân xuống sàn)")]
        [SerializeField] private float footGroundOffset = 0.06f;

        [Header("Movement & Patrol (Base 1m Scale)")]
        [SerializeField] private float walkSpeed = 2.0f;
        [SerializeField] private float chaseSpeed = 5.2f;
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private float patrolRadius = 8f;
        [SerializeField] private float waitAtPointTime = 2.0f;

        [Header("Sensors (Base 1m Scale)")]
        [SerializeField] private float sightRange = 9f;
        [SerializeField] private float sightAngle = 110f;
        [SerializeField] private float hearingMultiplier = 1.35f;

        [Header("Combat (Base 1m Scale)")]
        [SerializeField] private float swipeRange = 2.1f;
        [SerializeField] private float jumpAttackMinRange = 3.2f;
        [SerializeField] private float jumpAttackMaxRange = 5.5f;
        [SerializeField] private float attackDamage = 25f;
        [SerializeField] private float attackCooldown = 1.8f;

        private Animator animator;
        private NavMeshAgent navAgent;
        private Collider selfCollider;
        private Transform playerTransform;
        private PlayerHealth playerHealth;
        private Renderer[] meshRenderers;

        private Transform leftFootBone;
        private Transform rightFootBone;
        private Transform leftToeBone;
        private Transform rightToeBone;
        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private readonly RaycastHit[] sightHits = new RaycastHit[8];
        private readonly RaycastHit[] obstacleHits = new RaycastHit[8];
        private readonly List<Vector3> discoveredWaypoints = new List<Vector3>(64);
        private NavMeshPath cachedNavPath;

        private MonsterState currentState = MonsterState.Patrol;
        private Vector3 spawnOrigin;
        private Vector3 targetDestination;
        private Vector3 previousPatrolPoint;
        private Vector3 lastProgressCheckPos;
        private float progressCheckTimer;
        private float stuckTimer;
        private int currentPatrolIndex;
        private float stateTimer;
        private float lostSightTimer;
        private float lastAttackTime;
        private bool isPlayerHidden;
        private bool isDnaAbsorbed;
        private bool isRevealedByUV;

        public bool IsDead => currentState == MonsterState.Dead;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            navAgent = GetComponent<NavMeshAgent>();
            selfCollider = GetComponent<Collider>();
            meshRenderers = GetComponentsInChildren<Renderer>();
            spawnOrigin = transform.position;
            lastProgressCheckPos = transform.position;
            currentHealth = maxHealth;
            cachedNavPath = new NavMeshPath();

            if (animator != null)
            {
                animator.applyRootMotion = false;
                if (animator.isHuman)
                {
                    leftFootBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    rightFootBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    leftToeBone = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                    rightToeBone = animator.GetBoneTransform(HumanBodyBones.RightToes);
                }
            }
        }

        private static NavMeshDataInstance runtimeNavMeshInstance;

        private void Start()
        {
            FirstPersonController player = FindFirstObjectByType<FirstPersonController>();
            if (player != null)
            {
                playerTransform = player.transform;
                playerHealth = player.GetComponent<PlayerHealth>();
            }

            float scale = Mathf.Max(1f, transform.localScale.y);
            InitializeNavMeshAndAgent(scale);
            DiscoverWalkableWaypointsAcrossMap(scale);

            // Nếu NavMesh cũ trong Scene bị nghẽn ở cửa phòng (quái đứng trong phòng mà có ít hơn 5 điểm xa > 10m nối thông),
            // tự động dựng lại NavMesh chuẩn bán kính nhỏ (0.14 * scale) để thông toàn bộ cửa phòng với hành lang!
            if (CountDistantReachableWaypoints(scale) < 5)
            {
                BuildRuntimeNavMeshForMap(scale);
                if (navAgent != null && NavMesh.SamplePosition(transform.position, out NavMeshHit reHit, 12f * scale, NavMesh.AllAreas))
                {
                    navAgent.Warp(reHit.position);
                }
                DiscoverWalkableWaypointsAcrossMap(scale);
            }

            PickNextPatrolPoint();
        }

        private int CountDistantReachableWaypoints(float scale)
        {
            int reachable = 0;
            float minDistSqr = (10f * scale) * (10f * scale);
            for (int i = 0; i < discoveredWaypoints.Count; i++)
            {
                Vector3 diff = discoveredWaypoints[i] - transform.position;
                diff.y = 0f;
                if (diff.sqrMagnitude >= minDistSqr && IsPointReachableWithCompletePath(discoveredWaypoints[i]))
                {
                    reachable++;
                }
            }
            return reachable;
        }

        private void DiscoverWalkableWaypointsAcrossMap(float scale)
        {
            discoveredWaypoints.Clear();
            GameObject mapRoot = GameObject.Find("Demo_moitruong_Tang1_1211");
            Bounds scanBounds = new Bounds(spawnOrigin, new Vector3(45f * scale, 6f * scale, 45f * scale));

            if (mapRoot != null)
            {
                Renderer[] rends = mapRoot.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    scanBounds = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++)
                    {
                        scanBounds.Encapsulate(rends[i].bounds);
                    }
                }
            }

            float step = 5.5f * scale;
            float baseY = transform.position.y;
            float minClearanceFromWall = 0.75f * scale;

            for (float x = scanBounds.min.x + step * 0.5f; x <= scanBounds.max.x - step * 0.5f; x += step)
            {
                for (float z = scanBounds.min.z + step * 0.5f; z <= scanBounds.max.z - step * 0.5f; z += step)
                {
                    Vector3 sampleTest = new Vector3(x, baseY, z);
                    if (NavMesh.SamplePosition(sampleTest, out NavMeshHit hit, 3.5f * scale, NavMesh.AllAreas))
                    {
                        // Chỉ nhận các điểm nằm cùng tầng độ cao với quái vật
                        if (Mathf.Abs(hit.position.y - baseY) > 1.8f * scale)
                        {
                            continue;
                        }

                        Vector3 safePt = hit.position;
                        // Đẩy điểm tuần tra ra giữa hành lang/phòng để quái không bao giờ đi sát mép tường
                        if (NavMesh.FindClosestEdge(safePt, out NavMeshHit edgeHit, NavMesh.AllAreas))
                        {
                            if (edgeHit.distance < minClearanceFromWall)
                            {
                                Vector3 pushed = safePt + edgeHit.normal * (minClearanceFromWall - edgeHit.distance + 0.25f * scale);
                                if (NavMesh.SamplePosition(pushed, out NavMeshHit pushedHit, 1.5f * scale, NavMesh.AllAreas))
                                {
                                    safePt = pushedHit.position;
                                }
                                else
                                {
                                    continue;
                                }
                            }
                        }

                        discoveredWaypoints.Add(safePt);
                    }
                }
            }
        }

        private void InitializeNavMeshAndAgent(float scale)
        {
            // Nếu Scene chưa có dữ liệu NavMesh được Bake sẵn, tự động dựng nhanh NavMesh chuẩn tỉ lệ 20x (~0.15s)
            if (!NavMesh.SamplePosition(transform.position, out _, 15f * scale, NavMesh.AllAreas))
            {
                BuildRuntimeNavMeshForMap(scale);
            }

            if (navAgent == null)
            {
                navAgent = GetComponent<NavMeshAgent>();
            }
            if (navAgent == null)
            {
                navAgent = gameObject.AddComponent<NavMeshAgent>();
            }

            navAgent.agentTypeID = 0;
            navAgent.radius = 0.15f;
            navAgent.height = 1.6f;
            navAgent.baseOffset = 0f;
            navAgent.angularSpeed = 360f;
            navAgent.acceleration = 55f * scale;
            navAgent.stoppingDistance = 0.85f * scale;
            navAgent.autoBraking = true;
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

            // Tắt updatePosition/updateRotation tự động để đồng bộ mượt với hệ thống chống lún chân AlignFeetToFloor
            navAgent.updatePosition = false;
            navAgent.updateRotation = false;
            navAgent.enabled = true;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f * scale, NavMesh.AllAreas))
            {
                navAgent.Warp(hit.position);
            }
        }

        public static void BuildRuntimeNavMeshForMap(float scale)
        {
            GameObject mapRoot = GameObject.Find("Demo_moitruong_Tang1_1211");
            Transform collectRoot = mapRoot != null ? mapRoot.transform : null;

            // Tạm ẩn Collider của Cửa và Đồ vật kéo đẩy trong 1 tích tắc khi lấy mẫu mặt sàn tĩnh
            // để đường đi qua các khung cửa luôn thông suốt (Cửa đóng và Đồ vật sẽ tự cắt lưới bằng NavMeshObstacle).
            DoorController[] doors = FindObjectsByType<DoorController>(FindObjectsSortMode.None);
            DraggableProp[] props = FindObjectsByType<DraggableProp>(FindObjectsSortMode.None);
            List<Collider> tempDisabledColliders = new List<Collider>(128);

            for (int i = 0; i < doors.Length; i++)
            {
                Collider[] cols = doors[i].GetComponentsInChildren<Collider>();
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null && cols[c].enabled)
                    {
                        cols[c].enabled = false;
                        tempDisabledColliders.Add(cols[c]);
                    }
                }
            }

            for (int i = 0; i < props.Length; i++)
            {
                Collider[] cols = props[i].GetComponentsInChildren<Collider>();
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null && cols[c].enabled)
                    {
                        cols[c].enabled = false;
                        tempDisabledColliders.Add(cols[c]);
                    }
                }
            }

            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
            List<NavMeshBuildMarkup> markups = new List<NavMeshBuildMarkup>();

            NavMeshBuilder.CollectSources(
                collectRoot,
                ~0,
                NavMeshCollectGeometry.PhysicsColliders,
                0,
                markups,
                sources
            );

            for (int i = 0; i < tempDisabledColliders.Count; i++)
            {
                if (tempDisabledColliders[i] != null)
                {
                    tempDisabledColliders[i].enabled = true;
                }
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.14f * scale;
            settings.agentHeight = 1.6f * scale;
            settings.agentClimb = 0.38f * scale;
            settings.agentSlope = 45f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.075f * scale; // Tương đương ~1.5 đơn vị thế giới -> Thông suốt mọi khung cửa hẹp và Bake trong 0.15s
            settings.minRegionArea = 8f;

            Bounds worldBounds = new Bounds(Vector3.zero, new Vector3(3000f, 1000f, 3000f));
            if (mapRoot != null)
            {
                Renderer[] rends = mapRoot.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    worldBounds = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++)
                    {
                        worldBounds.Encapsulate(rends[i].bounds);
                    }
                    worldBounds.Expand(50f);
                }
            }

            NavMeshData navData = NavMeshBuilder.BuildNavMeshData(
                settings,
                sources,
                worldBounds,
                Vector3.zero,
                Quaternion.identity
            );

            if (navData != null)
            {
                if (runtimeNavMeshInstance.valid)
                {
                    NavMesh.RemoveNavMeshData(runtimeNavMeshInstance);
                }
                runtimeNavMeshInstance = NavMesh.AddNavMeshData(navData);
            }
        }

        private void OnEnable()
        {
            GameEvents.OnNoiseEmitted += HandleNoiseHeard;
            GameEvents.OnPlayerHiddenChanged += HandlePlayerHiddenChanged;
            GameEvents.OnUVLightUpdated += HandleUVLightUpdated;
        }

        private void OnDisable()
        {
            GameEvents.OnNoiseEmitted -= HandleNoiseHeard;
            GameEvents.OnPlayerHiddenChanged -= HandlePlayerHiddenChanged;
            GameEvents.OnUVLightUpdated -= HandleUVLightUpdated;
        }

        private void Update()
        {
            if (currentState == MonsterState.Dead)
            {
                return;
            }

            UpdateInfiltratorVisibility();

            float scale = Mathf.Max(0.1f, transform.localScale.y);

            if (CanSeePlayer(scale))
            {
                currentState = MonsterState.Chase;
            }

            switch (currentState)
            {
                case MonsterState.Patrol:
                    UpdatePatrol(scale);
                    break;
                case MonsterState.Investigate:
                    UpdateInvestigate(scale);
                    break;
                case MonsterState.Chase:
                    UpdateChase(scale);
                    break;
                case MonsterState.Attack:
                    UpdateAttack(scale);
                    break;
            }
        }

        private void LateUpdate()
        {
            float scale = Mathf.Max(0.1f, transform.localScale.y);
            AlignFeetToFloor(scale);
        }

        private void AlignFeetToFloor(float scale)
        {
            // Bắn tia từ ngực quái vật xuống dưới để tìm đúng mặt sàn
            Vector3 rayOrigin = transform.position + Vector3.up * (1.2f * scale);
            int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, groundHits, 4.0f * scale);

            float groundY = float.MinValue;
            for (int i = 0; i < hitCount; i++)
            {
                Collider c = groundHits[i].collider;
                if (c == null || c == selfCollider || c.transform.IsChildOf(transform))
                {
                    continue;
                }
                if (playerTransform != null && c.transform.IsChildOf(playerTransform))
                {
                    continue;
                }
                if (groundHits[i].point.y > groundY)
                {
                    groundY = groundHits[i].point.y;
                }
            }

            if (groundY == float.MinValue)
            {
                return;
            }

            float targetFloorY = groundY + (footGroundOffset * scale);

            // Kiểm tra vị trí xương bàn chân / mũi chân thấp nhất sau khi Animator đã chạy
            float lowestBoneY = float.MaxValue;
            if (leftFootBone != null && leftFootBone.position.y < lowestBoneY) lowestBoneY = leftFootBone.position.y;
            if (rightFootBone != null && rightFootBone.position.y < lowestBoneY) lowestBoneY = rightFootBone.position.y;
            if (leftToeBone != null && leftToeBone.position.y < lowestBoneY) lowestBoneY = leftToeBone.position.y;
            if (rightToeBone != null && rightToeBone.position.y < lowestBoneY) lowestBoneY = rightToeBone.position.y;

            if (lowestBoneY < float.MaxValue)
            {
                float correctionY = targetFloorY - lowestBoneY;
                Vector3 pos = transform.position;
                pos.y += correctionY;
                transform.position = pos;
            }
            else
            {
                Vector3 pos = transform.position;
                pos.y = targetFloorY;
                transform.position = pos;
            }
        }

        private void UpdatePatrol(float scale)
        {
            if (HasReachedOrBlockedOnPath(scale, 1.25f * scale))
            {
                StopMovement();
                stateTimer += Time.deltaTime;
                // Xoay người quan sát 2 bên hành lang/phòng tự nhiên khi đứng nghỉ tuần tra
                transform.Rotate(0f, Mathf.Sin(stateTimer * 3.0f) * 48f * Time.deltaTime, 0f);

                if (stateTimer >= waitAtPointTime)
                {
                    stateTimer = 0f;
                    PickNextPatrolPoint();
                }
                return;
            }

            MoveTowards(targetDestination, walkSpeed * scale, scale, 0.5f);
        }

        private void UpdateInvestigate(float scale)
        {
            if (HasReachedOrBlockedOnPath(scale, 1.45f * scale))
            {
                StopMovement();
                stateTimer += Time.deltaTime;
                // Quay đầu dò xét xung quanh kỹ hơn khi tới điểm có tiếng động hoặc dấu vết người chơi
                transform.Rotate(0f, Mathf.Sin(stateTimer * 3.8f) * 65f * Time.deltaTime, 0f);

                if (stateTimer >= 3.0f)
                {
                    stateTimer = 0f;
                    currentState = MonsterState.Patrol;
                    PickNextPatrolPoint();
                }
                return;
            }

            MoveTowards(targetDestination, walkSpeed * 1.35f * scale, scale, 0.65f);
        }

        private bool HasReachedOrBlockedOnPath(float scale, float fallbackThreshold)
        {
            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
            {
                if (!navAgent.pathPending && navAgent.remainingDistance <= navAgent.stoppingDistance + (0.35f * scale))
                {
                    return true;
                }
                if (!navAgent.pathPending &&
                    (navAgent.pathStatus == NavMeshPathStatus.PathInvalid || navAgent.pathStatus == NavMeshPathStatus.PathPartial) &&
                    navAgent.remainingDistance <= 2.2f * scale)
                {
                    return true;
                }
            }

            Vector3 flatDiff = targetDestination - transform.position;
            flatDiff.y = 0f;
            return flatDiff.sqrMagnitude <= fallbackThreshold * fallbackThreshold;
        }

        private void UpdateChase(float scale)
        {
            if (playerTransform == null || isPlayerHidden)
            {
                currentState = MonsterState.Investigate;
                stateTimer = 0f;
                return;
            }

            bool hasLOS = HasClearLineOfSightToPlayer(scale);
            if (hasLOS)
            {
                lostSightTimer = 0f;
                targetDestination = playerTransform.position;
            }
            else
            {
                lostSightTimer += Time.deltaTime;
                // Trong 2.5 giây đầu sau khi khuất góc tường, NavMeshAgent vẫn tiếp tục truy đuổi theo đường đi qua các khúc cua
                if (lostSightTimer <= 2.5f)
                {
                    targetDestination = playerTransform.position;
                }
                else
                {
                    lostSightTimer = 0f;
                    currentState = MonsterState.Investigate;
                    stateTimer = 0f;
                    return;
                }
            }

            Vector3 diff = playerTransform.position - transform.position;
            diff.y = 0f;
            float distSqr = diff.sqrMagnitude;

            float scaledSwipe = swipeRange * scale;
            float scaledJumpMin = jumpAttackMinRange * scale;
            float scaledJumpMax = jumpAttackMaxRange * scale;

            if (hasLOS && distSqr <= scaledSwipe * scaledSwipe)
            {
                currentState = MonsterState.Attack;
                return;
            }

            if (hasLOS &&
                distSqr >= scaledJumpMin * scaledJumpMin &&
                distSqr <= scaledJumpMax * scaledJumpMax &&
                Time.time - lastAttackTime >= attackCooldown * 1.5f)
            {
                PerformAttack(isJumpAttack: true);
                return;
            }

            MoveTowards(targetDestination, chaseSpeed * scale, scale, 1.0f);
        }

        private void UpdateAttack(float scale)
        {
            if (playerTransform == null || isPlayerHidden || !HasClearLineOfSightToPlayer(scale))
            {
                currentState = MonsterState.Investigate;
                stateTimer = 0f;
                return;
            }

            StopMovement();

            Vector3 lookDir = playerTransform.position - transform.position;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 8f);
            }

            float scaledSwipe = swipeRange * 1.15f * scale;
            if (lookDir.sqrMagnitude > scaledSwipe * scaledSwipe)
            {
                currentState = MonsterState.Chase;
                return;
            }

            if (Time.time - lastAttackTime >= attackCooldown)
            {
                PerformAttack(isJumpAttack: false);
            }
        }

        private void PerformAttack(bool isJumpAttack)
        {
            lastAttackTime = Time.time;
            animator.SetTrigger(isJumpAttack ? JumpAttackHash : SwipeAttackHash);

            if (playerHealth != null && !isPlayerHidden)
            {
                playerHealth.TakeDamage(attackDamage);
            }
        }

        private void MoveTowards(Vector3 destination, float worldSpeed, float scale, float normalizedAnimSpeed)
        {
            Vector3 desiredMoveDir = Vector3.zero;

            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
            {
                navAgent.isStopped = false;
                navAgent.speed = worldSpeed;
                navAgent.SetDestination(destination);

                // Chỉ dùng hướng dẫn của NavMeshAgent khi đường đi tới đích thực sự thông suốt (PathComplete)
                // hoặc còn đoạn đường dài hợp lệ. Nếu NavMesh bị ngắt cục bộ tại bậu cửa (PathPartial),
                // cho phép quái dùng cảm biến tia 3 hướng đi thẳng qua khung cửa mở ra ngoài hành lang!
                if (!navAgent.pathPending && navAgent.pathStatus == NavMeshPathStatus.PathComplete)
                {
                    if (navAgent.remainingDistance <= navAgent.stoppingDistance + (0.25f * scale))
                    {
                        animator.SetFloat(SpeedHash, 0f);
                        navAgent.nextPosition = transform.position;
                        stuckTimer = 0f;
                        return;
                    }

                    Vector3 toSteering = navAgent.steeringTarget - transform.position;
                    toSteering.y = 0f;
                    if (toSteering.sqrMagnitude > 0.01f)
                    {
                        desiredMoveDir = toSteering.normalized;
                    }
                    else
                    {
                        Vector3 vel = navAgent.desiredVelocity;
                        vel.y = 0f;
                        if (vel.sqrMagnitude > 0.01f)
                        {
                            desiredMoveDir = vel.normalized;
                        }
                    }
                }
            }

            if (desiredMoveDir.sqrMagnitude <= 0.001f)
            {
                Vector3 dir = destination - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    desiredMoveDir = dir.normalized;
                }
            }

            if (desiredMoveDir.sqrMagnitude <= 0.001f)
            {
                animator.SetFloat(SpeedHash, 0f);
                return;
            }

            // Kết hợp Cảm biến 3 tia (Trước - Trái 42° - Phải 42°) để ôm cua mượt qua khung cửa và không bao giờ đâm đầu vào tường
            Vector3 steeredDir = SteerWithWhiskerSensors(desiredMoveDir, worldSpeed, scale, out bool hardBlocked);
            if (hardBlocked)
            {
                animator.SetFloat(SpeedHash, 0f);
                if (currentState == MonsterState.Patrol)
                {
                    PickNextPatrolPoint();
                }
                else if (currentState == MonsterState.Investigate)
                {
                    currentState = MonsterState.Patrol;
                    PickNextPatrolPoint();
                }
                return;
            }

            animator.SetFloat(SpeedHash, normalizedAnimSpeed);
            transform.position += steeredDir * (worldSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(steeredDir), Time.deltaTime * 8f);

            if (navAgent != null && navAgent.isActiveAndEnabled)
            {
                if (navAgent.isOnNavMesh)
                {
                    navAgent.nextPosition = transform.position;
                }
                else if (NavMesh.SamplePosition(transform.position, out NavMeshHit reWarpHit, 4f * scale, NavMesh.AllAreas))
                {
                    navAgent.Warp(reWarpHit.position);
                }
            }

            // Bộ phát hiện kẹt tường (Anti-Stuck): Nếu sau 0.9s đang đi mà quái bị vướng góc tường không nhích được -> lập tức đổi hướng tuần tra khác
            progressCheckTimer += Time.deltaTime;
            if (progressCheckTimer >= 0.3f)
            {
                Vector3 moved = transform.position - lastProgressCheckPos;
                moved.y = 0f;
                if (moved.magnitude < 0.18f * scale * progressCheckTimer)
                {
                    stuckTimer += progressCheckTimer;
                    if (stuckTimer >= 0.9f)
                    {
                        stuckTimer = 0f;
                        if (currentState == MonsterState.Patrol || currentState == MonsterState.Investigate)
                        {
                            currentState = MonsterState.Patrol;
                            PickNextPatrolPoint();
                        }
                    }
                }
                else
                {
                    stuckTimer = 0f;
                }

                lastProgressCheckPos = transform.position;
                progressCheckTimer = 0f;
            }
        }

        private Vector3 SteerWithWhiskerSensors(Vector3 moveDir, float worldSpeed, float scale, out bool hardBlocked)
        {
            hardBlocked = false;
            Vector3 castOrigin = transform.position + Vector3.up * (0.95f * scale);
            float castRadius = 0.34f * scale;
            float lookAheadDist = Mathf.Max(0.95f * scale, worldSpeed * Time.deltaTime * 3.0f);

            if (!CheckObstacleInDirection(castOrigin, castRadius, moveDir, lookAheadDist, out Vector3 hitNormal, out _))
            {
                return moveDir;
            }

            // Phía trước có tường/vật cản: Bắn 2 tia râu cảm biến sang Trái (-45°) và Phải (+45°) để chọn hướng rẽ thông thoáng hơn
            Vector3 leftDir = (Quaternion.Euler(0f, -45f, 0f) * moveDir).normalized;
            Vector3 rightDir = (Quaternion.Euler(0f, 45f, 0f) * moveDir).normalized;
            float whiskerDist = lookAheadDist * 1.45f;

            bool leftBlocked = CheckObstacleInDirection(castOrigin, castRadius * 0.85f, leftDir, whiskerDist, out _, out float leftClearDist);
            bool rightBlocked = CheckObstacleInDirection(castOrigin, castRadius * 0.85f, rightDir, whiskerDist, out _, out float rightClearDist);

            if (!leftBlocked && rightBlocked)
            {
                return leftDir;
            }
            if (!rightBlocked && leftBlocked)
            {
                return rightDir;
            }
            if (!leftBlocked && !rightBlocked)
            {
                return leftClearDist >= rightClearDist ? leftDir : rightDir;
            }

            // Nếu cả 2 bên đều hẹp, trượt dọc theo mặt phẳng tường
            hitNormal.y = 0f;
            if (hitNormal.sqrMagnitude > 0.001f)
            {
                Vector3 slideDir = Vector3.ProjectOnPlane(moveDir, hitNormal.normalized);
                if (slideDir.sqrMagnitude > 0.05f)
                {
                    Vector3 normalizedSlide = slideDir.normalized;
                    if (!CheckObstacleInDirection(castOrigin, castRadius * 0.8f, normalizedSlide, lookAheadDist * 0.75f, out _, out _))
                    {
                        return normalizedSlide;
                    }
                }
            }

            hardBlocked = true;
            return moveDir;
        }

        private bool CheckObstacleInDirection(Vector3 origin, float shoulderWidth, Vector3 dir, float maxDist, out Vector3 normal, out float clearDist)
        {
            normal = Vector3.zero;
            clearDist = maxDist;

            // Sử dụng 3 tia Raycast song song (Giữa ngực + Vai trái + Vai phải) thay cho SphereCast bán kính lớn
            // để khi quái đứng sát tường trong phòng hẹp KHÔNG bị lỗi SphereCast chạm tường phía lưng ở khoảng cách 0!
            Vector3 rightPerp = new Vector3(dir.z, 0f, -dir.x).normalized * (shoulderWidth * 0.65f);
            bool found = false;

            for (int r = -1; r <= 1; r++)
            {
                Vector3 rayStart = origin + rightPerp * r;
                int hitCount = Physics.RaycastNonAlloc(rayStart, dir, obstacleHits, maxDist, ~0, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < hitCount; i++)
                {
                    Collider c = obstacleHits[i].collider;
                    if (c == null || c == selfCollider || c.transform.IsChildOf(transform))
                    {
                        continue;
                    }
                    if (playerTransform != null && (c.transform == playerTransform || c.transform.IsChildOf(playerTransform)))
                    {
                        continue;
                    }
                    // Bỏ qua mặt sàn hoặc các mặt phẳng không hướng ngược chiều di chuyển
                    if (obstacleHits[i].normal.y > 0.65f || obstacleHits[i].distance <= 0.005f)
                    {
                        continue;
                    }
                    if (Vector3.Dot(obstacleHits[i].normal, dir) > -0.05f)
                    {
                        continue;
                    }
                    if (obstacleHits[i].distance < clearDist)
                    {
                        clearDist = obstacleHits[i].distance;
                        normal = obstacleHits[i].normal;
                        found = true;
                    }
                }
            }

            return found;
        }

        private void StopMovement()
        {
            animator.SetFloat(SpeedHash, 0f);
            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
            {
                navAgent.isStopped = true;
            }
        }

        private void PickNextPatrolPoint()
        {
            stuckTimer = 0f;
            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                Transform pt = patrolPoints[currentPatrolIndex % patrolPoints.Length];
                currentPatrolIndex++;
                if (pt != null)
                {
                    previousPatrolPoint = targetDestination;
                    targetDestination = pt.position;
                    return;
                }
            }

            float scale = Mathf.Max(0.1f, transform.localScale.y);
            float longRangeDistSqr = (11.0f * scale) * (11.0f * scale);

            // 1. Ưu tiên số 1: Chọn điểm tuần tra ở XA (> 11m) sang các phòng khác / dọc hành lang có đường đi thông suốt (PathComplete)
            //    để quái luôn tự đi ra khỏi phòng hiện tại và tuần tra rộng khắp bản đồ!
            if (discoveredWaypoints.Count > 0)
            {
                int count = discoveredWaypoints.Count;
                int startIdx = Random.Range(0, count);

                for (int i = 0; i < count; i++)
                {
                    Vector3 candidate = discoveredWaypoints[(startIdx + i) % count];
                    Vector3 diffFromCurrent = candidate - transform.position;
                    diffFromCurrent.y = 0f;
                    if (diffFromCurrent.sqrMagnitude < longRangeDistSqr)
                    {
                        continue;
                    }

                    Vector3 diffFromPrev = candidate - previousPatrolPoint;
                    diffFromPrev.y = 0f;
                    if (diffFromPrev.sqrMagnitude < (6.0f * scale) * (6.0f * scale))
                    {
                        continue;
                    }

                    if (IsPointReachableWithCompletePath(candidate))
                    {
                        previousPatrolPoint = targetDestination;
                        targetDestination = candidate;
                        return;
                    }
                }
            }

            // 2. Nếu bạn đặt quái ở trong một phòng nhỏ mà chưa thấy điểm xa trên NavMesh:
            //    Quét 16 tia Radar 360 độ (tầm xa 32m) để tìm đúng Khe Cửa Mở / Lối Hành Lang dài nhất dẫn ra khỏi phòng!
            Vector3 castOrigin = transform.position + Vector3.up * (0.95f * scale);
            float bestScore = -1f;
            Vector3 bestExitPoint = transform.position + transform.forward * (6f * scale);
            Vector3 dirToPrev = previousPatrolPoint - transform.position;
            dirToPrev.y = 0f;

            for (int a = 0; a < 16; a++)
            {
                float angle = a * 22.5f + Random.Range(-6f, 6f);
                Vector3 testDir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                CheckObstacleInDirection(castOrigin, 0.28f * scale, testDir, 32f * scale, out _, out float clearDist);

                // Ưu tiên hướng có khoảng trống dài nhất (xuyên qua cửa mở ra hành lang) và hạn chế đi ngược lại chỗ vừa đứng
                float score = clearDist;
                if (dirToPrev.sqrMagnitude > 1f && Vector3.Dot(testDir, dirToPrev.normalized) > 0.65f)
                {
                    score *= 0.45f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    float walkDist = Mathf.Clamp(clearDist * 0.82f, 4f * scale, 26f * scale);
                    bestExitPoint = transform.position + testDir * walkDist;
                }
            }

            if (NavMesh.SamplePosition(bestExitPoint, out NavMeshHit exitNavHit, 6f * scale, NavMesh.AllAreas))
            {
                bestExitPoint = exitNavHit.position;
            }

            previousPatrolPoint = targetDestination;
            targetDestination = bestExitPoint;
        }

        private bool IsPointReachableWithCompletePath(Vector3 targetPt)
        {
            if (cachedNavPath == null)
            {
                cachedNavPath = new NavMeshPath();
            }

            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
            {
                if (navAgent.CalculatePath(targetPt, cachedNavPath))
                {
                    return cachedNavPath.status == NavMeshPathStatus.PathComplete;
                }
            }
            else if (NavMesh.CalculatePath(transform.position, targetPt, NavMesh.AllAreas, cachedNavPath))
            {
                return cachedNavPath.status == NavMeshPathStatus.PathComplete;
            }

            return false;
        }

        private bool CanSeePlayer(float scale)
        {
            if (playerTransform == null || isPlayerHidden)
            {
                return false;
            }

            Vector3 toPlayer = playerTransform.position - transform.position;
            toPlayer.y = 0f;
            float maxSight = sightRange * scale;

            if (toPlayer.sqrMagnitude > maxSight * maxSight)
            {
                return false;
            }

            float angle = Vector3.Angle(transform.forward, toPlayer);
            if (angle > sightAngle * 0.5f)
            {
                return false;
            }

            return HasClearLineOfSightToPlayer(scale);
        }

        private bool HasClearLineOfSightToPlayer(float scale)
        {
            if (playerTransform == null)
            {
                return false;
            }

            // Kiểm tra Line-of-Sight (Tầm nhìn thực tế): Không nhìn/đánh xuyên tường hoặc cửa đang đóng
            Vector3 eyePos = transform.position + Vector3.up * (1.45f * scale);
            Vector3 targetPos = playerTransform.position + Vector3.up * (1.0f * scale);
            Vector3 rayDir = targetPos - eyePos;
            float dist = rayDir.magnitude;
            if (dist <= 0.01f)
            {
                return true;
            }

            int hitCount = Physics.RaycastNonAlloc(eyePos, rayDir / dist, sightHits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider c = sightHits[i].collider;
                if (c == null || c == selfCollider || c.transform.IsChildOf(transform))
                {
                    continue;
                }
                if (c.transform == playerTransform || c.transform.IsChildOf(playerTransform))
                {
                    continue;
                }
                // Có tường/cửa/vật cản chắn giữa mắt quái vật và người chơi
                return false;
            }

            return true;
        }

        private void HandleNoiseHeard(Vector3 noisePos, float noiseRadius)
        {
            if (currentState == MonsterState.Dead || currentState == MonsterState.Chase || currentState == MonsterState.Attack)
            {
                return;
            }

            float effectiveRadius = noiseRadius * hearingMultiplier;
            if ((noisePos - transform.position).sqrMagnitude <= effectiveRadius * effectiveRadius)
            {
                targetDestination = noisePos;
                stateTimer = 0f;
                currentState = MonsterState.Investigate;
            }
        }

        private void HandlePlayerHiddenChanged(bool hidden)
        {
            isPlayerHidden = hidden;
        }

        private void HandleUVLightUpdated(bool isUVActive, Transform lightTransform, float range, float spotAngle)
        {
            if (variant != MonsterVariant.Infiltrator_Floor2 || lightTransform == null)
            {
                isRevealedByUV = true;
                return;
            }

            if (!isUVActive)
            {
                isRevealedByUV = false;
                return;
            }

            Vector3 toMonster = (transform.position + Vector3.up * transform.localScale.y) - lightTransform.position;
            if (toMonster.sqrMagnitude > range * range)
            {
                isRevealedByUV = false;
                return;
            }

            float angle = Vector3.Angle(lightTransform.forward, toMonster);
            isRevealedByUV = angle <= spotAngle * 0.6f;
        }

        private void UpdateInfiltratorVisibility()
        {
            if (variant != MonsterVariant.Infiltrator_Floor2 || meshRenderers == null)
            {
                return;
            }

            bool shouldShow = isRevealedByUV || currentState == MonsterState.Attack || currentState == MonsterState.Dead;
            for (int i = 0; i < meshRenderers.Length; i++)
            {
                if (meshRenderers[i] != null)
                {
                    meshRenderers[i].enabled = shouldShow;
                }
            }
        }

        public void TakeDamage(float damage)
        {
            if (currentState == MonsterState.Dead)
            {
                return;
            }

            currentHealth -= damage;
            if (currentHealth <= 0f)
            {
                Die();
            }
            else
            {
                currentState = MonsterState.Chase;
            }
        }

        private void Die()
        {
            currentState = MonsterState.Dead;
            StopMovement();
            animator.SetBool(IsDeadHash, true);
            UpdateInfiltratorVisibility();
        }

        public string GetInteractPrompt()
        {
            if (currentState != MonsterState.Dead || isDnaAbsorbed)
            {
                return string.Empty;
            }

            string abilityName = variant == MonsterVariant.Crawler_Floor1 ? "Lướt Nhanh (Dash)" : "Tàng Hình (Cloak)";
            return $"[Click / F] Hấp thụ DNA: {abilityName}";
        }

        public void Interact(GameObject interactor)
        {
            if (currentState != MonsterState.Dead || isDnaAbsorbed || interactor == null)
            {
                return;
            }

            DNAAbilitySystem dnaSystem = interactor.GetComponent<DNAAbilitySystem>();
            if (dnaSystem != null)
            {
                MonsterAbilityType ability = variant == MonsterVariant.Crawler_Floor1
                    ? MonsterAbilityType.CrawlerDash
                    : MonsterAbilityType.InfiltratorCloak;
                dnaSystem.CopyAbility(ability);
                isDnaAbsorbed = true;
            }
        }
    }
}
