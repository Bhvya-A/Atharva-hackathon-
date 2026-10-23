using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace TheOtherSide
{
    /// <summary>
    /// Builds a testable first-person setup in SampleScene at play time.
    /// Existing lighting/camera are reused; a floor, player, HUD, and sample interactable are added.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class GameBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (Object.FindAnyObjectByType<GameBootstrap>() != null ||
                Object.FindAnyObjectByType<GameManager>() != null)
                return;

            string scene = SceneManager.GetActiveScene().name;
            if (scene != "SampleScene")
                return;

            new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
        }

        void Awake()
        {
            GameInput.Initialize();

            if (GameManager.Instance == null)
                new GameObject("GameManager").AddComponent<GameManager>();

            if (DimensionManager.Instance == null)
                new GameObject("DimensionManager").AddComponent<DimensionManager>();

            CreateGround();
            CreateLosOccluder();
            BakeRuntimeNavMesh();
            FirstPersonController player = CreatePlayer();
            CreateHud();
            CreateSampleInteractable(player.transform.position + player.transform.forward * 3f + Vector3.up * 0.5f);
            CreateDimensionTestObjects(player.transform.position);
            CreateGhost(player);
            CreateRelicGameplay(player);
            DimensionManager.Instance?.RefreshPresence();
            EnsureEventSystem();
        }

        static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
        }

        static FirstPersonController CreatePlayer()
        {
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 0.1f, 0f);

            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;
            cc.minMoveDistance = 0f;

            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }

            Transform pivot = cam.transform;
            pivot.SetParent(player.transform, false);
            pivot.localPosition = new Vector3(0f, 1.6f, 0f);
            pivot.localRotation = Quaternion.identity;

            FirstPersonController controller = player.AddComponent<FirstPersonController>();
            controller.Configure(pivot);

            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            interactor.Configure(cam);

            return controller;
        }

        static void CreateHud()
        {
            if (HUDController.Instance != null)
                return;

            GameObject hud = new GameObject("HUD");
            HUDController controller = hud.AddComponent<HUDController>();
            controller.BuildDefaultUI();
        }

        static void CreateSampleInteractable(Vector3 position)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "SampleInteractable";
            cube.transform.position = position;
            cube.AddComponent<SimpleInteractable>();
            cube.AddComponent<DimensionPresence>().Configure(DimensionMembership.Both);
        }

        static void CreateDimensionTestObjects(Vector3 playerPosition)
        {
            CreateDimensionMarker(
                "RealityMarker",
                playerPosition + new Vector3(-2.2f, 0.75f, 4f),
                new Color(0.82f, 0.72f, 0.45f),
                DimensionMembership.Reality);

            CreateDimensionMarker(
                "VoidMarker",
                playerPosition + new Vector3(2.2f, 0.75f, 4f),
                new Color(0.45f, 0.18f, 0.72f),
                DimensionMembership.Void);
        }

        static void CreateDimensionMarker(string name, Vector3 position, Color color, DimensionMembership membership)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name;
            marker.transform.position = position;
            marker.transform.localScale = new Vector3(0.9f, 1.5f, 0.9f);
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = color;

            marker.AddComponent<DimensionPresence>().Configure(membership);
        }

        static void CreateLosOccluder()
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "GhostLosWall";
            wall.transform.position = new Vector3(0f, 1.5f, 6f);
            wall.transform.localScale = new Vector3(8f, 3f, 0.4f);
            Renderer renderer = wall.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(0.32f, 0.32f, 0.36f);
        }

        static void BakeRuntimeNavMesh()
        {
            NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface == null)
            {
                GameObject go = new GameObject("RuntimeNavMeshSurface");
                surface = go.AddComponent<NavMeshSurface>();
            }

            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();

            if (!NavMesh.SamplePosition(Vector3.zero, out _, 4f, NavMesh.AllAreas))
            {
                Debug.LogWarning(
                    "GameBootstrap: runtime NavMesh bake produced no walkable surface near the origin. " +
                    "GhostAI will use fallback movement. Bake a NavMesh Surface in the Editor for authored levels.");
            }
        }

        static void CreateGhost(FirstPersonController player)
        {
            if (Object.FindAnyObjectByType<GhostAI>(FindObjectsInactive.Include) != null)
                return;

            Vector3 spawn = new Vector3(0f, 0f, 12f);
            if (player != null)
            {
                Vector3 playerPos = player.transform.position;
                playerPos.y = 0f;
                if (Vector3.Distance(new Vector3(spawn.x, 0f, spawn.z), playerPos) < 6f)
                    spawn = playerPos + Vector3.forward * 8f;
            }

            GameObject ghost = new GameObject("Ghost");
            ghost.transform.position = spawn;
            ghost.transform.rotation = Quaternion.LookRotation(Vector3.forward);

            NavMeshAgent agent = ghost.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.8f;
            agent.acceleration = 10f;
            agent.stoppingDistance = 1.15f;
            agent.autoBraking = true;

            if (NavMesh.SamplePosition(spawn, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                agent.Warp(hit.position);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Visual";
            body.transform.SetParent(ghost.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.85f, 1f, 0.85f);
            SetTriggerColor(body, new Color(0.5f, 0.05f, 0.12f));

            GameObject eyes = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eyes.name = "Eyes";
            eyes.transform.SetParent(ghost.transform, false);
            eyes.transform.localPosition = new Vector3(0f, 1.45f, 0.28f);
            eyes.transform.localScale = new Vector3(0.5f, 0.14f, 0.14f);
            SetTriggerColor(eyes, new Color(0.2f, 0.95f, 0.35f));

            Transform[] points = CreatePatrolPoints(spawn);
            GhostAI ai = ghost.AddComponent<GhostAI>();
            ai.Configure(player != null ? player.transform : null, points);
            ghost.AddComponent<DimensionPresence>().Configure(DimensionMembership.Both);
        }

        static Transform[] CreatePatrolPoints(Vector3 home)
        {
            Transform folder = new GameObject("GhostPatrolPoints").transform;
            Vector3[] offsets =
            {
                new Vector3(4.5f, 0f, 1.5f),
                new Vector3(-4f, 0f, 3f),
                new Vector3(3f, 0f, -3.5f),
                new Vector3(-3.5f, 0f, -1.5f)
            };

            var points = new Transform[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                Transform point = new GameObject($"PatrolPoint_{i + 1}").transform;
                point.SetParent(folder, true);
                point.position = home + offsets[i];
                points[i] = point;
            }

            return points;
        }

        static void CreateRelicGameplay(FirstPersonController player)
        {
            if (player == null)
                return;

            if (player.GetComponent<PlayerRelicCarrier>() == null)
                player.gameObject.AddComponent<PlayerRelicCarrier>();

            // Skip the whole setup if an authored or previously spawned loop is already present.
            if (Object.FindAnyObjectByType<RitualTable>(FindObjectsInactive.Include) != null ||
                Object.FindAnyObjectByType<RelicKeyContainer>(FindObjectsInactive.Include) != null ||
                Object.FindAnyObjectByType<RelicKeyPickup>(FindObjectsInactive.Include) != null ||
                Object.FindAnyObjectByType<RelicPickup>(FindObjectsInactive.Include) != null)
                return;

            GameObject root = new GameObject("RelicGameplay");

            CreateRitualTable(root.transform, new Vector3(0f, 0f, -6f));

            CreateFreeRelic(
                root.transform,
                "Relic_Tide",
                "Tide Relic",
                new Vector3(-9f, 0f, -7f),
                PrimitiveType.Sphere,
                new Color(0.1f, 0.85f, 1f),
                0.55f);

            CreateFreeRelic(
                root.transform,
                "Relic_Ember",
                "Ember Relic",
                new Vector3(9f, 0f, -7f),
                PrimitiveType.Cube,
                new Color(1f, 0.35f, 0.08f),
                0.5f);

            CreateFreeRelic(
                root.transform,
                "Relic_Thorn",
                "Thorn Relic",
                new Vector3(0f, 0f, -12f),
                PrimitiveType.Capsule,
                new Color(0.35f, 1f, 0.3f),
                0.45f);

            CreateLockedContainer(root.transform, new Vector3(6.5f, 0f, -3.5f));
            CreateRustyKey(root.transform, new Vector3(-6.5f, 0f, -3.5f));
        }

        static void CreateRitualTable(Transform parent, Vector3 worldPosition)
        {
            GameObject table = new GameObject("RitualTable");
            table.transform.SetParent(parent, false);
            table.transform.position = worldPosition;
            table.AddComponent<RitualTable>();
            table.AddComponent<DimensionPresence>().Configure(DimensionMembership.Reality);

            GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "TableTop";
            top.transform.SetParent(table.transform, false);
            top.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            top.transform.localScale = new Vector3(2.4f, 0.2f, 1.4f);
            SetColor(top, new Color(0.3f, 0.28f, 0.34f));

            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? -0.9f : 0.9f;
                float z = i % 2 == 0 ? -0.45f : 0.45f;

                GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leg.name = $"Leg_{i + 1}";
                leg.transform.SetParent(table.transform, false);
                leg.transform.localPosition = new Vector3(x, 0.4f, z);
                leg.transform.localScale = new Vector3(0.15f, 0.8f, 0.15f);
                SetColor(leg, new Color(0.22f, 0.2f, 0.25f));
            }

            GameObject rune = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rune.name = "Rune";
            rune.transform.SetParent(table.transform, false);
            rune.transform.localPosition = new Vector3(0f, 1.12f, 0f);
            rune.transform.localScale = Vector3.one * 0.4f;
            SetGlowColor(rune, new Color(0.35f, 0.85f, 1f));
        }

        static void CreateFreeRelic(
            Transform parent,
            string objectName,
            string displayName,
            Vector3 worldPosition,
            PrimitiveType shape,
            Color color,
            float scale)
        {
            GameObject display = new GameObject($"{objectName}_Display");
            display.transform.SetParent(parent, false);
            display.transform.position = worldPosition;
            display.AddComponent<DimensionPresence>().Configure(DimensionMembership.Void);

            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "Pedestal";
            pedestal.transform.SetParent(display.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            pedestal.transform.localScale = new Vector3(0.9f, 0.4f, 0.9f);
            SetColor(pedestal, new Color(0.16f, 0.16f, 0.2f));

            GameObject relic = GameObject.CreatePrimitive(shape);
            relic.name = objectName;
            relic.transform.SetParent(display.transform, false);
            relic.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            relic.transform.localScale = Vector3.one * scale;
            relic.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            SetGlowColor(relic, color);
            relic.AddComponent<RelicPickup>().Configure(displayName);
        }

        static void CreateLockedContainer(Transform parent, Vector3 worldPosition)
        {
            GameObject container = new GameObject("LockedRelicContainer");
            container.transform.SetParent(parent, false);
            container.transform.position = worldPosition;
            container.AddComponent<DimensionPresence>().Configure(DimensionMembership.Void);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "ContainerBody";
            body.transform.SetParent(container.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            body.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            SetColor(body, new Color(0.28f, 0.12f, 0.36f));

            GameObject lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lid.name = "ContainerLid";
            lid.transform.SetParent(container.transform, false);
            lid.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            lid.transform.localScale = new Vector3(1.3f, 0.1f, 1.3f);
            SetColor(lid, new Color(0.52f, 0.24f, 0.66f));

            GameObject relic = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            relic.name = "Relic_Crown";
            relic.transform.SetParent(container.transform, false);
            relic.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            relic.transform.localScale = new Vector3(0.45f, 0.2f, 0.45f);
            SetGlowColor(relic, new Color(1f, 0.82f, 0.15f));
            relic.AddComponent<RelicPickup>().Configure("Crown Relic");
            relic.SetActive(false);

            container.AddComponent<RelicKeyContainer>().Configure(
                "rusty_key",
                "Rusty Key",
                relic,
                body.GetComponent<Renderer>());
        }

        static void CreateRustyKey(Transform parent, Vector3 worldPosition)
        {
            GameObject display = new GameObject("RustyKey_Display");
            display.transform.SetParent(parent, false);
            display.transform.position = worldPosition;
            display.AddComponent<DimensionPresence>().Configure(DimensionMembership.Void);

            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "KeyPedestal";
            pedestal.transform.SetParent(display.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            pedestal.transform.localScale = new Vector3(0.8f, 0.6f, 0.8f);
            SetColor(pedestal, new Color(0.16f, 0.16f, 0.2f));

            GameObject key = GameObject.CreatePrimitive(PrimitiveType.Cube);
            key.name = "RustyKey";
            key.transform.SetParent(display.transform, false);
            key.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            key.transform.localScale = new Vector3(0.5f, 0.12f, 0.12f);
            key.transform.localRotation = Quaternion.Euler(0f, 30f, 20f);
            SetGlowColor(key, new Color(1f, 0.78f, 0.12f));
            key.AddComponent<RelicKeyPickup>().Configure("rusty_key", "Rusty Key");
        }

        static void SetTriggerColor(GameObject primitive, Color color)
        {
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
                collider.isTrigger = true;

            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = color;
        }

        static void SetColor(GameObject primitive, Color color)
        {
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = color;
        }

        static void SetGlowColor(GameObject primitive, Color color)
        {
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer == null)
                return;

            Material material = renderer.material;
            material.color = color;

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.6f);
            }
        }

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }
    }
}
