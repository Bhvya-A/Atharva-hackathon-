using UnityEngine;
using UnityEngine.AI;

namespace TheOtherSide
{
    public enum GhostBehavior
    {
        Roaming,
        Chasing
    }

    /// <summary>
    /// Patrols, detects the player with FOV and line of sight, then chases and attacks.
    /// Subscribe to <see cref="DimensionManager.GhostStunRequested"/> for dimension-switch stuns.
    /// Call <see cref="Freeze"/> for a future Time Freeze ability — this does not pause the player or Time.timeScale.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public class GhostAI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform player;
        [SerializeField] Transform[] patrolPoints;
        [SerializeField] LayerMask losMask = ~0;
        [SerializeField] LayerMask obstacleMask = ~0;

        [Header("Movement")]
        [SerializeField] float moveSpeed = 1.5f;
        [SerializeField] float chaseSpeed = 3.8f;
        [SerializeField] float patrolRadius = 6f;
        [SerializeField] float patrolDwellTime = 0.6f;
        [SerializeField] float stoppingDistance = 1.15f;

        [Header("Detection")]
        [SerializeField] float detectionDistance = 10f;
        [SerializeField] float fieldOfView = 70f;
        [SerializeField] float eyeHeight = 1.5f;
        [SerializeField] float chaseDuration = 4f;
        [SerializeField] float stateLockDuration = 0.4f;
        [SerializeField] float chaseRefreshBonus = 1.15f;

        [Header("Combat")]
        [SerializeField] float attackDistance = 1.7f;
        [SerializeField] float attackCooldown = 1.5f;

        [Header("Spawn")]
        [SerializeField] float minPlayerClearance = 5.5f;

        NavMeshAgent _agent;
        Renderer[] _renderers;
        Vector3 _home;
        int _patrolIndex;
        float _dwellUntil;
        float _stateLockUntil;
        float _chaseUntil;
        float _nextAttackTime;
        float _stunUntil;
        float _nextDestinationUpdate;
        bool _loggedNavMeshFallback;
        bool _dimensionBound;
        bool _gameManagerBound;
        bool _halted;
        bool _stunWasActive;
        float _chaseRemainingOnFreeze;
        DimensionManager _boundDimensions;
        GameManager _boundGameManager;
        bool _warnedMissingPlayer;
        bool _useNavMesh;
        GhostBehavior _behavior = GhostBehavior.Roaming;
        GhostBehavior _behaviorBeforeStun = GhostBehavior.Roaming;

        static readonly Color RoamColor = new Color(0.45f, 0.04f, 0.12f);
        static readonly Color ChaseColor = new Color(0.95f, 0.12f, 0.08f);
        static readonly Color StunColor = new Color(0.25f, 0.75f, 0.95f);
        static readonly Color HaltColor = new Color(0.35f, 0.35f, 0.35f);

        public bool IsFrozen => Time.time < _stunUntil;
        public bool IsStunned => IsFrozen;
        public GhostBehavior CurrentBehavior => _behavior;
        public bool UsingNavMesh => _useNavMesh;

        public void Configure(Transform playerTransform, params Transform[] points)
        {
            player = playerTransform;
            if (points != null && points.Length > 0)
                patrolPoints = points;
        }

        /// <summary>
        /// Stops this ghost's movement and attacks for <paramref name="duration"/> seconds.
        /// Safe to call from dimension switching or a future Time Freeze ability.
        /// Does not freeze the player or change Time.timeScale.
        /// </summary>
        public void Freeze(float duration)
        {
            if (duration <= 0f)
                return;

            if (!IsFrozen)
            {
                _behaviorBeforeStun = _behavior;
                _chaseRemainingOnFreeze = Mathf.Max(0f, _chaseUntil - Time.time);
            }

            _stunUntil = Mathf.Max(_stunUntil, Time.time + duration);
            StopMovement();
            ApplyVisualState();
        }

        /// <summary>Alias for <see cref="Freeze"/> so dimension stun and Time Freeze share one code path.</summary>
        public void Stun(float duration) => Freeze(duration);

        public void Unfreeze()
        {
            _stunUntil = 0f;
            ResumeAfterStun();
            ApplyVisualState();
        }

        void Awake()
        {
            _home = transform.position;
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null)
                _agent = gameObject.AddComponent<NavMeshAgent>();

