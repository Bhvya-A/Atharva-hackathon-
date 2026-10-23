using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Interactable relic. Picking it up only adds it to the player's carried relics;
    /// it does not count as deposited until the player uses the ritual table.
    /// </summary>
    [DisallowMultipleComponent]
    public class RelicPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] string relicName = "Void Relic";
        [SerializeField] float spinSpeed = 45f;

        bool _collected;

        public string Prompt => _collected ? string.Empty : $"Take {relicName}";
        public bool CanInteract => !_collected;

        public void Configure(string displayName)
        {
            relicName = displayName;
        }

        void Update()
        {
            if (!_collected && !Mathf.Approximately(spinSpeed, 0f))
                transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
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

            _collected = true;
            carrier.AddRelic();

            int deposited = GameManager.Instance != null ? GameManager.Instance.RelicsCollected : 0;
            Debug.Log(
                $"[TheOtherSide] Picked up {relicName}. Carried: {carrier.CarriedRelics}. " +
                $"Deposited: {deposited}/{GameManager.RelicGoal}.");

            Destroy(gameObject);
        }
    }
}
