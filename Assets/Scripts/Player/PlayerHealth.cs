using System;
using System.Collections;
using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Single authoritative health/damage entry point for the player.
    /// One valid ghost hit always removes exactly one point, a life is only lost
    /// when health reaches zero, and respawning restores health after a short
    /// unscaled-time transition with a configurable immunity window.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] int maxHealth = 5;
        [SerializeField] float respawnDelay = 0.9f;
        [SerializeField] float respawnInvulnerability = 2f;

        [Header("Respawn")]
        [Tooltip("Optional scene marker. Falls back to the position where the player spawned.")]
        [SerializeField] Transform spawnPoint;

        public static PlayerHealth Instance { get; private set; }

        public event Action<int, int> HealthChanged;
        public event Action<int, int> Damaged;
        public event Action Died;
        public event Action Respawned;

        public int CurrentHealth { get; private set; }
        public int MaxHealth => Mathf.Max(1, maxHealth);
        public bool IsDead { get; private set; }
        public bool IsAlive => !IsDead;
        public bool IsImmune => Time.time < _immuneUntil;
        public float ImmunityRemaining => Mathf.Max(0f, _immuneUntil - Time.time);
        public float RespawnInvulnerability => respawnInvulnerability;

        Vector3 _spawnPosition;
        Quaternion _spawnRotation;
        float _immuneUntil;
        float _lastDamageTime = -10f;
        CharacterController _controller;
        FirstPersonController _motor;
        Coroutine _deathRoutine;
        bool _spawnConfigured;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("PlayerHealth: duplicate player health component removed.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            CurrentHealth = MaxHealth;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _controller = GetComponent<CharacterController>();
            _motor = GetComponent<FirstPersonController>();
        }

        void Start()
        {
            if (spawnPoint != null)
                ConfigureSpawn(spawnPoint, false);

            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        void OnDestroy()
        {
            if (_deathRoutine != null)
                StopCoroutine(_deathRoutine);

            if (Instance == this)
                Instance = null;
        }

        public void ConfigureSpawn(Transform point, bool useCurrentAsFallback = true)
        {
            if (point != null)
            {
                spawnPoint = point;
                _spawnPosition = point.position;
                _spawnRotation = point.rotation;
                _spawnConfigured = true;
                return;
            }

            if (useCurrentAsFallback)
            {
                _spawnPosition = transform.position;
                _spawnRotation = transform.rotation;
                _spawnConfigured = true;
            }
        }

        public void ConfigureSpawn(Vector3 position, Quaternion rotation)
        {
            _spawnPosition = position;
            _spawnRotation = rotation;
            _spawnConfigured = true;
        }

        /// <summary>
        /// Applies at most one point of damage per call. Returns true only when
        /// health actually changed, which lets callers rely on a single hit event
        /// per valid attack.
        /// </summary>
        public bool TakeDamage(int amount = 1)
        {
            if (IsDead || amount <= 0)
                return false;

            if (GameManager.Instance == null || !GameManager.Instance.IsGameplayActive)
                return false;

            if (IsImmune)
                return false;

            // A single ghost swing must never remove more than one point.
            int applied = Mathf.Clamp(amount, 1, 1);

            // Safety net against duplicate callbacks in the same simulation step.
            if (Time.time - _lastDamageTime < 0.02f)
                return false;

            _lastDamageTime = Time.time;
            CurrentHealth = Mathf.Max(0, CurrentHealth - applied);

            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
            Damaged?.Invoke(CurrentHealth, MaxHealth);

            if (CurrentHealth <= 0)
            {
                IsDead = true;
                Died?.Invoke();
                BeginLifeLoss();
            }

            return true;
        }

        public void HealFull()
        {
            if (IsDead)
                return;

            CurrentHealth = MaxHealth;
            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        void BeginLifeLoss()
        {
            GameManager manager = GameManager.Instance;
            int remainingLives = manager != null ? manager.LoseLife(1) : 0;

            if (manager == null || remainingLives <= 0)
            {
                // GameManager has switched to GameOver, which stops the clock and
                // enables the cursor for the restart button. There is no respawn.
                return;
            }

            _deathRoutine = StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            // Let the hit vignette and death feedback play without using scaled time,
            // because a pause or game-over transition may set Time.timeScale to zero.
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, respawnDelay));

            if (GameManager.Instance == null || GameManager.Instance.Lives <= 0)
            {
                _deathRoutine = null;
                yield break;
            }

            TeleportToSpawn();

            CurrentHealth = MaxHealth;
            _immuneUntil = Time.time + Mathf.Max(0f, respawnInvulnerability);
            IsDead = false;
            _lastDamageTime = -10f;
            _deathRoutine = null;

            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
            Respawned?.Invoke();
        }

        void TeleportToSpawn()
        {
            if (!_spawnConfigured)
            {
                _spawnPosition = transform.position;
                _spawnRotation = transform.rotation;
            }

            if (_controller == null)
                _controller = GetComponent<CharacterController>();

            bool controllerWasEnabled = _controller != null && _controller.enabled;
            if (controllerWasEnabled)
                _controller.enabled = false;

            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);

            if (controllerWasEnabled)
                _controller.enabled = true;

            if (_motor == null)
                _motor = GetComponent<FirstPersonController>();

            _motor?.ResetMotion();

            CameraShake shake = CameraShake.Instance;
            if (shake == null)
                shake = GetComponentInChildren<CameraShake>(true);
            shake?.ResetShake();
        }
    }
}
