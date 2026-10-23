using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Locked container that consumes a matching key and reveals its relic.
    /// Interaction is refused (with a prompt) while the key is missing.
    /// </summary>
    [DisallowMultipleComponent]
    public class RelicKeyContainer : MonoBehaviour, IInteractable
    {
        [SerializeField] string requiredKeyId = "rusty_key";
        [SerializeField] string requiredKeyName = "Rusty Key";
        [SerializeField] GameObject relicToReveal;
        [SerializeField] Renderer statusRenderer;
        [SerializeField] Color unlockedColor = new Color(0.25f, 0.85f, 0.45f);

        bool _unlocked;
        PlayerRelicCarrier _carrier;

        public string Prompt
        {
            get
            {
                if (_unlocked)
                    return string.Empty;

                PlayerRelicCarrier carrier = ResolveCarrier();
                return carrier != null && carrier.HasKey(requiredKeyId)
                    ? $"Unlock with {requiredKeyName}"
                    : $"Locked - need {requiredKeyName}";
            }
        }

        public bool CanInteract => !_unlocked;

        public void Configure(string keyId, string keyDisplayName, GameObject relic, Renderer renderer)
        {
            requiredKeyId = keyId;
            requiredKeyName = keyDisplayName;
            relicToReveal = relic;
            statusRenderer = renderer;
        }

        public void Interact()
        {
            if (_unlocked)
                return;

            PlayerRelicCarrier carrier = ResolveCarrier();
            if (carrier == null || !carrier.HasKey(requiredKeyId))
            {
                Debug.Log($"[TheOtherSide] {name} is locked. Find the {requiredKeyName} first.");
                return;
            }

            carrier.ConsumeKey(requiredKeyId);
            _unlocked = true;

            if (relicToReveal != null)
                relicToReveal.SetActive(true);

            if (statusRenderer != null)
                statusRenderer.material.color = unlockedColor;

            Debug.Log($"[TheOtherSide] Unlocked {name}. A relic is now available.");
        }

        PlayerRelicCarrier ResolveCarrier()
        {
            if (_carrier == null)
                _carrier = PlayerRelicCarrier.Find();

            return _carrier;
        }
    }
}
