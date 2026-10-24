using System;
using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Player time-freeze power (F). Reuses <see cref="GhostAI.Freeze"/> so movement,
    /// attacks, stun visuals and behaviour resume share the dimension-switch stun path.
    /// </summary>
    [DisallowMultipleComponent]
    public class TimeFreezeAbility : MonoBehaviour
    {
        [SerializeField] float freezeDuration = 5f;
        [SerializeField] float cooldownDuration = 10f;
        [SerializeField] float activationMessageDuration = 2.5f;
        [SerializeField] float readyMessageDuration = 2f;

        public static TimeFreezeAbility Instance { get; private set; }

        public event Action Activated;
        public event Action CooldownBecameReady;

        GhostAI _ghost;
        PlayerHealth _health;
        float _cooldownUntil;
        bool _cooldownWasRunning;

        public bool IsReady => Time.time >= _cooldownUntil;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownUntil - Time.time);
        public float FreezeDuration => freezeDuration;
        public float CooldownDuration => cooldownDuration;
        public float CooldownProgress01 => cooldownDuration <= 0f
            ? 1f
            : Mathf.Clamp01(1f - CooldownRemaining / cooldownDuration);

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("TimeFreezeAbility: duplicate ability component removed.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            _health = GetComponent<PlayerHealth>();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (!IsReady)
            {
                _cooldownWasRunning = true;
            }
            else if (_cooldownWasRunning)
            {
                _cooldownWasRunning = false;
                CooldownBecameReady?.Invoke();
                HUDController.Instance?.ShowTimeFreezeMessage("Time Freeze ready (F)", readyMessageDuration);
            }

            if (GameInput.TimeFreezePressed)
                TryActivate();
        }

        public bool TryActivate()
        {
            if (GameManager.Instance != null && !GameManager.Instance.IsGameplayActive)
                return false;

            if (_health == null)
                _health = GetComponent<PlayerHealth>();

            if (_health != null && _health.IsDead)
                return false;

            if (!IsReady)
            {
                HUDController.Instance?.ShowTimeFreezeMessage($"Time Freeze  {CooldownRemaining:0.0}s", 1f);
                return false;
            }

            GhostAI ghost = ResolveGhost();
            if (ghost == null)
            {
                HUDController.Instance?.ShowTimeFreezeMessage("Time Freeze: no target", 1.5f);
                return false;
            }

            ghost.Freeze(freezeDuration);
            _cooldownUntil = Time.time + cooldownDuration;
            _cooldownWasRunning = true;

            HorrorAudioManager.Instance?.PlayFreezeStart();
            StartCoroutine(FreezeEndCue(freezeDuration));

            Activated?.Invoke();
            HUDController.Instance?.ShowTimeFreezeMessage(
                $"Time Freeze!  Ghost frozen {freezeDuration:0}s",
                activationMessageDuration);
            return true;
        }

        GhostAI ResolveGhost()
        {
            if (_ghost != null)
                return _ghost;

            _ghost = FindAnyObjectByType<GhostAI>();
            return _ghost;
        }

        System.Collections.IEnumerator FreezeEndCue(float delay)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, delay));
            HorrorAudioManager.Instance?.PlayFreezeEnd();
        }
    }
}
