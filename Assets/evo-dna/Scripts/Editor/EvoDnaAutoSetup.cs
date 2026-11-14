#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using EvoDNA.Player;
using EvoDNA.AI;
using EvoDNA.Combat;
using EvoDNA.Interaction;
using EvoDNA.Systems;
using EvoDNA.UI;

namespace EvoDNA.EditorTools
{
    public static class EvoDnaAutoSetup
    {
        private const string MonsterFolder = "Assets/evo-dna/Monster";
        private const string ControllerPath = "Assets/evo-dna/Monster/ParasiteMonsterController.controller";

        [MenuItem("Evo-DNA/3. Áp dụng Setup Cửa Mẫu (BoxCollider Trigger + DoorController) cho toàn bộ Cửa", false, 3)]
        public static void MenuApplySampleDoorToAll()
        {
            if (Application.isPlaying)
            {
                return;
            }

            GameObject mapRoot = GameObject.Find("Demo_moitruong_Tang1_1211");
            if (mapRoot != null)
            {
                Transform[] allChildren = mapRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < allChildren.Length; i++)
                {
                    if (allChildren[i] != null)
                    {
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(allChildren[i].gameObject);
                    }
                }

                ApplySampleDoorSetupToAllDoors(mapRoot);
            }
        }

        private static void ApplySampleDoorSetupToAllDoors(GameObject mapRoot)
        {
            Transform[] allChildren = mapRoot.GetComponentsInChildren<Transform>(true);

            // Lấy thông số BoxCollider mẫu từ cánh cửa bạn vừa làm (SM_Door.001_M_TrashDoorDesk_0)
            Vector3 sampleDoorCenter = new Vector3(-0.00029303148f, 0f, 0.004503709f);
            Vector3 sampleDoorSize = new Vector3(0.014764979f, 0.018555328f, 0.009007419f);

            for (int i = 0; i < allChildren.Length; i++)
            {
                if (allChildren[i].name == "SM_Door.001_M_TrashDoorDesk_0")
                {
                    BoxCollider sampleBox = allChildren[i].GetComponent<BoxCollider>();
                    if (sampleBox != null)
                    {
                        sampleDoorCenter = sampleBox.center;
                        sampleDoorSize = sampleBox.size;
                    }
                    break;
                }
            }

            int updatedCount = 0;

            for (int i = 0; i < allChildren.Length; i++)
            {
                if (allChildren[i] == null)
                {
                    continue;
                }

                GameObject go = allChildren[i].gameObject;
                string n = go.name;
                Renderer rend = go.GetComponent<Renderer>();
                if (rend == null)
                {
                    continue;
                }

                bool isStandardDoor = n.StartsWith("SM_Door.001_M_TrashDoorDesk");
                bool isKeypadDoor = n == "Plane_handleBase_0" || HasMaterialNamed(rend, "keyPad");

                // Nếu trước đó lỡ gắn nhầm vào cửa thang máy / cửa trượt điện -> Xóa sạch DoorController, Trigger BoxCollider và hinge
                if (DoorController.IsElevatorOrSlidingDoor(go) || (!isStandardDoor && !isKeypadDoor))
                {
                    DoorController wrongDc = go.GetComponent<DoorController>();
                    if (wrongDc != null)
                    {
                        Object.DestroyImmediate(wrongDc);
                        BoxCollider[] boxes = go.GetComponents<BoxCollider>();
                        for (int b = 0; b < boxes.Length; b++)
                        {
                            if (boxes[b] != null && boxes[b].isTrigger)
                            {
                                Object.DestroyImmediate(boxes[b]);
                            }
                        }
                        Transform wrongHinge = go.transform.Find("hinge");
                        if (wrongHinge == null)
                        {
                            wrongHinge = go.transform.Find("Hinge");
                        }
                        if (wrongHinge != null)
                        {
                            Object.DestroyImmediate(wrongHinge.gameObject);
                        }
                        EditorUtility.SetDirty(go);
                        updatedCount++;
                    }
                    continue;
                }

                // Nếu là cửa mật mã Plane_handleBase_0 mà chưa gắn hoặc đang gắn sai loại -> cập nhật luôn
                DoorController existingDc = go.GetComponent<DoorController>();
                if (existingDc != null)
                {
                    Transform h = go.transform.Find("hinge");
                    if (h == null)
                    {
                        h = go.transform;
                    }
                    if (isKeypadDoor)
                    {
                        existingDc.ConfigureDoor(DoorType.Passcode, "Khóa Điện Tử", h, 90f, "1211");
                        EditorUtility.SetDirty(go);
                    }
                    continue;
                }

                // 1. Tìm object con 'hinge' nếu đã tạo sẵn, nếu chưa có thì dùng chính Transform của cửa (DoorController tự tính mép bản lề)
                Transform hingeTrans = go.transform.Find("hinge");
                if (hingeTrans == null)
                {
                    hingeTrans = go.transform.Find("Hinge");
                }
                if (hingeTrans == null)
                {
                    hingeTrans = go.transform;
                }

                // 2. Gắn BoxCollider (Is Trigger = true)
                BoxCollider box = go.GetComponent<BoxCollider>();
                if (box == null)
                {
                    box = go.AddComponent<BoxCollider>();
                }
                box.isTrigger = true;

                if (isStandardDoor)
                {
                    box.center = sampleDoorCenter;
                    box.size = sampleDoorSize;
                }
                else
                {
                    // Mở rộng độ dày vùng Trigger tương đương ~55 đơn vị thế giới để dễ bắt va chạm
                    Vector3 lossy = go.transform.lossyScale;
                    float scaleMag = Mathf.Max(1f, Mathf.Abs(lossy.x));
                    float triggerDepthLocal = 55f / scaleMag;
                    Vector3 sz = box.size;
                    if (sz.x < sz.z)
                    {
                        sz.x = Mathf.Max(sz.x, triggerDepthLocal);
                    }
                    else
                    {
                        sz.z = Mathf.Max(sz.z, triggerDepthLocal);
                    }
                    box.size = sz;
                }

                // 3. Gắn DoorController (Chỉ cửa Plane_handleBase_0 có bàn phím điện tử mới là Passcode "1211", còn lại là Normal)
                DoorController dc = go.AddComponent<DoorController>();
                DoorType dtype = isKeypadDoor ? DoorType.Passcode : DoorType.Normal;
                string title = isKeypadDoor ? "Cửa Khóa Điện Tử (DECK 5)" : "Cửa Phòng Lab";
                float angle = (n == "R_2" || n == "Door2") ? -90f : 90f;
                dc.ConfigureDoor(dtype, title, hingeTrans, angle, "1211");

                EditorUtility.SetDirty(go);
                updatedCount++;
            }

            if (updatedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"✅ [Evo-DNA] Đã áp dụng mẫu Cửa (hinge + BoxCollider Trigger + DoorController) cho {updatedCount} cánh cửa còn lại trong Map!");
            }
        }

        private static bool HasMaterialNamed(Renderer rend, string targetMatName)
        {
            if (rend == null || rend.sharedMaterials == null)
            {
                return false;
            }
            for (int i = 0; i < rend.sharedMaterials.Length; i++)
            {
                if (rend.sharedMaterials[i] != null && rend.sharedMaterials[i].name.StartsWith(targetMatName))
                {
                    return true;
                }
            }
            return false;
        }

        [MenuItem("Evo-DNA/1. Tự động Setup Toàn Bộ (Player POV + AI Quái + Cửa Tự Động)", false, 1)]
        public static void SetupSceneAndMonster()
        {
            // 1. Đảm bảo Animation Walking và Run được bật Loop Time
            EnsureAnimationLooping($"{MonsterFolder}/Parasite L Starkie@Mutant Walking.fbx", true);
            EnsureAnimationLooping($"{MonsterFolder}/Parasite L Starkie@Mutant Run.fbx", true);

            // 2. Tạo Animator Controller cho Quái vật với 5 Animation
            AnimatorController controller = CreateOrRebuildMonsterAnimatorController();

            // 3. Tìm Quái vật trong Scene và gắn đầy đủ AI + Collider + Animator
            GameObject monsterObj = GameObject.Find("Parasite L Starkie");
            float worldScale = 20f;

            if (monsterObj != null)
            {
                worldScale = Mathf.Max(1f, monsterObj.transform.localScale.y);

                Animator anim = monsterObj.GetComponent<Animator>();
                if (anim == null) anim = monsterObj.AddComponent<Animator>();
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;

                CapsuleCollider col = monsterObj.GetComponent<CapsuleCollider>();
                if (col == null) col = monsterObj.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 0.9f, 0f);
                col.radius = 0.45f;
                col.height = 1.8f;

                MonsterAI ai = monsterObj.GetComponent<MonsterAI>();
                if (ai == null) ai = monsterObj.AddComponent<MonsterAI>();

                NavMeshAgent navAgent = monsterObj.GetComponent<NavMeshAgent>();
                if (navAgent == null) navAgent = monsterObj.AddComponent<NavMeshAgent>();
                navAgent.agentTypeID = 0;
                navAgent.radius = 0.16f;
                navAgent.height = 1.6f;
                navAgent.baseOffset = 0f;
                navAgent.angularSpeed = 360f;
                navAgent.acceleration = 55f * worldScale;
                navAgent.stoppingDistance = 0.85f * worldScale;

                EditorUtility.SetDirty(monsterObj);
            }
            else
            {
                Debug.LogWarning("[Evo-DNA Setup] Không tìm thấy 'Parasite L Starkie' trong Scene hiện tại.");
            }

            // 4. Tạo hoặc cập nhật Nhân vật góc nhìn thứ nhất (Player_POV)
            SetupFirstPersonPlayer(monsterObj, worldScale);

            // 5. Tự động gắn Script Đóng/Mở cho toàn bộ Cửa & Tủ Locker trong Map
            SetupAllDoorsAndLockersInScene();

            // 6. Tự động Setup & Bake NavMesh chuẩn tỉ lệ 20x
            MenuSetupAndBakeNavMesh();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("✅ [Evo-DNA] Đã tự động Setup xong Player_POV, AI Quái vật (NavMesh) và Hệ thống Cửa! Hãy bấm nút PLAY để trải nghiệm ngay.");
        }

        [MenuItem("Evo-DNA/4. Tự động Setup & Bake NavMesh (Chuẩn tỉ lệ 20x - Chống đơ máy)", false, 4)]
        public static void MenuSetupAndBakeNavMesh()
        {
            if (Application.isPlaying)
            {
                return;
            }

            GameObject mapRoot = GameObject.Find("Demo_moitruong_Tang1_1211");
            if (mapRoot == null)
            {
                Debug.LogWarning("[Evo-DNA NavMesh] Không tìm thấy 'Demo_moitruong_Tang1_1211' trong Scene!");
                return;
            }

            GameObject monsterObj = GameObject.Find("Parasite L Starkie");
            float worldScale = monsterObj != null ? Mathf.Max(1f, monsterObj.transform.localScale.y) : 20f;

            if (monsterObj != null)
            {
                NavMeshAgent navAgent = monsterObj.GetComponent<NavMeshAgent>();
                if (navAgent == null) navAgent = monsterObj.AddComponent<NavMeshAgent>();
                navAgent.agentTypeID = 0;
                navAgent.radius = 0.16f;
                navAgent.height = 1.6f;
                navAgent.baseOffset = 0f;
                navAgent.angularSpeed = 360f;
                navAgent.acceleration = 55f * worldScale;
                navAgent.stoppingDistance = 0.85f * worldScale;
                EditorUtility.SetDirty(monsterObj);
            }

            NavMeshSurface surface = mapRoot.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = mapRoot.AddComponent<NavMeshSurface>();
            }

            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 1.5f; // Tỉ lệ 20x: Voxel = 1.5 giúp thông mọi khung cửa hẹp và Bake trong ~1 giây

            // Tạm tắt Collider của Cửa và Đồ vật kéo/đẩy trong lúc Bake tĩnh để đường đi nối thông qua các khung cửa
            // (Khi vào game, Cửa đang đóng và Đồ vật chèn cửa sẽ tự khoét NavMesh bằng NavMeshObstacle Carving)
            DoorController[] doors = Object.FindObjectsByType<DoorController>(FindObjectsSortMode.None);
            DraggableProp[] props = Object.FindObjectsByType<DraggableProp>(FindObjectsSortMode.None);
            List<Collider> tempDisabled = new List<Collider>(128);

            for (int i = 0; i < doors.Length; i++)
            {
                Collider[] cols = doors[i].GetComponentsInChildren<Collider>();
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null && cols[c].enabled)
                    {
                        cols[c].enabled = false;
                        tempDisabled.Add(cols[c]);
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
                        tempDisabled.Add(cols[c]);
                    }
                }
            }

            surface.BuildNavMesh();

            for (int i = 0; i < tempDisabled.Count; i++)
            {
                if (tempDisabled[i] != null)
                {
                    tempDisabled[i].enabled = true;
                }
            }

            EditorUtility.SetDirty(mapRoot);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("✅ [Evo-DNA NavMesh] Đã Bake thành công NavMesh chuẩn tỉ lệ 20x cho toàn bộ Tầng 1 và gắn NavMeshAgent cho Quái vật!");
        }

        [MenuItem("Evo-DNA/2. Chỉ gắn Cửa Tự Động & Tủ Ẩn Nấp cho Map Tầng 1", false, 2)]
        public static void SetupAllDoorsAndLockersInScene()
        {
            GameObject mapRoot = GameObject.Find("Demo_moitruong_Tang1_1211");
            if (mapRoot == null)
            {
                Debug.LogWarning("[Evo-DNA Setup] Không tìm thấy 'Demo_moitruong_Tang1_1211' trong Scene!");
                return;
            }

            Transform[] allChildren = mapRoot.GetComponentsInChildren<Transform>(true);
            int doorCount = 0;
            int lockerCount = 0;
            int dragCount = 0;
            int pickupCount = 0;

            for (int i = 0; i < allChildren.Length; i++)
            {
                if (allChildren[i] == null)
                {
                    continue;
                }

                GameObject go = allChildren[i].gameObject;
                string n = go.name;
                Renderer rend = go.GetComponent<Renderer>();
                if (rend == null)
                {
                    continue;
                }

                string matName = rend.sharedMaterial != null ? rend.sharedMaterial.name : string.Empty;
                bool isCombinedLargeMesh = rend.bounds.extents.x > 30f || rend.bounds.extents.z > 30f;

                // Gắn HidingSpot (Phím F ẩn nấp) cho Tủ Locker, Giường bệnh và Bàn mổ
                if (n.StartsWith("SM_Locker_M_Locker") || matName == "M_Locker")
                {
                    EnsureProperColliderForProp(go, isCombinedLargeMesh);
                    HidingSpot hs = go.GetComponent<HidingSpot>();
                    if (hs == null) hs = go.AddComponent<HidingSpot>();
                    hs.ConfigureSpot("Tủ Sắt Locker", false);
                    EditorUtility.SetDirty(go);
                    lockerCount++;
                    continue;
                }

                if (n.StartsWith("SM_Couch") || matName == "M_Mattress" || matName == "M_ouch" || n == "DissectionTable_Main_20" || n == "Object_32")
                {
                    EnsureProperColliderForProp(go, isCombinedLargeMesh);
                    HidingSpot hs = go.GetComponent<HidingSpot>();
                    if (hs == null) hs = go.AddComponent<HidingSpot>();
                    string spotTitle = (n == "DissectionTable_Main_20" || n == "Object_32") ? "Gầm Bàn Mổ" : "Gầm Giường";
                    hs.ConfigureSpot(spotTitle, true);
                    EditorUtility.SetDirty(go);
                    lockerCount++;
                    continue;
                }

                // 3. Gắn DraggableProp (Phím F cầm / di chuyển đồ vật) cho Bình gas, Thùng rác, Ghế, Cây truyền dịch, Khay, Gối
                string propTitle = null;
                if (n.StartsWith("SM_Ballons") || matName == "M_Ballons")
                    propTitle = "Bình Gas";
                else if (n.StartsWith("SM_TrashContainer") || matName == "M_TrashDoorDesk.002")
                    propTitle = "Thùng Rác";
                else if (n.StartsWith("SM_Chair2") || n.StartsWith("Ghe_bang"))
                    propTitle = "Ghế";
                else if (n.StartsWith("SM_Drip") || matName == "M_DripChair.001" || matName == "M_DripChair.002")
                    propTitle = "Cây Truyền Dịch";
                else if (n.StartsWith("Sofa_pillows") || matName == "Pillows_mat")
                    propTitle = "Gối";
                else if (n.StartsWith("SM_Tray"))
                    propTitle = "Khay Y Tế";

                if (propTitle != null)
                {
                    EnsureProperColliderForProp(go, isCombinedLargeMesh);
                    if (isCombinedLargeMesh)
                    {
                        // Không gắn DraggableProp lên Mesh gộp trải dài qua nhiều phòng
                        DraggableProp oldDp = go.GetComponent<DraggableProp>();
                        if (oldDp != null) Object.DestroyImmediate(oldDp);
                        continue;
                    }

                    DraggableProp dp = go.GetComponent<DraggableProp>();
                    if (dp == null) dp = go.AddComponent<DraggableProp>();
                    dp.SetPropName(propTitle);
                    EditorUtility.SetDirty(go);
                    dragCount++;
                    continue;
                }

                // 4. Gắn PickupItem (Phím F nhặt vật phẩm vào túi đồ) cho Hộp thuốc, Ống nghiệm, Tài liệu
                if (n.StartsWith("Vial") || n == "Thuoc" || matName == "M_MedicineBox3" || n.StartsWith("Informartion_paper"))
                {
                    if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
                    if (go.GetComponent<PickupItem>() == null) go.AddComponent<PickupItem>();
                    EditorUtility.SetDirty(go);
                    pickupCount++;
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"✅ [Evo-DNA] Đã gắn: {doorCount} Cửa tự động, {lockerCount} Điểm ẩn nấp, {dragCount} Đồ vật kéo/đẩy, và {pickupCount} Vật phẩm nhặt vào túi đồ!");
        }

        private static void EnsureAnimationLooping(string fbxAssetPath, bool loop)
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            bool changed = false;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i].loopTime != loop)
                {
                    clips[i].loopTime = loop;
                    changed = true;
                }
            }

            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        private static AnimatorController CreateOrRebuildMonsterAnimatorController()
        {
            if (File.Exists(ControllerPath))
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("SwipeAttack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("JumpAttack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            AnimationClip walkClip = LoadClipFromFbx($"{MonsterFolder}/Parasite L Starkie@Mutant Walking.fbx");
            AnimationClip runClip = LoadClipFromFbx($"{MonsterFolder}/Parasite L Starkie@Mutant Run.fbx");
            AnimationClip swipeClip = LoadClipFromFbx($"{MonsterFolder}/Parasite L Starkie@Mutant Swiping.fbx");
            AnimationClip jumpClip = LoadClipFromFbx($"{MonsterFolder}/Parasite L Starkie@Jump Attack.fbx");
            AnimationClip dieClip = LoadClipFromFbx($"{MonsterFolder}/Parasite L Starkie@Mutant Dying.fbx");

            AnimatorState walkState = sm.AddState("Mutant Walking");
            walkState.motion = walkClip;
            sm.defaultState = walkState;

            AnimatorState runState = sm.AddState("Mutant Run");
            runState.motion = runClip;

            AnimatorState swipeState = sm.AddState("Mutant Swiping");
            swipeState.motion = swipeClip;

            AnimatorState jumpState = sm.AddState("Jump Attack");
            jumpState.motion = jumpClip;

            AnimatorState dieState = sm.AddState("Mutant Dying");
            dieState.motion = dieClip;

            // Chuyển trạng thái Walk <-> Run
            AnimatorStateTransition toRun = walkState.AddTransition(runState);
            toRun.hasExitTime = false;
            toRun.duration = 0.15f;
            toRun.AddCondition(AnimatorConditionMode.Greater, 0.75f, "Speed");

            AnimatorStateTransition toWalk = runState.AddTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.15f;
            toWalk.AddCondition(AnimatorConditionMode.Less, 0.75f, "Speed");

            // Đòn đánh Swipe
            AnimatorStateTransition anyToSwipe = sm.AddAnyStateTransition(swipeState);
            anyToSwipe.hasExitTime = false;
            anyToSwipe.duration = 0.1f;
            anyToSwipe.AddCondition(AnimatorConditionMode.If, 0f, "SwipeAttack");

            AnimatorStateTransition swipeBack = swipeState.AddTransition(walkState);
            swipeBack.hasExitTime = true;
            swipeBack.exitTime = 0.9f;
            swipeBack.duration = 0.15f;

            // Đòn vồ Jump Attack
            AnimatorStateTransition anyToJump = sm.AddAnyStateTransition(jumpState);
            anyToJump.hasExitTime = false;
            anyToJump.duration = 0.1f;
            anyToJump.AddCondition(AnimatorConditionMode.If, 0f, "JumpAttack");

            AnimatorStateTransition jumpBack = jumpState.AddTransition(walkState);
            jumpBack.hasExitTime = true;
            jumpBack.exitTime = 0.9f;
            jumpBack.duration = 0.15f;

            // Trạng thái Chết (Dying)
            AnimatorStateTransition anyToDie = sm.AddAnyStateTransition(dieState);
            anyToDie.hasExitTime = false;
            anyToDie.duration = 0.1f;
            anyToDie.canTransitionToSelf = false;
            anyToDie.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");

            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationClip LoadClipFromFbx(string fbxPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }
            return null;
        }

        private static void SetupFirstPersonPlayer(GameObject monsterObj, float worldScale)
        {
            GameObject playerObj = GameObject.Find("Player_POV");
            if (playerObj == null)
            {
                playerObj = new GameObject("Player_POV");
            }

            playerObj.tag = "Player";
            playerObj.transform.localScale = new Vector3(worldScale, worldScale, worldScale);

            if (monsterObj != null)
            {
                // Đặt Player cách Quái vật một khoảng an toàn để test ngay
                playerObj.transform.position = monsterObj.transform.position + new Vector3(0f, 0.5f, -8f * worldScale);
                playerObj.transform.LookAt(new Vector3(monsterObj.transform.position.x, playerObj.transform.position.y, monsterObj.transform.position.z));
            }
            else
            {
                playerObj.transform.position = new Vector3(0f, 3.5f, -40f);
            }

            CharacterController cc = playerObj.GetComponent<CharacterController>();
            if (cc == null) cc = playerObj.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.38f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;

            // Gắn Camera vào đầu Player_POV
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                mainCam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }

            mainCam.transform.SetParent(playerObj.transform, false);
            mainCam.transform.localPosition = new Vector3(0f, 1.58f, 0f);
            mainCam.transform.localRotation = Quaternion.identity;
            mainCam.transform.localScale = Vector3.one;
            mainCam.nearClipPlane = 0.1f;
            mainCam.farClipPlane = 2500f;

            // Tạo Spot Light làm Đèn Pin gắn theo Camera
            Transform flashlightTrans = mainCam.transform.Find("Flashlight_SpotLight");
            GameObject flashlightObj;
            if (flashlightTrans == null)
            {
                flashlightObj = new GameObject("Flashlight_SpotLight");
                flashlightObj.transform.SetParent(mainCam.transform, false);
            }
            else
            {
                flashlightObj = flashlightTrans.gameObject;
            }

            flashlightObj.transform.localPosition = new Vector3(0.15f, -0.1f, 0.1f);
            flashlightObj.transform.localRotation = Quaternion.identity;

            Light spotLight = flashlightObj.GetComponent<Light>();
            if (spotLight == null) spotLight = flashlightObj.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.range = 25f * worldScale;
            spotLight.spotAngle = 62f;
            spotLight.innerSpotAngle = 35f;
            spotLight.intensity = 4.5f * worldScale;
            spotLight.shadows = LightShadows.Soft;

            // Gắn các Script hệ thống của Player
            if (playerObj.GetComponent<FirstPersonController>() == null)
                playerObj.AddComponent<FirstPersonController>();
            if (playerObj.GetComponent<FlashlightController>() == null)
                playerObj.AddComponent<FlashlightController>();
            PlayerHealth health = playerObj.GetComponent<PlayerHealth>();
            if (health == null) health = playerObj.AddComponent<PlayerHealth>();
            Texture2D bloodTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/evo-dna/UI/blood2.png");
            if (bloodTex != null)
            {
                health.SetBloodTexture(bloodTex);
            }
            if (playerObj.GetComponent<DNAAbilitySystem>() == null)
                playerObj.AddComponent<DNAAbilitySystem>();
            if (playerObj.GetComponent<KnifeWeapon>() == null)
                playerObj.AddComponent<KnifeWeapon>();
            if (playerObj.GetComponent<HandgunWeapon>() == null)
                playerObj.AddComponent<HandgunWeapon>();
            if (playerObj.GetComponent<InteractionRaycast>() == null)
                playerObj.AddComponent<InteractionRaycast>();
            PhysicsGrabber oldGrabber = playerObj.GetComponent<PhysicsGrabber>();
            if (oldGrabber != null)
                Object.DestroyImmediate(oldGrabber);
            if (playerObj.GetComponent<InventoryManager>() == null)
                playerObj.AddComponent<InventoryManager>();
            if (playerObj.GetComponent<GameHUDManager>() == null)
                playerObj.AddComponent<GameHUDManager>();

            Selection.activeGameObject = playerObj;
            EditorUtility.SetDirty(playerObj);
        }

        private static void EnsureProperColliderForProp(GameObject go, bool isCombinedLargeMesh)
        {
            if (isCombinedLargeMesh)
            {
                // Đối với Mesh gộp nhiều phòng (như SM_Couch_M_Mattress_0), tuyệt đối không dùng BoxCollider vì hộp bao sẽ tràn ra hành lang
                BoxCollider[] boxes = go.GetComponents<BoxCollider>();
                for (int i = 0; i < boxes.Length; i++)
                {
                    Object.DestroyImmediate(boxes[i]);
                }

                if (go.GetComponent<MeshCollider>() == null)
                {
                    MeshFilter mf = go.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        MeshCollider mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }
            }
            else
            {
                if (go.GetComponent<Collider>() == null)
                {
                    go.AddComponent<BoxCollider>();
                }
            }
        }
    }
}
#endif
