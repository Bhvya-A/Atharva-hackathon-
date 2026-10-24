using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Presents the equipped relic in the lower part of the first-person view.
    /// Lives on the active camera so dimension swaps (which only toggle
    /// DimensionPresence objects) and respawns (which move the player root) cannot
    /// detach it. No rig or animation controller is required: placement, idle sway
    /// and a restrained movement response are all local-transform offsets.
    /// </summary>
    [DisallowMultipleComponent]
    public class RelicHolder : MonoBehaviour
    {
        [Serializable]
        public class HoldProfile
        {
            public RelicType type = RelicType.Skull;
            public Vector3 localPosition = new Vector3(0.26f, -0.24f, 0.44f);
            public Vector3 localEulerAngles = Vector3.zero;
            public float scale = 1f;
        }

        [Header("Placement")]
        [SerializeField] Transform holdRoot;
        [SerializeField] HoldProfile[] profiles = Array.Empty<HoldProfile>();

        [Header("Idle sway")]
        [SerializeField] float swayPosition = 0.006f;
        [SerializeField] float swayRotation = 0.7f;
        [SerializeField] float swaySpeed = 0.85f;

        [Header("Movement response")]
        [SerializeField] float bobPosition = 0.012f;
        [SerializeField] float bobRotation = 1.4f;
        [SerializeField] float bobFrequency = 7.5f;
        [SerializeField] float movementDamping = 8f;
        [SerializeField] float lookLagDegrees = 1.8f;

        [Header("Lighting")]
        [Tooltip("Optional small light so the relic stays readable in dark rooms.")]
        [SerializeField] Light holdLight;

        static readonly Dictionary<RelicType, HoldProfile> Defaults = new Dictionary<RelicType, HoldProfile>
        {
            { RelicType.Skull, new HoldProfile { type = RelicType.Skull, localPosition = new Vector3(0.27f, -0.17f, 0.80f), localEulerAngles = new Vector3(-6f, -22f, 4f), scale = 1.15f } },
            { RelicType.Grimoire, new HoldProfile { type = RelicType.Grimoire, localPosition = new Vector3(0.28f, -0.22f, 0.76f), localEulerAngles = new Vector3(12f, -28f, -6f), scale = 1f } },
            { RelicType.Candle, new HoldProfile { type = RelicType.Candle, localPosition = new Vector3(0.27f, -0.20f, 0.72f), localEulerAngles = new Vector3(2f, -14f, 3f), scale = 1f } },
            { RelicType.Compass, new HoldProfile { type = RelicType.Compass, localPosition = new Vector3(0.24f, -0.15f, 0.60f), localEulerAngles = new Vector3(52f, -16f, 0f), scale = 1f } }
        };

        readonly Dictionary<RelicType, GameObject> _instances = new Dictionary<RelicType, GameObject>();

        PlayerRelicCarrier _carrier;
        FirstPersonController _motor;
        RelicType? _shown;
        Vector3 _basePosition;
        Quaternion _baseRotation;
        Vector3 _movementOffset;
        Vector3 _movementEuler;
        float _bobPhase;
        float _lastYaw;
        float _lastPitch;
        float _lookLagX;
        float _lookLagY;
        bool _hasLookSample;
        bool _bound;

        public RelicType? ShownRelic => _shown;

        void Awake()
        {
            if (holdRoot == null)
                holdRoot = transform;

            CacheLookSample();
        }

        void OnEnable()
        {
            CacheLookSample();
        }

        void OnDisable()
        {
            UnbindCarrier();
        }

        void OnDestroy()
        {
            UnbindCarrier();
            ClearInstances();
        }

        public void Configure(Light light)
        {
            holdLight = light;
        }

        void Update()
        {
            BindCarrier();
            RefreshEquipped();
            Animate();
        }

        void BindCarrier()
        {
            if (_bound && _carrier != null)
                return;

            PlayerRelicCarrier carrier = PlayerRelicCarrier.Find();
            if (carrier == null || carrier == _carrier)
                return;

            UnbindCarrier();
            _carrier = carrier;
            _carrier.EquippedChanged += OnEquippedChanged;
            _bound = true;

            if (_motor == null)
                _motor = GetComponentInParent<FirstPersonController>();

            OnEquippedChanged(_carrier.Equipped);
        }

        void UnbindCarrier()
        {
            if (_bound && _carrier != null)
                _carrier.EquippedChanged -= OnEquippedChanged;

            _bound = false;
            _carrier = null;
        }

        void OnEquippedChanged(RelicType? type)
        {
            Show(type);
        }

        void RefreshEquipped()
        {
            if (_carrier == null)
                return;

            if (_carrier.Equipped != _shown)
                Show(_carrier.Equipped);
        }

        void Show(RelicType? type)
        {
            if (_shown == type && (type == null || (type.HasValue && _instances.ContainsKey(type.Value))))
                return;

            if (_shown.HasValue && _instances.TryGetValue(_shown.Value, out GameObject previous) && previous != null)
                previous.SetActive(false);

            _shown = type;

            if (!type.HasValue)
            {
                SetLightActive(false);
                return;
            }

            GameObject instance = GetOrCreate(type.Value);
            if (instance == null)
            {
                SetLightActive(false);
                return;
            }

            instance.SetActive(true);
            ApplyProfile(instance, ResolveProfile(type.Value));
            SetLightActive(true);
        }

        GameObject GetOrCreate(RelicType type)
        {
            if (_instances.TryGetValue(type, out GameObject existing) && existing != null)
                return existing;

            GameObject prefab = Resources.Load<GameObject>(RelicTypes.ResourcePath(type));
            if (prefab == null)
            {
                Debug.LogWarning($"RelicHolder: missing relic prefab at Resources/{RelicTypes.ResourcePath(type)}.");
                return null;
            }

            GameObject instance = Instantiate(prefab, holdRoot, false);
            instance.name = $"Held_{type}";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // The held copy must never be targetable, collide, or respond to the
            // dimension system. The world prefab keeps those components.
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycast < 0)
                ignoreRaycast = 2;

            SetLayerRecursively(instance, ignoreRaycast);

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }

            RelicPickup[] pickups = instance.GetComponentsInChildren<RelicPickup>(true);
            for (int i = 0; i < pickups.Length; i++)
            {
                if (pickups[i] != null)
                    Destroy(pickups[i]);
            }

            DimensionPresence[] presences = instance.GetComponentsInChildren<DimensionPresence>(true);
            for (int i = 0; i < presences.Length; i++)
            {
                if (presences[i] != null)
                    Destroy(presences[i]);
            }

            // Held relics must not be frozen by Time.timeScale (pause/game over).
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            instance.SetActive(false);
            _instances[type] = instance;
            return instance;
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }

        HoldProfile ResolveProfile(RelicType type)
        {
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Length; i++)
                {
                    if (profiles[i] != null && profiles[i].type == type)
                        return profiles[i];
                }
            }

            return Defaults.TryGetValue(type, out HoldProfile fallback)
                ? fallback
                : new HoldProfile { type = type };
        }

        void ApplyProfile(GameObject instance, HoldProfile profile)
        {
            _baseRotation = Quaternion.Euler(profile.localEulerAngles);
            instance.transform.localRotation = _baseRotation;
            instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, profile.scale);
            instance.transform.localPosition = profile.localPosition;

            // Prefab pivots are not consistent (the compass hangs from its lid, the
            // grimoire sits on its base). Re-centre the visible bounds on the hold
            // point so every relic is framed the same way in the lower view.
            Vector3 offset = Vector3.zero;
            if (holdRoot != null && TryGetVisualBounds(instance, out Bounds bounds))
            {
                Vector3 center = holdRoot.InverseTransformPoint(bounds.center);
                offset = profile.localPosition - center;
            }

            _basePosition = profile.localPosition + offset;
            instance.transform.localPosition = _basePosition;
        }

        static bool TryGetVisualBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        void Animate()
        {
            if (!_shown.HasValue || !_instances.TryGetValue(_shown.Value, out GameObject instance) || instance == null)
                return;

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            float speed01 = 0f;

            if (_motor != null)
            {
                Vector3 planar = _motor.PlanarVelocity;
                speed01 = Mathf.Clamp01(planar.magnitude / Mathf.Max(0.01f, _motor.WalkSpeed));
                _bobPhase += dt * bobFrequency * (0.4f + speed01);
            }

            // Restrained movement response: a small lag opposite to walking direction
            // and a vertical bob that only becomes obvious while moving.
            Vector3 targetOffset = new Vector3(
                Mathf.Sin(_bobPhase) * bobPosition * speed01,
                Mathf.Cos(_bobPhase * 2f) * bobPosition * 0.5f * speed01,
                0f);

            Vector3 targetEuler = new Vector3(
                Mathf.Cos(_bobPhase * 2f) * bobRotation * speed01,
                Mathf.Sin(_bobPhase) * bobRotation * 0.6f * speed01,
                0f);

            _movementOffset = Vector3.Lerp(_movementOffset, targetOffset, 1f - Mathf.Exp(-movementDamping * dt));
            _movementEuler = Vector3.Lerp(_movementEuler, targetEuler, 1f - Mathf.Exp(-movementDamping * dt));

            UpdateLookLag(dt);

            float t = Time.unscaledTime * swaySpeed;
            Vector3 idle = new Vector3(
                Mathf.Sin(t * 1.13f) * swayPosition,
                Mathf.Sin(t * 0.77f + 1.2f) * swayPosition * 0.7f,
                0f);

            Vector3 idleEuler = new Vector3(
                Mathf.Sin(t * 0.91f) * swayRotation * 0.6f,
                Mathf.Sin(t * 0.63f + 2.1f) * swayRotation,
                Mathf.Sin(t * 1.37f) * swayRotation * 0.5f);

            instance.transform.localPosition = _basePosition + idle + _movementOffset;
            instance.transform.localRotation = _baseRotation * Quaternion.Euler(
                idleEuler + _movementEuler + new Vector3(_lookLagY, _lookLagX, 0f));
        }

        void UpdateLookLag(float dt)
        {
            if (holdRoot == null)
                return;

            float yaw = NormalizeAngle(holdRoot.eulerAngles.y);
            float pitch = NormalizeAngle(holdRoot.eulerAngles.x);

            if (!_hasLookSample)
            {
                _lastYaw = yaw;
                _lastPitch = pitch;
                _hasLookSample = true;
                return;
            }

            float yawDelta = Mathf.DeltaAngle(_lastYaw, yaw);
            float pitchDelta = Mathf.DeltaAngle(_lastPitch, pitch);
            _lastYaw = yaw;
            _lastPitch = pitch;

            float targetX = Mathf.Clamp(yawDelta * 0.35f, -lookLagDegrees, lookLagDegrees);
            float targetY = Mathf.Clamp(-pitchDelta * 0.35f, -lookLagDegrees, lookLagDegrees);

            float blend = 1f - Mathf.Exp(-10f * dt);
            _lookLagX = Mathf.Lerp(_lookLagX, targetX, blend);
            _lookLagY = Mathf.Lerp(_lookLagY, targetY, blend);
        }

        static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }

        void CacheLookSample()
        {
            if (holdRoot == null)
                return;

            _lastYaw = NormalizeAngle(holdRoot.eulerAngles.y);
            _lastPitch = NormalizeAngle(holdRoot.eulerAngles.x);
            _hasLookSample = true;
        }

        void SetLightActive(bool active)
        {
            if (holdLight != null && holdLight.gameObject.activeSelf != active)
                holdLight.gameObject.SetActive(active);
        }

        void ClearInstances()
        {
            foreach (KeyValuePair<RelicType, GameObject> pair in _instances)
            {
                if (pair.Value != null)
                    Destroy(pair.Value);
            }

            _instances.Clear();
        }
    }
}
