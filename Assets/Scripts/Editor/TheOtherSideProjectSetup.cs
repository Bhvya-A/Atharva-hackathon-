#if UNITY_EDITOR
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheOtherSide.EditorTools
{
    /// <summary>
    /// One-shot project authoring helper. The runtime still assembles the arena, but the
    /// Canvas root, managers, spawn markers and the enemy/player prefabs are authored
    /// assets so they can be inspected and edited before pressing Play.
    /// </summary>
    public static class TheOtherSideProjectSetup
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string PlayerPrefabPath = "Assets/Resources/Player/Player.prefab";
        const string GhostPrefabPath = "Assets/Resources/Enemy/Ghost.prefab";
        const string GhostVisualPath = "Assets/Resources/Enemy/GhostVisual.prefab";

        [MenuItem("The Other Side/Setup Project Content", priority = 0)]
        public static void RunAll()
        {
            EnsureFolders();
            CreatePlayerPrefab();
            CreateGhostPrefab();
            SetupScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("TheOtherSide setup: prefabs, scene HUD, managers and spawn points are ready.");
        }

        [MenuItem("The Other Side/Import TMP Essentials", priority = 1)]
        public static void EnsureTmpEssentials()
        {
            string settingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(settingsPath) != null)
            {
                Debug.Log("TheOtherSide setup: TMP Essential Resources already present.");
                return;
            }

            string[] packages = Directory.GetFiles(
                "Library/PackageCache",
                "TMP Essential Resources.unitypackage",
                SearchOption.AllDirectories);

            if (packages.Length == 0)
            {
                Debug.LogError("TheOtherSide setup: TMP Essential Resources.unitypackage was not found in Library/PackageCache.");
                return;
            }

            AssetDatabase.ImportPackage(packages[0], false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("TheOtherSide setup: imported TMP Essential Resources from " + packages[0]);
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Player");
            EnsureFolder("Assets/Resources/Enemy");
            EnsureFolder("Assets/Resources/UI");
            EnsureFolder("Assets/Resources/UI/Fonts");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        static void CreatePlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null)
            {
                Debug.Log("TheOtherSide setup: Player prefab already exists, leaving it untouched.");
                return;
            }

            GameObject root = new GameObject("Player");
            root.tag = "Player";

            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.3f;
            controller.minMoveDistance = 0f;

            Transform pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 1.6f, 0f);
            pivot.localRotation = Quaternion.identity;

            FirstPersonController motor = root.AddComponent<FirstPersonController>();
            motor.Configure(pivot);

            root.AddComponent<PlayerHealth>();
            root.AddComponent<TimeFreezeAbility>();
            root.AddComponent<PlayerInteractor>();
            root.AddComponent<PlayerRelicCarrier>();

            // The CameraShake component is added to the scene camera by GameBootstrap,
            // because the main camera is not part of the player prefab.
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("TheOtherSide setup: created " + PlayerPrefabPath);
        }

        static void CreateGhostPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath) != null)
            {
                Debug.Log("TheOtherSide setup: Ghost prefab already exists, leaving it untouched.");
                return;
            }

            GameObject root = new GameObject("Ghost");

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.8f;
            agent.acceleration = 10f;
            agent.stoppingDistance = 1.15f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            CapsuleCollider body = root.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 0.9f, 0f);
            body.height = 1.8f;
            body.radius = 0.4f;

            GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GhostVisualPath);
            if (visualPrefab != null)
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, root.transform);
                visual.name = "Visual";
                visual.transform.localPosition = new Vector3(0f, 0.085f, 0f);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * 0.85f;

                Animator animator = visual.GetComponent<Animator>();
                if (animator != null)
                    animator.applyRootMotion = false;
            }
            else
            {
                Debug.LogWarning("TheOtherSide setup: GhostVisual prefab not found; the Ghost prefab has no visible model.");
            }

            root.AddComponent<GhostAI>();
            root.AddComponent<DimensionPresence>().Configure(DimensionMembership.Both);

            PrefabUtility.SaveAsPrefabAsset(root, GhostPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("TheOtherSide setup: created " + GhostPrefabPath);
        }

        static void SetupScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene);

                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            SetupSystems();
            SetupHud();
            SetupEventSystem();
            SetupSpawnPoints();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("TheOtherSide setup: saved scene content in " + ScenePath);
        }

        static void SetupSystems()
        {
            GameObject systems = GameObject.Find("GameSystems");
            if (systems == null)
                systems = new GameObject("GameSystems");

            if (systems.GetComponent<GameBootstrap>() == null)
                systems.AddComponent<GameBootstrap>();
            if (systems.GetComponent<GameManager>() == null)
                systems.AddComponent<GameManager>();
            if (systems.GetComponent<DimensionManager>() == null)
                systems.AddComponent<DimensionManager>();

            if (systems.transform.Find("RuntimeNavMeshSurface") == null)
            {
                GameObject nav = new GameObject("RuntimeNavMeshSurface");
                nav.transform.SetParent(systems.transform, false);
                NavMeshSurface surface = nav.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.All;
            }
        }

        static void SetupHud()
        {
            GameObject hud = GameObject.Find("HUD");
            if (hud == null)
            {
                hud = new GameObject(
                    "HUD",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster),
                    typeof(HUDController));
            }

            Canvas canvas = hud.GetComponent<Canvas>();
            if (canvas == null)
                canvas = hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = hud.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = hud.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (hud.GetComponent<GraphicRaycaster>() == null)
                hud.AddComponent<GraphicRaycaster>();
            if (hud.GetComponent<HUDController>() == null)
                hud.AddComponent<HUDController>();
        }

        static void SetupEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }

        static void SetupSpawnPoints()
        {
            GameObject root = GameObject.Find("SpawnPoints");
            if (root == null)
                root = new GameObject("SpawnPoints");

            CreateMarker(root.transform, "PlayerSpawn", new Vector3(0f, 0.1f, 0f), Quaternion.identity);
            CreateMarker(root.transform, "GhostSpawn", new Vector3(0f, 0f, 12f), Quaternion.LookRotation(Vector3.forward));
            CreateMarker(root.transform, "RespawnPoint", new Vector3(0f, 0.1f, 0f), Quaternion.identity);

            Transform patrol = root.transform.Find("GhostPatrol");
            if (patrol == null)
            {
                GameObject patrolGo = new GameObject("GhostPatrol");
                patrolGo.transform.SetParent(root.transform, false);
                patrol = patrolGo.transform;
            }

            Vector3 home = new Vector3(0f, 0f, 12f);
            Vector3[] offsets =
            {
                new Vector3(4.5f, 0f, 1.5f),
                new Vector3(-4f, 0f, 3f),
                new Vector3(3f, 0f, -3.5f),
                new Vector3(-3.5f, 0f, -1.5f)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                string name = $"PatrolPoint_{i + 1}";
                Transform point = patrol.Find(name);
                if (point == null)
                {
                    GameObject go = new GameObject(name);
                    go.transform.SetParent(patrol, false);
                    point = go.transform;
                }

                point.position = home + offsets[i];
            }
        }

        static void CreateMarker(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            Transform marker = parent.Find(name);
            if (marker == null)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                marker = go.transform;
            }

            marker.SetPositionAndRotation(position, rotation);
        }
    }
}
#endif
