using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Holds the relics and key the player is currently carrying.
    /// Deposited relics live in GameManager.RelicsCollected so they survive player respawns.
    /// The most recently picked-up relic is the one shown in the player's hand.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerRelicCarrier : MonoBehaviour
    {
        [SerializeField] int carriedRelics;
        [SerializeField] string heldKeyId = string.Empty;
        [SerializeField] List<RelicType> carriedTypes = new List<RelicType>();

        public int CarriedRelics => carriedRelics;
        public bool IsCarryingRelic => carriedRelics > 0;
        public bool HasAnyKey => !string.IsNullOrEmpty(heldKeyId);

        /// <summary>Relic currently presented in first person, or null when empty-handed.</summary>
        public RelicType? Equipped { get; private set; }

        public IReadOnlyList<RelicType> CarriedTypes => carriedTypes;

        /// <summary>Raised after a relic is added. Carries the relic type that was added.</summary>
        public event Action<RelicType> RelicAdded;

        /// <summary>Raised whenever the held relic changes (including to null).</summary>
        public event Action<RelicType?> EquippedChanged;

        /// <summary>Legacy untyped add. Kept so old callers cannot corrupt the count.</summary>
        public void AddRelic()
        {
            AddRelicInternal(null);
        }

        public void AddRelic(RelicType type)
        {
            AddRelicInternal(type);
        }

        void AddRelicInternal(RelicType? type)
        {
            carriedRelics++;
            if (type.HasValue)
                carriedTypes.Add(type.Value);

            RelicType? nextEquipped = type.HasValue
                ? type
                : (carriedTypes.Count > 0 ? carriedTypes[carriedTypes.Count - 1] : Equipped);

            SetEquipped(nextEquipped);

            if (type.HasValue)
                RelicAdded?.Invoke(type.Value);
        }

        /// <summary>Removes the most recently carried relic and updates the held visual.</summary>
        public bool RemoveRelic()
        {
            if (carriedRelics <= 0)
                return false;

            carriedRelics--;

            if (carriedTypes.Count > 0)
                carriedTypes.RemoveAt(carriedTypes.Count - 1);

            SetEquipped(carriedTypes.Count > 0
                ? carriedTypes[carriedTypes.Count - 1]
                : (RelicType?)null);

            return true;
        }

        void SetEquipped(RelicType? next)
        {
            if (Equipped == next)
                return;

            Equipped = next;
            EquippedChanged?.Invoke(Equipped);
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
