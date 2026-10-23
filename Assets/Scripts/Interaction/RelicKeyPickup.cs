using UnityEngine;

namespace TheOtherSide
{
    /// <summary>Simple key pickup that stores one key id on the player's carrier.</summary>
    [DisallowMultipleComponent]
    public class RelicKeyPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] string keyId = "rusty_key";
        [SerializeField] string displayName = "Rusty Key";
        [SerializeField] float spinSpeed = 60f;

        bool _taken;

        public string Prompt => _taken ? string.Empty : $"Take {displayName}";
        public bool CanInteract => !_taken;

        public void Configure(string id, string name)
        {
            keyId = id;
            displayName = name;
        }

        void Update()
        {
            if (!_taken && !Mathf.Approximately(spinSpeed, 0f))
                transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
        }

        public void Interact()
        {
            if (_taken)
                return;

            PlayerRelicCarrier carrier = PlayerRelicCarrier.Find();
            if (carrier == null)
            {
                Debug.LogWarning($"RelicKeyPickup ({name}): no PlayerRelicCarrier was found on the player.");
                return;
            }

            _taken = true;
            carrier.CollectKey(keyId);
            Debug.Log($"[TheOtherSide] Picked up {displayName}.");
            Destroy(gameObject);
        }
    }
}
