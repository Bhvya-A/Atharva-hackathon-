using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Small additive camera-position shake used for hit feedback.
    /// The player controller owns camera rotation, so this only offsets local position.
    /// Runs on unscaled time so it still settles while a menu pauses the game.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class CameraShake : MonoBehaviour
    {
        public static CameraShake Instance { get; private set; }

        [SerializeField] float maxOffset = 0.055f;

        Vector3 _baseLocalPosition;
        bool _hasBase;
        float _intensity;
        float _endTime;

        void Awake()
        {
            Instance = this;
            CaptureBase();
        }

        void OnEnable()
        {
            Instance = this;
            CaptureBase();
        }

        void Start()
        {
            CaptureBase();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Shake(float intensity, float duration)
        {
            if (intensity <= 0f || duration <= 0f)
                return;

            CaptureBase();
            _intensity = Mathf.Max(_intensity, intensity);
            _endTime = Mathf.Max(_endTime, Time.unscaledTime + duration);
        }

        public void ResetShake()
        {
            _intensity = 0f;
            _endTime = 0f;
            if (_hasBase)
                transform.localPosition = _baseLocalPosition;
        }

        void LateUpdate()
        {
            if (!_hasBase)
                CaptureBase();

            if (Time.unscaledTime >= _endTime || _intensity <= 0f)
            {
                if (_hasBase && transform.localPosition != _baseLocalPosition)
                    transform.localPosition = _baseLocalPosition;
                return;
            }

            float falloff = Mathf.Clamp01((_endTime - Time.unscaledTime) / 0.35f);
            float seed = Time.unscaledTime * 38f;

            Vector3 offset = new Vector3(
                Mathf.PerlinNoise(seed, 0.17f) - 0.5f,
                Mathf.PerlinNoise(0.31f, seed) - 0.5f,
                0f) * (maxOffset * _intensity * falloff);

            transform.localPosition = _baseLocalPosition + offset;
        }

        void CaptureBase()
        {
            // Capture once, after the bootstrap has parented this camera to the player.
            if (_hasBase)
                return;

            _baseLocalPosition = transform.localPosition;
            _hasBase = true;
        }
    }
}
