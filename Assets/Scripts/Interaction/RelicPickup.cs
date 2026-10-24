using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Interactable relic. Picking it up only adds it to the player's carried relics;
    /// it does not count as deposited until the player uses the ritual table.
    /// The visual is a normalized model prefab (Resources/Relics/&lt;type&gt;) so the
    /// pickup, inventory and deposit rules stay independent of the artwork.
    /// </summary>
    [DisallowMultipleComponent]
    public class RelicPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] RelicType relicType = RelicType.Skull;
        [SerializeField] string relicName = "SKULL";
        [SerializeField] float spinSpeed = 22f;
        [SerializeField] float bobHeight = 0.05f;
        [SerializeField] float bobSpeed = 1.4f;

        bool _collected;
        Vector3 _basePosition;
        bool _hasBasePosition;

        public RelicType Type => relicType;
        public string Prompt => _collected ? string.Empty : $"PICK UP {relicName}";
        public bool CanInteract => !_collected;

        public void Configure(string displayName, RelicType type)
        {
            relicName = string.IsNullOrEmpty(displayName) ? RelicTypes.DisplayName(type) : displayName;
            relicType = type;
        }

        void Start()
        {
            CaptureBasePosition();
        }

        public void SetBasePosition(Vector3 position)
        {
            _basePosition = position;
            _hasBasePosition = true;
        }

        void CaptureBasePosition()
        {
            if (_hasBasePosition)
                return;

            _basePosition = transform.position;
            _hasBasePosition = true;
        }

        void Update()
        {
            if (_collected)
                return;

            if (!Mathf.Approximately(spinSpeed, 0f))
                transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);

            if (bobHeight > 0f)
            {
                float y = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
                transform.position = _basePosition + Vector3.up * y;
            }
        }

        public void Interact()
        {
            if (_collected)
                return;

            PlayerRelicCarrier carrier = PlayerRelicCarrier.Find();
            if (carrier == null)
            {
                Debug.LogWarning($"RelicPickup ({name}): no PlayerRelicCarrier was found on the player.");
                return;
            }

            // Consume immediately so a second interaction in the same frame (or a
            // duplicate raycast hit) cannot add the same relic twice.
            _collected = true;

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }

            carrier.AddRelic(relicType);

            int deposited = GameManager.Instance != null ? GameManager.Instance.RelicsCollected : 0;
            Debug.Log(
                $"[TheOtherSide] Picked up {relicName}. Carried: {carrier.CarriedRelics}. " +
                $"Deposited: {deposited}/{GameManager.RelicGoal}.");

            HorrorAudioManager.Instance?.PlayRelicPickup();
            HUDController.Instance?.ShowNotification($"{relicName} RELIC CLAIMED", 1.6f);

            Destroy(gameObject);
        }
    }
}
