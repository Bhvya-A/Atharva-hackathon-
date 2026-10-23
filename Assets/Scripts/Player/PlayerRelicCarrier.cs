using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Holds the relics and key the player is currently carrying.
    /// Deposited relics live in GameManager.RelicsCollected so they survive player respawns.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerRelicCarrier : MonoBehaviour
    {
        [SerializeField] int carriedRelics;
        [SerializeField] string heldKeyId = string.Empty;

        public int CarriedRelics => carriedRelics;
        public bool IsCarryingRelic => carriedRelics > 0;
        public bool HasAnyKey => !string.IsNullOrEmpty(heldKeyId);

        public void AddRelic()
        {
            carriedRelics++;
        }

        public bool RemoveRelic()
        {
            if (carriedRelics <= 0)
                return false;

            carriedRelics--;
            return true;
        }

        public void CollectKey(string keyId)
        {
            heldKeyId = keyId;
        }

        public bool HasKey(string keyId)
        {
            return !string.IsNullOrEmpty(keyId) && heldKeyId == keyId;
        }

        public bool ConsumeKey(string keyId)
        {
            if (!HasKey(keyId))
                return false;

            heldKeyId = string.Empty;
            return true;
        }

        public static PlayerRelicCarrier Find()
        {
            return UnityEngine.Object.FindAnyObjectByType<PlayerRelicCarrier>();
        }
    }
}