            _agent.radius = Mathf.Max(_agent.radius, 0.35f);
            _agent.height = Mathf.Max(_agent.height, 1.8f);
            _agent.speed = moveSpeed;
            _agent.acceleration = 10f;
            _agent.angularSpeed = 220f;
            _agent.stoppingDistance = stoppingDistance;
            _agent.autoBraking = true;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            _renderers = GetComponentsInChildren<Renderer>(true);
            EnsureClearOfPlayer();
        }

        void OnEnable()
        {
            BindManagers();
        }

        void Start()
        {
            BindManagers();
            EnsureClearOfPlayer();
            EnsurePatrolPoints();
            TryEnableNavMesh();
            ApplyVisualState();
        }

        void OnDisable()
        {
            UnbindManagers();
        }

        void OnDestroy()
        {
            UnbindManagers();
        }

        void Update()
        {
            BindManagers();

            if (ShouldHalt())
            {
                if (!_halted)
                {
                    _halted = true;
                    StopMovement();
                    ApplyVisualState();
                }
                return;
            }

            if (_halted)
            {
                _halted = false;
                ApplyVisualState();
            }

            if (!ResolvePlayer())
                return;

            if (IsFrozen)
            {
                _stunWasActive = true;
                StopMovement();
                return;
            }

            if (_stunWasActive)
                ResumeAfterStun();

            TryEnableNavMesh();
            UpdateDetection();

            if (_behavior == GhostBehavior.Chasing)
            {
                ChasePlayer();
                TryAttack();
            }
            else
            {
                Patrol();
            }

            ApplyVisualState();
        }

        bool ShouldHalt()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null)
                return false;

            return manager.State == GameState.GameOver || manager.State == GameState.Won || !manager.IsGameplayActive;
        }

        void UpdateDetection()
        {
            bool seesPlayer = CanDetectPlayer(requireFieldOfView: true);

            if (_behavior == GhostBehavior.Roaming)
            {
                if (seesPlayer && Time.time >= _stateLockUntil)
                    EnterChase();
                return;
            }

            if (CanDetectPlayer(requireFieldOfView: false))
            {
                _chaseUntil = Time.time + chaseDuration;
                return;
            }

            if (Time.time >= _chaseUntil && Time.time >= _stateLockUntil)
                EnterRoam();
        }

        void EnterChase()
        {
            _behavior = GhostBehavior.Chasing;
            _behaviorBeforeStun = GhostBehavior.Chasing;
            _chaseUntil = Time.time + chaseDuration;
            _stateLockUntil = Time.time + stateLockDuration;
            _nextDestinationUpdate = 0f;
        }

        void EnterRoam()
        {
            _behavior = GhostBehavior.Roaming;
            _behaviorBeforeStun = GhostBehavior.Roaming;
            _stateLockUntil = Time.time + stateLockDuration;
            _dwellUntil = 0f;
            _nextDestinationUpdate = 0f;
        }

        void Patrol()
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                EnsurePatrolPoints();
                if (patrolPoints == null || patrolPoints.Length == 0)
                    return;
            }

            Transform point = patrolPoints[_patrolIndex];
            if (point == null)
            {
                _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
                return;
            }

            Vector3 destination = Flatten(point.position);
            if (Reached(destination))
            {
                if (Time.time < _dwellUntil)
                {
                    StopMovement();
                    return;
                }

                if (_dwellUntil <= 0f)
                {
                    _dwellUntil = Time.time + patrolDwellTime;
                    StopMovement();
                    return;
                }

                _dwellUntil = 0f;
                _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
                _nextDestinationUpdate = 0f;
                return;
            }

            MoveTo(destination, moveSpeed);
        }

        void ChasePlayer()
        {
            if (player == null)
                return;

            Vector3 destination = Flatten(player.position);
            if (HorizontalDistance(transform.position, player.position) <= stoppingDistance)
            {
                StopMovement();
                Face(player.position);
                return;
            }

            MoveTo(destination, chaseSpeed);
        }

        void TryAttack()
        {
            if (player == null)
                return;

            if (IsFrozen || ShouldHalt())
                return;

            GameManager manager = GameManager.Instance;
            if (manager == null || manager.State != GameState.Playing)
                return;

            if (HorizontalDistance(transform.position, player.position) > attackDistance)
                return;

            if (Time.time < _nextAttackTime)
                return;

            // Do not damage through walls; the player must still be visible when the hit lands.
            if (!CanDetectPlayer(requireFieldOfView: false))
                return;

            manager.LoseLife(1);
            _nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldown);
        }

        bool CanDetectPlayer(bool requireFieldOfView)
        {
            if (player == null)
                return false;

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 target = GetPlayerAimPoint();
            Vector3 toTarget = target - eye;
            float distance = toTarget.magnitude;
            float range = requireFieldOfView ? detectionDistance : detectionDistance * chaseRefreshBonus;
            if (distance > range + 0.001f)
                return false;

            if (requireFieldOfView && distance > 0.01f)
            {
                float angle = Vector3.Angle(transform.forward, toTarget);
                if (angle > fieldOfView * 0.5f)
                    return false;
            }

            float losDistance = Mathf.Max(0f, distance - 0.05f);
            if (losDistance <= 0.02f)
                return true;

            // Ignore the ghost's own colliders, then check the nearest remaining hit.
            RaycastHit[] hits = Physics.RaycastAll(
                eye,
                toTarget / distance,
                losDistance,
                losMask,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.MaxValue;
            bool nearestIsPlayer = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitTransform = hits[i].transform;
                if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
                    continue;

                if (hits[i].distance >= nearestDistance)
                    continue;

                nearestDistance = hits[i].distance;
                nearestIsPlayer = IsPlayerTransform(hitTransform);
            }

            return nearestDistance == float.MaxValue || nearestIsPlayer;
        }

        Vector3 GetPlayerAimPoint()
        {
            if (player.TryGetComponent(out CharacterController controller))
                return controller.bounds.center;

            Collider collider = player.GetComponentInChildren<Collider>();
            if (collider != null)
                return collider.bounds.center;

            return player.position + Vector3.up * 0.9f;
        }

        bool IsPlayerTransform(Transform hit)
        {
            if (player == null || hit == null)
                return false;

            return hit == player || hit.IsChildOf(player) || player.IsChildOf(hit);
        }

        void MoveTo(Vector3 destination, float speed)
        {
            if (_useNavMesh && _agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.updateRotation = true;
                _agent.isStopped = false;
                _agent.speed = speed;
                _agent.stoppingDistance = stoppingDistance;
                if (Time.time >= _nextDestinationUpdate)
                {
                    _agent.SetDestination(destination);
                    _nextDestinationUpdate = Time.time + 0.2f;
                }
                return;
            }

            Face(destination);

            Vector3 next = Vector3.MoveTowards(Flatten(transform.position), destination, speed * Time.deltaTime);
            Vector3 delta = next - Flatten(transform.position);
            if (delta.sqrMagnitude > 0.0001f)
            {
                Vector3 origin = transform.position + Vector3.up * 0.9f;
                float probe = delta.magnitude + _agent.radius;
                if (Physics.SphereCast(origin, _agent.radius * 0.85f, delta.normalized, out RaycastHit hit, probe, obstacleMask, QueryTriggerInteraction.Ignore)
                    && !IsPlayerTransform(hit.transform))
                {
                    return;
                }
            }

            transform.position = new Vector3(next.x, transform.position.y, next.z);
        }

        void StopMovement()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
                _agent.velocity = Vector3.zero;
            }

            _nextDestinationUpdate = 0f;
        }

        void Face(Vector3 worldPoint)
        {
            Vector3 dir = Flatten(worldPoint) - Flatten(transform.position);
            if (dir.sqrMagnitude < 0.001f)
                return;

            Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 8f);
        }

        bool Reached(Vector3 destination)
        {
            return HorizontalDistance(transform.position, destination) <= Mathf.Max(0.35f, stoppingDistance * 0.45f);
        }

        void TryEnableNavMesh()
        {
            if (_agent == null)
                return;

            Vector3 sampleOrigin = transform.position;
            if (NavMesh.SamplePosition(sampleOrigin, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                if (!_agent.enabled)
                    _agent.enabled = true;

                if (!_agent.isOnNavMesh)
                    _agent.Warp(hit.position);

                _useNavMesh = _agent.isOnNavMesh;
                if (_useNavMesh)
                    return;
            }

            _useNavMesh = false;
            if (_agent.enabled && !_agent.isOnNavMesh)
                _agent.enabled = false;

            if (!_loggedNavMeshFallback)
            {
                _loggedNavMeshFallback = true;
                Debug.LogWarning(
                    "GhostAI: no baked NavMesh near the ghost. Using straight-line fallback so the ghost still patrols and chases. " +
                    "For proper pathfinding around walls: add a NavMesh Surface to the level (Window > AI > Navigation), " +
                    "include the floor and walls, bake, and ensure the ghost's NavMeshAgent can place on the mesh. " +
                    "GameBootstrap bakes a runtime surface in SampleScene; if that failed, confirm AI Navigation is installed.");
            }
        }

        void EnsurePatrolPoints()
        {
            if (patrolPoints != null && patrolPoints.Length > 0)
                return;

            var generated = new Transform[4];
            Vector3[] offsets =
            {
                new Vector3(patrolRadius, 0f, 0f),
                new Vector3(-patrolRadius * 0.6f, 0f, patrolRadius * 0.7f),
                new Vector3(patrolRadius * 0.4f, 0f, -patrolRadius * 0.8f),
                new Vector3(-patrolRadius * 0.75f, 0f, -patrolRadius * 0.2f)
            };

            Transform folder = new GameObject("GhostPatrolPoints").transform;
            folder.SetParent(transform.parent, true);

            for (int i = 0; i < offsets.Length; i++)
            {
                var point = new GameObject($"PatrolPoint_{i + 1}").transform;
                point.SetParent(folder, true);
                point.position = _home + offsets[i];
                generated[i] = point;
            }

            patrolPoints = generated;
        }

        void EnsureClearOfPlayer()
        {
            if (!ResolvePlayer())
                return;

            Vector3 ghostPos = Flatten(transform.position);
            Vector3 playerPos = Flatten(player.position);
            float distance = Vector3.Distance(ghostPos, playerPos);
            float needed = Mathf.Max(minPlayerClearance, attackDistance + 2f);
            if (distance >= needed)
                return;

            Vector3 away = ghostPos - playerPos;
            if (away.sqrMagnitude < 0.01f)
                away = Vector3.back;

            Vector3 safe = playerPos + away.normalized * needed;
            safe.y = transform.position.y;
            transform.position = safe;
            _home = safe;

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
                _agent.Warp(safe);
        }

        bool ResolvePlayer()
        {
            if (player != null)
                return true;

            FirstPersonController controller = FindAnyObjectByType<FirstPersonController>();
            if (controller != null)
            {
                player = controller.transform;
                return true;
            }

            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
            {
                player = tagged.transform;
                return true;
            }

            if (!_warnedMissingPlayer)
            {
                _warnedMissingPlayer = true;
                Debug.LogWarning("GhostAI: no player found. Assign a player Transform or keep the existing FirstPersonController tagged Player.");
            }

            return false;
        }

        void ResumeAfterStun()
        {
            _stunWasActive = false;
            if (_halted)
                return;

            _behavior = _behaviorBeforeStun;
            if (_behavior == GhostBehavior.Chasing)
                _chaseUntil = Time.time + Mathf.Max(_chaseRemainingOnFreeze, stateLockDuration);
            _stateLockUntil = Time.time + stateLockDuration;
        }

        void BindManagers()
        {
            if (!_dimensionBound && DimensionManager.Instance != null)
            {
                _boundDimensions = DimensionManager.Instance;
                _boundDimensions.GhostStunRequested += OnGhostStunRequested;
                _dimensionBound = true;
            }

            if (!_gameManagerBound && GameManager.Instance != null)
            {
                _boundGameManager = GameManager.Instance;
                _boundGameManager.StateChanged += OnGameStateChanged;
                _gameManagerBound = true;
                OnGameStateChanged(_boundGameManager.State);
            }
        }

        void UnbindManagers()
        {
            if (_dimensionBound && _boundDimensions != null)
                _boundDimensions.GhostStunRequested -= OnGhostStunRequested;
            _dimensionBound = false;
            _boundDimensions = null;

            if (_gameManagerBound && _boundGameManager != null)
                _boundGameManager.StateChanged -= OnGameStateChanged;
            _gameManagerBound = false;
            _boundGameManager = null;
        }

        void OnGhostStunRequested(float duration)
        {
            Freeze(duration);
        }

        void OnGameStateChanged(GameState state)
        {
            if (state == GameState.GameOver || state == GameState.Won)
            {
                _halted = true;
                StopMovement();
                ApplyVisualState();
            }
        }

        void ApplyVisualState()
        {
            Color tint;
            if (_halted)
                tint = HaltColor;
            else if (IsFrozen)
                tint = StunColor;
            else if (_behavior == GhostBehavior.Chasing)
                tint = ChaseColor;
            else
                tint = RoamColor;

            if (_renderers == null)
                return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                    continue;

                _renderers[i].material.color = tint;
            }
        }

        static Vector3 Flatten(Vector3 position)
        {
            position.y = 0f;
            return position;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionDistance);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackDistance);

            Vector3 forward = transform.forward * detectionDistance;
            Quaternion left = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f);
            Quaternion right = Quaternion.Euler(0f, fieldOfView * 0.5f, 0f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(eye, eye + left * forward);
            Gizmos.DrawLine(eye, eye + right * forward);
        }
    }
}
