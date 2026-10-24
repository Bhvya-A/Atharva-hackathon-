using System.Collections.Generic;
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
    /// The player and ghost are spawned from Resources prefabs so their components are
    /// editable in the Project window instead of being assembled invisibly at runtime.
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

            // The scene may already own manager components. Do not instantiate a
            // second manager just because its Awake has not run yet (bootstrap uses
            // a very early execution order).
            if (Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include) == null)
                new GameObject("GameManager").AddComponent<GameManager>();

            if (Object.FindAnyObjectByType<DimensionManager>(FindObjectsInactive.Include) == null)
                new GameObject("DimensionManager").AddComponent<DimensionManager>();

            if (Object.FindAnyObjectByType<HorrorAudioManager>(FindObjectsInactive.Include) == null)
                new GameObject("AudioManager").AddComponent<HorrorAudioManager>();

            CreateGround();
            CreateStairTest();
            CreateLosOccluder();
            BakeRuntimeNavMesh();
            _player = CreatePlayer();
            CreateHud();
            CreateSampleInteractable(_player.transform.position + _player.transform.forward * 3f + Vector3.up * 0.5f);
            CreateDimensionTestObjects(_player.transform.position);
            CreateRelicGameplay(_player);
            EnsureEventSystem();
        }

        FirstPersonController _player;

        void Start()
        {
            StartCoroutine(SpawnGhostWhenNavMeshIsReady());
        }

        /// <summary>
        /// A NavMesh built during Awake becomes queryable only after the surface has
        /// registered its data with the navigation system, so the agent is created one
        /// frame later. Instantiating it earlier logs "no valid NavMesh" and leaves the
        /// agent disabled until it is re-warped.
        /// </summary>
        System.Collections.IEnumerator SpawnGhostWhenNavMeshIsReady()
        {
            yield return null;
            yield return null;

            if (!NavMesh.SamplePosition(Vector3.zero, out _, 4f, NavMesh.AllAreas))
            {
                Debug.LogWarning(
                    "GameBootstrap: runtime NavMesh bake produced no walkable surface near the origin. " +
                    "GhostAI will use fallback movement. Bake a NavMesh Surface in the Editor for authored levels.");
            }

            CreateGhost(_player);
            DimensionManager.Instance?.RefreshPresence();
        }

        static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
        }

        /// <summary>
        /// Temporary two-floor test structure for stair traversal. It is intentionally
        /// separate from the deferred mansion: a raised platform plus eight 0.25 m
        /// steps that both the CharacterController player and the NavMeshAgent ghost
        /// must climb in both directions.
        /// </summary>
        static void CreateStairTest()
        {
            GameObject root = new GameObject("StairTest");

            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "UpperPlatform";
            platform.transform.SetParent(root.transform, false);
            platform.transform.position = new Vector3(11f, 1f, 8f);
            platform.transform.localScale = new Vector3(6f, 2f, 6f);
            SetColor(platform, new Color(0.24f, 0.23f, 0.27f));

            const int stepCount = 8;
            const float rise = 0.25f;
            const float run = 0.5f;
            const float width = 3f;
            const float platformTop = 2f;
            const float topEdgeX = 8f;

            for (int i = 0; i < stepCount; i++)
            {
                float top = platformTop - (i + 1) * rise;
                float centerX = topEdgeX - run * 0.5f - i * run;

                GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"Step_{i + 1}";
                step.transform.SetParent(root.transform, false);
                step.transform.position = new Vector3(centerX, top * 0.5f, 8f);
                step.transform.localScale = new Vector3(run, Mathf.Max(0.05f, top), width);
                SetColor(step, new Color(0.27f, 0.26f, 0.3f));
            }
        }

        static FirstPersonController CreatePlayer()
        {
            Transform spawnPoint = FindSceneTransform("PlayerSpawn");
            Vector3 position = spawnPoint != null ? spawnPoint.position : new Vector3(0f, 0.1f, 0f);
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

            GameObject player = null;
            GameObject playerPrefab = Resources.Load<GameObject>("Player/Player");
            if (playerPrefab != null)
            {
                player = Object.Instantiate(playerPrefab, position, rotation);
                player.name = "Player";
            }

            if (player == null)
            {
                // Prefab-less fallback so the scene still opens in a fresh checkout
                // even if the setup tool has not run yet.
                player = new GameObject("Player");
                player.transform.SetPositionAndRotation(position, rotation);
            }

            player.tag = "Player";

            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc == null)
                cc = player.AddComponent<CharacterController>();
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

            Transform pivot = player.transform.Find("CameraPivot");
            if (pivot == null)
            {
                pivot = new GameObject("CameraPivot").transform;
                pivot.SetParent(player.transform, false);
                pivot.localPosition = new Vector3(0f, 1.6f, 0f);
            }

            cam.transform.SetParent(pivot, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;

            if (cam.GetComponent<CameraShake>() == null)
                cam.gameObject.AddComponent<CameraShake>();

            // First-person relic presentation lives on the active camera so it follows
            // the view through dimension swaps and respawns without a new rig.
            if (cam.GetComponent<RelicHolder>() == null)
            {
                RelicHolder holder = cam.gameObject.AddComponent<RelicHolder>();

                GameObject lightGo = new GameObject("RelicHoldLight");
                lightGo.transform.SetParent(cam.transform, false);
                lightGo.transform.localPosition = new Vector3(0.18f, -0.02f, 0.35f);

                Light holdLight = lightGo.AddComponent<Light>();
                holdLight.type = LightType.Point;
                holdLight.color = new Color(1f, 0.87f, 0.7f);
                holdLight.intensity = 0.5f;
                holdLight.range = 2.4f;
                holdLight.shadows = LightShadows.None;

                lightGo.SetActive(false);
                holder.Configure(holdLight);
            }

            FirstPersonController controller = player.GetComponent<FirstPersonController>();
            if (controller == null)
                controller = player.AddComponent<FirstPersonController>();
            controller.Configure(pivot);

            PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();
            if (interactor == null)
                interactor = player.AddComponent<PlayerInteractor>();
            interactor.Configure(cam);

            if (player.GetComponent<TimeFreezeAbility>() == null)
                player.AddComponent<TimeFreezeAbility>();

            if (player.GetComponent<PlayerRelicCarrier>() == null)
                player.AddComponent<PlayerRelicCarrier>();

            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health == null)
                health = player.AddComponent<PlayerHealth>();

            if (spawnPoint != null)
                health.ConfigureSpawn(spawnPoint, false);
            else
                health.ConfigureSpawn(position, rotation);

            // The scene authors a dedicated RespawnPoint so the respawn location can
            // differ from the first spawn (checkpoints, safe rooms, ...).
            Transform respawnPoint = FindSceneTransform("RespawnPoint");
            if (respawnPoint != null)
                health.ConfigureSpawn(respawnPoint, false);

            return controller;
        }

        static void CreateHud()
        {
            // The scene owns the HUD root; only create one if this is a bare scene.
            HUDController existing = Object.FindAnyObjectByType<HUDController>(FindObjectsInactive.Include);
            if (existing != null)
                return;

            GameObject hud = new GameObject("HUD");
            HUDController controller = hud.AddComponent<HUDController>();
            controller.BuildDefaultUI();
        }

        static Transform FindSceneTransform(string name)
        {
            GameObject found = GameObject.Find(name);
            return found != null ? found.transform : null;
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
        }

        static void CreateGhost(FirstPersonController player)
        {
            if (Object.FindAnyObjectByType<GhostAI>(FindObjectsInactive.Include) != null)
                return;

            Transform ghostSpawnMarker = FindSceneTransform("GhostSpawn");
            Vector3 spawn = ghostSpawnMarker != null ? ghostSpawnMarker.position : new Vector3(0f, 0f, 12f);
            if (player != null)
            {
                Vector3 playerPos = player.transform.position;
                playerPos.y = 0f;
                if (Vector3.Distance(new Vector3(spawn.x, 0f, spawn.z), playerPos) < 6f)
                    spawn = playerPos + Vector3.forward * 8f;
            }

            Quaternion spawnRotation = ghostSpawnMarker != null
                ? ghostSpawnMarker.rotation
                : Quaternion.LookRotation(Vector3.forward);

            GameObject ghost = null;
            GameObject ghostPrefab = Resources.Load<GameObject>("Enemy/Ghost");
            if (ghostPrefab != null)
            {
                ghost = Object.Instantiate(ghostPrefab, spawn, spawnRotation);
                ghost.name = "Ghost";
            }

            if (ghost == null)
            {
                // Prefab-less fallback: build the same hierarchy once so the scene
                // remains playable before the setup tool has authored the prefab.
                ghost = new GameObject("Ghost");
                ghost.transform.SetPositionAndRotation(spawn, spawnRotation);
                CreateGhostVisual(ghost.transform);
            }

            NavMeshAgent agent = ghost.GetComponent<NavMeshAgent>();
            if (agent == null)
                agent = ghost.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.8f;
            agent.acceleration = 10f;
            agent.stoppingDistance = 1.15f;
            agent.autoBraking = true;

            CapsuleCollider bodyCollider = ghost.GetComponent<CapsuleCollider>();
            if (bodyCollider == null)
                bodyCollider = ghost.AddComponent<CapsuleCollider>();
            bodyCollider.center = new Vector3(0f, 0.9f, 0f);
            bodyCollider.height = 1.8f;
            bodyCollider.radius = 0.4f;

            Animator ghostAnimator = ghost.GetComponentInChildren<Animator>(true);
            if (ghostAnimator != null)
                ghostAnimator.applyRootMotion = false;

            if (NavMesh.SamplePosition(spawn, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                agent.Warp(hit.position);

            Transform[] points = ResolvePatrolPoints(spawn);
            GhostAI ai = ghost.GetComponent<GhostAI>();
            if (ai == null)
                ai = ghost.AddComponent<GhostAI>();
            ai.Configure(player != null ? player.transform : null, points);

            DimensionPresence presence = ghost.GetComponent<DimensionPresence>();
            if (presence == null)
                presence = ghost.AddComponent<DimensionPresence>();
            presence.Configure(DimensionMembership.Both);
        }

        static void CreateGhostVisual(Transform ghost)
        {
            GameObject modelPrefab = Resources.Load<GameObject>("Enemy/GhostVisual");
            if (modelPrefab == null)
            {
                Debug.LogWarning("GameBootstrap: Resources/Enemy/GhostVisual.prefab was not found. Using a capsule placeholder.");
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Visual";
                body.transform.SetParent(ghost, false);
                body.transform.localPosition = new Vector3(0f, 1f, 0f);
                body.transform.localScale = new Vector3(0.85f, 1f, 0.85f);
                SetTriggerColor(body, new Color(0.5f, 0.05f, 0.12f));
                return;
            }

            // Mixamo faces +Z, which matches Transform.forward, so no yaw offset is needed.
            // Bind pose is ~2.14 m tall: 0.85 fits the 1.8 m agent and 0.085 puts the feet on the floor.
            GameObject visual = Object.Instantiate(modelPrefab, ghost, false);
            visual.name = "Visual";
            visual.transform.localPosition = new Vector3(0f, 0.085f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * 0.85f;

            Animator animator = visual.GetComponent<Animator>();
            if (animator == null)
                animator = visual.AddComponent<Animator>();

            // NavMeshAgent moves the root; the clips must stay in place.
            animator.applyRootMotion = false;
            if (animator.runtimeAnimatorController == null)
                Debug.LogWarning("GameBootstrap: GhostVisual has no Animator Controller; the enemy will not animate.");
        }

        static Transform[] ResolvePatrolPoints(Vector3 home)
        {
            Transform folder = FindSceneTransform("GhostPatrol");
            if (folder != null && folder.childCount > 0)
            {
                var authored = new List<Transform>();
                for (int i = 0; i < folder.childCount; i++)
                {
                    Transform child = folder.GetChild(i);
                    if (child != null)
                        authored.Add(child);
                }

                if (authored.Count > 0)
                {
                    AddStairPatrolPoint(authored);
                    return authored.ToArray();
                }
            }

            return CreatePatrolPoints(home);
        }

        /// <summary>
        /// Authored patrol points keep the designer's route, but the ghost also has to
        /// prove it can cross floors. Append the stair-test waypoint unless the authored
        /// route already visits the upper platform.
        /// </summary>
        static void AddStairPatrolPoint(List<Transform> points)
        {
            Transform stairRoot = FindSceneTransform("StairTest");
            if (stairRoot == null)
                return;

            Vector3 stairTop = new Vector3(11f, 2f, 8f);
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] != null && Vector3.Distance(points[i].position, stairTop) < 1.5f)
                    return;
            }

            Transform point = new GameObject("PatrolPoint_Stairs").transform;
            point.SetParent(stairRoot, true);
            point.position = stairTop;
            points.Add(point);
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

            var points = new Transform[offsets.Length + 1];
            for (int i = 0; i < offsets.Length; i++)
            {
                Transform point = new GameObject($"PatrolPoint_{i + 1}").transform;
                point.SetParent(folder, true);
                point.position = home + offsets[i];
                points[i] = point;
            }

            // Extra waypoint on the stair-test platform so patrol exercises the
            // staircase in both directions instead of staying on the ground floor.
            Transform stairs = new GameObject("PatrolPoint_Stairs").transform;
            stairs.SetParent(folder, true);
            stairs.position = new Vector3(11f, 2f, 8f);
            points[offsets.Length] = stairs;

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

            // Gameplay roles are unchanged from Phase 1 (three free relics plus one
            // key-gated container); only the artwork and display names changed.
            CreateFreeRelic(root.transform, "Relic_Compass", RelicType.Compass, new Vector3(-9f, 0f, -7f));
            CreateFreeRelic(root.transform, "Relic_Candle", RelicType.Candle, new Vector3(9f, 0f, -7f));
            CreateFreeRelic(root.transform, "Relic_Skull", RelicType.Skull, new Vector3(0f, 0f, -12f));

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

        static void CreateFreeRelic(Transform parent, string objectName, RelicType type, Vector3 worldPosition)
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

            AddDisplayLight(display.transform, RelicGlow(type));

            GameObject relic = InstantiateRelic(
                display.transform,
                type,
                worldPosition + Vector3.up * 0.9f,
                Quaternion.Euler(0f, 25f, 0f));

            if (relic == null)
                return;

            // Sit the centred prefab just above the pedestal using its real height.
            Bounds bounds = ComputeBounds(relic);
            Vector3 position = relic.transform.position;
            position.y = worldPosition.y + 0.52f + bounds.extents.y;
            relic.transform.position = position;

            RelicPickup pickup = relic.GetComponent<RelicPickup>();
            if (pickup != null)
                pickup.SetBasePosition(position);
        }

        static GameObject InstantiateRelic(Transform parent, RelicType type, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = Resources.Load<GameObject>(RelicTypes.ResourcePath(type));
            if (prefab != null)
            {
                GameObject instance = Object.Instantiate(prefab, position, rotation, parent);
                instance.name = type.ToString();
                return instance;
            }

            Debug.LogError(
                $"[TheOtherSide] Missing relic prefab Resources/{RelicTypes.ResourcePath(type)}. " +
                "Using a labelled placeholder so the loop stays playable. Rebuild with " +
                "'The Other Side > Build Relic Prefabs'.");

            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = type + "_Placeholder";
            placeholder.transform.SetParent(parent, false);
            placeholder.transform.position = position;
            placeholder.transform.rotation = rotation;
            placeholder.transform.localScale = Vector3.one * 0.24f;
            SetGlowColor(placeholder, RelicGlow(type));

            RelicPickup pickup = placeholder.AddComponent<RelicPickup>();
            pickup.Configure(RelicTypes.DisplayName(type), type);
            return placeholder;
        }

        static void AddDisplayLight(Transform parent, Color color)
        {
            GameObject lightGo = new GameObject("DisplayLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.1f, 0f);

            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 0.55f;
            light.range = 2.6f;
            light.shadows = LightShadows.None;
        }

        static Color RelicGlow(RelicType type)
        {
            switch (type)
            {
                case RelicType.Compass: return new Color(0.25f, 0.78f, 1f);
                case RelicType.Candle: return new Color(1f, 0.62f, 0.28f);
                case RelicType.Skull: return new Color(0.6f, 0.9f, 0.65f);
                case RelicType.Grimoire: return new Color(0.72f, 0.45f, 1f);
                default: return Color.white;
            }
        }

        static Bounds ComputeBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool has = false;
            Bounds bounds = new Bounds();

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;

                if (!has)
                {
                    bounds = renderers[i].bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return bounds;
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

            AddDisplayLight(container.transform, RelicGlow(RelicType.Grimoire));

            GameObject relic = InstantiateRelic(
                container.transform,
                RelicType.Grimoire,
                worldPosition + Vector3.up * 1.6f,
                Quaternion.Euler(8f, -18f, 0f));

            if (relic != null)
            {
                Bounds bounds = ComputeBounds(relic);
                Vector3 position = relic.transform.position;
                position.y = worldPosition.y + 1.3f + bounds.extents.y;
                relic.transform.position = position;

                RelicPickup pickup = relic.GetComponent<RelicPickup>();
                if (pickup != null)
                    pickup.SetBasePosition(position);

                relic.SetActive(false);
            }

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
