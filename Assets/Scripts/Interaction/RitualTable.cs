using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Reality-side deposit point. One carried relic is deposited per interaction,
    /// using GameManager.CollectRelic so the HUD and victory state stay authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public class RitualTable : MonoBehaviour, IInteractable
    {
        PlayerRelicCarrier _carrier;

        public string Prompt
        {
            get
            {
                PlayerRelicCarrier carrier = ResolveCarrier();
                if (carrier == null || carrier.CarriedRelics <= 0)
                    return "Ritual Table - need a relic";

                return carrier.CarriedRelics == 1
                    ? "Deposit relic"
                    : $"Deposit relic ({carrier.CarriedRelics} carried)";
            }
        }

        // Keep the prompt visible even with nothing to deposit so the table is discoverable.
        public bool CanInteract => true;

        public void Interact()
        {
            GameManager manager = GameManager.Instance;
            PlayerRelicCarrier carrier = ResolveCarrier();

            if (manager == null || carrier == null || manager.State != GameState.Playing)
                return;

            if (!carrier.RemoveRelic())
            {
                Debug.Log("[TheOtherSide] The ritual table needs a carried relic.");
                return;
            }

            manager.CollectRelic();
            Debug.Log(
                $"[TheOtherSide] Relic deposited. Progress: " +
                $"{manager.RelicsCollected}/{GameManager.RelicGoal}.");
        }

        PlayerRelicCarrier ResolveCarrier()
        {
            if (_carrier == null)
                _carrier = PlayerRelicCarrier.Find();

            return _carrier;
        }
    }
}
