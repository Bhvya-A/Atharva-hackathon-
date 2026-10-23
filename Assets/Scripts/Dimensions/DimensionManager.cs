using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TheOtherSide
{
    /// <summary>
    /// Authoritative dimension state. The player stays at the same world pose; only dimension content swaps.
    /// Future GhostAI should subscribe to <see cref="GhostStunRequested"/> (5s by default) rather than polling input.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class DimensionManager : MonoBehaviour
    {
        public static DimensionManager Instance { get; private set; }

        [SerializeField] float switchCooldown = 2f;
        [SerializeField] float transitionDuration = 0.45f;
        [SerializeField] float ghostStunDuration = 5f;
        [SerializeField] Color realityFlash = new Color(1f, 0.96f, 0.88f, 1f);
        [SerializeField] Color voidFlash = new Color(0.22f, 0.05f, 0.38f, 1f);

        RawImage _overlay;

        public Dimension Current { get; private set; } = Dimension.Reality;
        public bool IsTransitioning { get; private set; }
        public float SwitchCooldown => switchCooldown;
        public float TransitionDuration => transitionDuration;
        public float GhostStunDuration => ghostStunDuration;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownUntil - Time.time);
        public bool IsSwitchReady => !IsTransitioning && CooldownRemaining <= 0f;
        public bool IsGhostStunned => Time.time < _ghostStunUntil;
        public float GhostStunRemaining => Mathf.Max(0f, _ghostStunUntil - Time.time);

        public event Action<Dimension, Dimension> TransitionStarted;
        public event Action<Dimension, Dimension> DimensionChanged;
        public event Action<float> GhostStunRequested;

        float _cooldownUntil;
        float _ghostStunUntil;
        Coroutine _transitionRoutine;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        void OnDestroy()
        {
            if (_transitionRoutine != null)
                StopCoroutine(_transitionRoutine);

            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            ApplyPresence(Current);
        }

        void Update()
        {
            if (!GameInput.SwitchDimensionPressed)
                return;

            TrySwitch();
        }

        public void BindOverlay(RawImage overlay)
        {
            _overlay = overlay;
            if (_overlay != null)
            {
                Color hidden = _overlay.color;
                hidden.a = 0f;
                _overlay.color = hidden;
                _overlay.gameObject.SetActive(false);
            }
        }

        public void RefreshPresence()
        {
            ApplyPresence(Current);
        }

        public bool TrySwitch()
        {
            if (IsTransitioning)
                return false;

            if (GameManager.Instance == null || !GameManager.Instance.IsGameplayActive)
                return false;

            if (CooldownRemaining > 0f)
                return false;

            Dimension next = Current == Dimension.Reality ? Dimension.Void : Dimension.Reality;
            _transitionRoutine = StartCoroutine(SwitchRoutine(next));
            return true;
        }

        IEnumerator SwitchRoutine(Dimension next)
        {
            IsTransitioning = true;
            Dimension previous = Current;
            Color flash = next == Dimension.Void ? voidFlash : realityFlash;

            TransitionStarted?.Invoke(previous, next);

            float fadeIn = Mathf.Max(0.05f, transitionDuration * 0.45f);
            float fadeOut = Mathf.Max(0.05f, transitionDuration * 0.55f);

            yield return FadeOverlay(0f, 0.92f, fadeIn, flash);

            ApplyDimension(next);
            _cooldownUntil = Time.time + switchCooldown;
            _ghostStunUntil = Time.time + ghostStunDuration;
            GhostStunRequested?.Invoke(ghostStunDuration);
            DimensionChanged?.Invoke(previous, Current);

            yield return FadeOverlay(0.92f, 0f, fadeOut, flash);

            IsTransitioning = false;
            _transitionRoutine = null;
        }

        void ApplyDimension(Dimension next)
        {
            Current = next;
            ApplyPresence(next);
        }

        void ApplyPresence(Dimension dimension)
        {
            DimensionPresence[] objects = FindObjectsByType<DimensionPresence>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                    objects[i].Apply(dimension);
            }
        }

        IEnumerator FadeOverlay(float from, float to, float duration, Color rgb)
        {
            if (_overlay == null)
                yield break;

            _overlay.gameObject.SetActive(true);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                SetOverlay(rgb, Mathf.Lerp(from, to, t));
                yield return null;
            }

            SetOverlay(rgb, to);
            if (to <= 0.001f)
                _overlay.gameObject.SetActive(false);
        }

        void SetOverlay(Color rgb, float alpha)
        {
            if (_overlay == null)
                return;

            rgb.a = alpha;
            _overlay.color = rgb;
        }
    }
}
