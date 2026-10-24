using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace TheOtherSide
{
    public enum GhostBehavior
    {
        Roaming,
        Chasing,
        Investigating
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
        [SerializeField] float detectionDistance = 11f;
        [SerializeField] float fieldOfView = 80f;
        [SerializeField] float eyeHeight = 1.5f;
        [Tooltip("Seconds the ghost keeps chasing after losing sight before it investigates the last known position.")]
        [FormerlySerializedAs("chaseDuration")]
        [SerializeField] float targetLossDelay = 4f;
        [SerializeField] float stateLockDuration = 0.4f;
        [Tooltip("Inside this radius the ghost notices the player even outside its field of view.")]
        [SerializeField] float closeRangeRadius = 2.4f;
        [Tooltip("Inside this much smaller radius a thin corner does not hide the player. Keeps close-range awareness fair.")]
        [SerializeField] float closeRangeOcclusionBypass = 1.15f;
        [Tooltip("How long the ghost searches the last known position before returning to patrol.")]
        [SerializeField] float investigateDuration = 7f;
        [SerializeField] float investigateLookSpeed = 75f;

        [Header("Combat")]
        [SerializeField] float attackDistance = 1.7f;
        [SerializeField] float attackCooldown = 1.5f;
        [Tooltip("Extra idle time after a swing so the Attack state can exit before it is triggered again.")]
        [SerializeField] float attackRecovery = 0.35f;
        [Tooltip("Normalized point inside the Attack clip where the punch connects. Damage is validated once at this moment.")]
        [Range(0.1f, 0.85f)]
        [SerializeField] float attackHitNormalizedTime = 0.45f;

        [Header("Spawn")]
        [SerializeField] float minPlayerClearance = 5.5f;

        NavMeshAgent _agent;
        Animator _animator;
        Renderer[] _renderers;
        Vector3 _home;
        int _patrolIndex;
        float _dwellUntil;
        float _stateLockUntil;
        float _chaseUntil;
        float _nextAttackTime;
        float _stunUntil;
        float _nextDestinationUpdate;
        Vector3 _lastKnownPosition;
        Vector3 _lastDestination;
        bool _hasLastDestination;
        float _investigateUntil;
        float _investigateYaw;
        bool _investigatingArrived;
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
        MaterialPropertyBlock _visualOverrides;
        Color _appliedTint;
        bool _hasVisualOverride;

        const string AttackClipName = "Attack";
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");

        float _speedParameter;
        float _attackUntil;
        float _attackHitTime;
        bool _attackHitApplied = true;
        float _attackClipLength = 0.35f; // overwritten with the real clip length when an Animator exists
        Vector3 _animatorSamplePosition;
        PlayerHealth _playerHealth;

        static readonly Color StunColor = new Color(0.35f, 0.8f, 1f);
        static readonly Color HaltColor = new Color(0.45f, 0.45f, 0.45f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public bool IsFrozen => Time.time < _stunUntil;
        public bool IsStunned => IsFrozen;
        public GhostBehavior CurrentBehavior => _behavior;
        public bool UsingNavMesh => _useNavMesh;
        public bool IsAttacking => _attackUntil > Time.time;
        public bool AttackHitApplied => _attackHitApplied;
        public float AttackHitTime => _attackHitTime;
        public float AttackDistance => attackDistance;
        public Vector3 LastKnownPosition => _lastKnownPosition;
        public bool IsPlayerVisibleNow { get; private set; }
        public float DistanceToPlayer { get; private set; }

        public event Action<GhostBehavior> BehaviorChanged;
        public event Action AttackStarted;

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
            CancelAttack();
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

            _animator = GetComponentInChildren<Animator>(true);
            if (_animator != null)
            {
                // The NavMeshAgent owns locomotion; never let a clip drive the root.
                _animator.applyRootMotion = false;
                _attackClipLength = ResolveClipLength(_animator, AttackClipName, _attackClipLength);
            }

            _animatorSamplePosition = transform.position;
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

            // The swing owns the body until this timer expires. Clearing IsAttacking once
            // (instead of every frame) lets the clip finish and blocks AnyState restarts.
            if (_attackUntil > 0f && Time.time >= _attackUntil)
                EndAttack();

            if (ShouldHalt())
            {
                if (!_halted)
                {
                    _halted = true;
                    StopMovement();
                    CancelAttack();
                    ApplyVisualState();
                }
                UpdateAnimator(0f);
                return;
            }

            if (_halted)
            {
                _halted = false;
                ApplyVisualState();
            }

            if (!ResolvePlayer())
            {
                UpdateAnimator(0f);
                return;
            }

            ResolvePlayerHealth();

            // A downed player is not a valid target: cancel any in-flight swing so
            // it cannot land during the respawn transition, and hold position until
            // the player is alive again.
            if (_playerHealth != null && !_playerHealth.IsAlive)
            {
                CancelAttack();
                StopMovement();
                UpdateAnimator(0f);
                ApplyVisualState();
                return;
            }

            if (IsFrozen)
            {
                _stunWasActive = true;
                StopMovement();
                CancelAttack();
                UpdateAnimator(0f);
                return;
            }

            if (_stunWasActive)
                ResumeAfterStun();

            if (IsAttacking)
            {
                // Hold position and keep facing the player while the punch plays.
                // The single damaging frame is validated here, from the same timer
                // that owns the animation state, so an interrupted swing cannot
                // deliver damage afterwards.
                ProcessAttackHit();
                StopMovement();
                Face(player.position);
                UpdateAnimator(0f);
                ApplyVisualState();
                return;
            }

            TryEnableNavMesh();
            UpdateDetection();

            if (_behavior == GhostBehavior.Chasing)
            {
                ChasePlayer();
                TryAttack();

                if (IsAttacking)
                {
                    StopMovement();
                    Face(player.position);
                    UpdateAnimator(0f);
                    ApplyVisualState();
                    return;
                }
            }
            else if (_behavior == GhostBehavior.Investigating)
            {
                Investigate();
            }
            else
            {
                Patrol();
            }

            UpdateAnimator(ResolveLocomotionSpeed01());
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
            if (player == null)
                return;

            Vector3 eye = GetEyePosition();
            Vector3 target = GetPlayerAimPoint();
            float eyeDistance = Vector3.Distance(eye, target);
            DistanceToPlayer = Vector3.Distance(transform.position, player.position);

            // Long-range detection needs range, field of view and a clear line of sight.
            bool seesPlayer = CanSeePlayer(eye, target, eyeDistance);

            // Close-range awareness is deliberately separate: the ghost notices a
            // player beside or slightly behind it, and very close targets are not
            // hidden by a thin corner. Long-range detection still requires LOS, so
            // walls block the ghost across the level.
            bool closeAwareness = IsCloseRangeAware();

            bool detected = seesPlayer || closeAwareness;
            IsPlayerVisibleNow = detected;

            if (detected)
                _lastKnownPosition = player.position;

            switch (_behavior)
            {
                case GhostBehavior.Roaming:
                    if (detected && Time.time >= _stateLockUntil)
                        EnterChase();
                    break;

                case GhostBehavior.Chasing:
                    if (detected)
                    {
                        _chaseUntil = Time.time + Mathf.Max(0.1f, targetLossDelay);
                    }
                    else if (Time.time >= _chaseUntil && Time.time >= _stateLockUntil)
                    {
                        // Do not instantly forget the player: search the place where
                        // they were last seen before returning to patrol.
                        EnterInvestigate();
                    }
                    break;

                case GhostBehavior.Investigating:
                    if (detected && Time.time >= _stateLockUntil)
                        EnterChase();
                    else if (Time.time >= _investigateUntil && Time.time >= _stateLockUntil)
                        EnterRoam();
                    break;
            }
        }

        /// <summary>
        /// Fair close-range awareness. Inside <see cref="closeRangeRadius"/> the ghost
        /// senses the player without the field-of-view cone. Only inside the much
        /// smaller <see cref="closeRangeOcclusionBypass"/> radius may it sense through
        /// thin cover; between the two radii a clear line of sight is still required.
        /// </summary>
        bool IsCloseRangeAware()
        {
            if (player == null)
                return false;

            float distance = Vector3.Distance(transform.position, player.position);
            if (distance > closeRangeRadius)
                return false;

            if (distance <= closeRangeOcclusionBypass)
                return true;

            return HasLineOfSight();
        }

        void EnterChase()
        {
            SetBehavior(GhostBehavior.Chasing);
            _behaviorBeforeStun = GhostBehavior.Chasing;
            _chaseUntil = Time.time + Mathf.Max(0.1f, targetLossDelay);
            _stateLockUntil = Time.time + stateLockDuration;
            _nextDestinationUpdate = 0f;
            _hasLastDestination = false;
        }

        void EnterRoam()
        {
            SetBehavior(GhostBehavior.Roaming);
            _behaviorBeforeStun = GhostBehavior.Roaming;
            _stateLockUntil = Time.time + stateLockDuration;
            _dwellUntil = 0f;
            _nextDestinationUpdate = 0f;
            _hasLastDestination = false;
        }

        void EnterInvestigate()
        {
            SetBehavior(GhostBehavior.Investigating);
            _behaviorBeforeStun = GhostBehavior.Investigating;
            _stateLockUntil = Time.time + stateLockDuration;
            _investigateUntil = Time.time + Mathf.Max(1f, investigateDuration);
            _investigatingArrived = false;
            _investigateYaw = transform.eulerAngles.y;
            _nextDestinationUpdate = 0f;
            _hasLastDestination = false;
        }

        void SetBehavior(GhostBehavior next)
        {
            if (_behavior == next)
                return;

            _behavior = next;
            BehaviorChanged?.Invoke(next);
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

            Vector3 destination = point.position;
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

            // Patrol must actually touch the waypoint: chasing uses stoppingDistance
            // for attack range, but that radius is larger than Reached(), which would
            // leave the agent parked short of the point forever.
            MoveTo(point.position, moveSpeed, 0.05f);
        }

        /// <summary>
        /// Searches the last known position after losing the player, then scans the
        /// area before returning to patrol. This keeps a fair "it went that way"
        /// window instead of instant telepathic re-acquisition or instant forgetting.
        /// </summary>
        void Investigate()
        {
            if (!_investigatingArrived)
            {
                if (Reached(_lastKnownPosition))
                {
                    _investigatingArrived = true;
                    _investigateYaw = transform.eulerAngles.y;
                    StopMovement();
                }
                else
                {
                    MoveTo(_lastKnownPosition, moveSpeed, 0.1f);
                    return;
                }
            }

            StopMovement();

            // Slow scan sweep so the search is readable and the ghost still turns
            // toward the player if they step back into view.
            _investigateYaw += Mathf.Max(1f, investigateLookSpeed) * Time.deltaTime;
            Quaternion look = Quaternion.Euler(0f, _investigateYaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 3f);
        }

        void ChasePlayer()
        {
            if (player == null)
                return;

            // Use the real 3D position so a player on another floor is not treated
            // as "already reached" just because the horizontal distance is small.
            if (Vector3.Distance(transform.position, player.position) <= stoppingDistance)
            {
                StopMovement();
                Face(player.position);
                return;
            }

            MoveTo(player.position, chaseSpeed, stoppingDistance);
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

            if (Vector3.Distance(transform.position, player.position) > attackDistance)
                return;

            if (Time.time < _nextAttackTime)
                return;

            // Do not start a swing through a wall. The actual damage is validated
            // again at the hit moment in ProcessAttackHit().
            if (!HasLineOfSight())
                return;

            // Starting the swing is animation only. Damage is applied exactly once
            // inside ProcessAttackHit(), at the punch's contact frame.
            _nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldown);
            BeginAttack();
        }

        void ProcessAttackHit()
        {
            if (_attackHitApplied || !IsAttacking)
                return;

            if (Time.time < _attackHitTime)
                return;

            // Consume the hit window immediately. If any validation below fails the
            // swing whiffs permanently; it cannot deal delayed damage after the
            // player escapes, the ghost is interrupted, or the target dies.
            _attackHitApplied = true;

            if (player == null || IsFrozen || ShouldHalt())
                return;

            GameManager manager = GameManager.Instance;
            if (manager == null || manager.State != GameState.Playing)
                return;

            ResolvePlayerHealth();
            if (_playerHealth == null || !_playerHealth.IsAlive)
                return;

            if (Vector3.Distance(transform.position, player.position) > attackDistance)
                return;

            if (!HasLineOfSight())
                return;

            _playerHealth.TakeDamage(1);
        }

        void BeginAttack()
        {
            // Keep the bool true for almost the whole clip so the punch can finish.
            _attackUntil = Time.time + Mathf.Max(0.25f, _attackClipLength - 0.08f);
            _attackHitTime = Time.time + Mathf.Max(0.05f, _attackClipLength * attackHitNormalizedTime);
            _attackHitApplied = false;
            _speedParameter = 0f;
            if (_animator != null)
                _animator.SetBool(IsAttackingHash, true);

            AttackStarted?.Invoke();
        }

        void EndAttack()
        {
            _attackUntil = 0f;
            _attackHitApplied = true;

            // The swing is longer than attackCooldown. Keep a small gap so Attack -> Idle
            // can finish before AnyState triggers a fresh swing (which would resume the
            // previous Attack state at its final frame instead of restarting cleanly).
            float recoveryUntil = Time.time + Mathf.Max(0f, attackRecovery);
            if (_nextAttackTime < recoveryUntil)
                _nextAttackTime = recoveryUntil;

            if (_animator != null)
                _animator.SetBool(IsAttackingHash, false);
        }

        void CancelAttack()
        {
            _attackUntil = 0f;
            _attackHitApplied = true;
            if (_animator != null && _animator.GetBool(IsAttackingHash))
                _animator.SetBool(IsAttackingHash, false);
        }

        void ResolvePlayerHealth()
        {
            if (_playerHealth != null)
                return;

            if (player != null)
            {
                _playerHealth = player.GetComponent<PlayerHealth>();
                if (_playerHealth == null)
                    _playerHealth = player.GetComponentInChildren<PlayerHealth>(true);
            }

            if (_playerHealth == null)
                _playerHealth = PlayerHealth.Instance;
        }

        void UpdateAnimator(float normalizedPlanarSpeed)
        {
            if (_animator == null)
                return;

            float target = Mathf.Clamp01(normalizedPlanarSpeed);

            // Chasing resolves to a hard Run, so do not blend up through Walk first.
            if (target >= 1f)
            {
                _speedParameter = 1f;
                _animator.SetFloat(SpeedHash, _speedParameter);
                return;
            }

            // Decelerate quickly so patrol dwells visibly reach Idle; accelerate normally.
            float rate = target < _speedParameter ? 9f : 3.5f;
            _speedParameter = Mathf.MoveTowards(_speedParameter, target, Time.deltaTime * rate);
            _animator.SetFloat(SpeedHash, _speedParameter);
        }

        float ResolveLocomotionSpeed01()
        {
            // Chasing always drives Run; the Attack state overrides locomotion via IsAttacking.
            if (_behavior == GhostBehavior.Chasing)
                return 1f;

            // Patrol: Walk while actually moving, Idle while dwelling or blocked.
            return MeasureActualSpeed01();
        }

        float MeasureActualSpeed01()
        {
            // Real displacement is more reliable than NavMeshAgent.velocity here:
            // when the agent is stopped for a dwell, this reads zero immediately.
            Vector3 velocity = Time.deltaTime > 0.0001f
                ? (transform.position - _animatorSamplePosition) / Time.deltaTime
                : Vector3.zero;

            velocity.y = 0f;
            return velocity.magnitude / Mathf.Max(0.1f, chaseSpeed);
        }

        static float ResolveClipLength(Animator animator, string clipName, float fallback)
        {
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null)
                return fallback;

            AnimationClip[] clips = controller.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == clipName)
                    return clips[i].length;
            }

            return fallback;
        }

        void LateUpdate()
        {
            _animatorSamplePosition = transform.position;
        }

        Vector3 GetEyePosition()
        {
            return transform.position + Vector3.up * eyeHeight;
        }

        /// <summary>Vision cone + range + occlusion. Used for long-range detection only.</summary>
        bool CanSeePlayer(Vector3 eye, Vector3 target, float distance)
        {
            if (player == null)
                return false;

            if (distance > detectionDistance + 0.001f)
                return false;

            if (distance > 0.01f)
            {
                float angle = Vector3.Angle(transform.forward, target - eye);
                if (angle > fieldOfView * 0.5f)
                    return false;
            }

            return HasLineOfSight(eye, target - eye, distance);
        }

        bool HasLineOfSight()
        {
            if (player == null)
                return false;

            Vector3 eye = GetEyePosition();
            Vector3 target = GetPlayerAimPoint();
            Vector3 toTarget = target - eye;
            return HasLineOfSight(eye, toTarget, toTarget.magnitude);
        }

        bool HasLineOfSight(Vector3 eye, Vector3 toTarget, float distance)
        {
            if (distance <= 0.02f)
                return true;

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

        void MoveTo(Vector3 destination, float speed, float stopDistance)
        {
            if (_useNavMesh && _agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.updateRotation = true;
                _agent.isStopped = false;
                _agent.speed = speed;
                _agent.stoppingDistance = stopDistance;

                if (Time.time >= _nextDestinationUpdate)
                {
                    // Sample the request onto the NavMesh instead of flattening Y.
                    // Flattening was what made multi-floor goals collapse onto the
                    // ground floor and caused the ghost to oscillate at the stairs.
                    Vector3 resolved = ResolveNavMeshPoint(destination);

                    // A warp, a re-enable or an external ResetPath clears the agent's
                    // path while the cached destination stays valid-looking. Without
                    // this check the ghost would think it already asked for that
                    // destination and stand still with no path at all.
                    bool pathMissing = !_agent.hasPath && !_agent.pathPending;

                    if (!_hasLastDestination || pathMissing ||
                        Vector3.Distance(resolved, _lastDestination) > 0.15f)
                    {
                        _agent.SetDestination(resolved);
                        _lastDestination = resolved;
                        _hasLastDestination = true;
                    }

                    _nextDestinationUpdate = Time.time + 0.2f;
                }

                return;
            }

            Face(destination);

            Vector3 flatDestination = Flatten(destination);
            Vector3 next = Vector3.MoveTowards(Flatten(transform.position), flatDestination, speed * Time.deltaTime);
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

        static Vector3 ResolveNavMeshPoint(Vector3 point)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                return hit.position;

            return point;
        }

        void StopMovement()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                // ResetPath first: the agent can clear its stopped flag while clearing the path.
                _agent.ResetPath();
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
            }

            _nextDestinationUpdate = 0f;
            _hasLastDestination = false;
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
            if (_useNavMesh && _agent != null && _agent.enabled && _agent.isOnNavMesh && _agent.hasPath && !_agent.pathPending)
                return _agent.remainingDistance <= Mathf.Max(0.35f, _agent.stoppingDistance + 0.15f);

            // 3D distance, not horizontal: a waypoint directly above/below on another
            // floor must not count as reached.
            return Vector3.Distance(transform.position, destination) <= Mathf.Max(0.5f, stoppingDistance * 0.45f);
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
                _playerHealth = null;
                return true;
            }

            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
            {
                player = tagged.transform;
                _playerHealth = null;
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
                CancelAttack();
                ApplyVisualState();
            }
        }

        void ApplyVisualState()
        {
            if (_renderers == null)
                return;

            // Normal play keeps the colours authored in the imported Mixamo materials.
            // Only stun and end-of-round feedback use a temporary override, so the
            // original material colours come back as soon as the state ends.
            if (!_halted && !IsFrozen)
            {
                ClearVisualOverride();
                return;
            }

            Color tint = _halted ? HaltColor : StunColor;
            if (_hasVisualOverride && _appliedTint == tint)
                return;

            if (_visualOverrides == null)
                _visualOverrides = new MaterialPropertyBlock();

            _visualOverrides.Clear();
            _visualOverrides.SetColor(BaseColorId, tint);
            _visualOverrides.SetColor(ColorId, tint);

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer != null)
                    renderer.SetPropertyBlock(_visualOverrides);
            }

            _appliedTint = tint;
            _hasVisualOverride = true;
        }

        void ClearVisualOverride()
        {
            if (!_hasVisualOverride)
                return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer != null)
                    renderer.SetPropertyBlock(null);
            }

            _hasVisualOverride = false;
        }

        static Vector3 Flatten(Vector3 position)
        {
            position.y = 0f;
            return position;
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
