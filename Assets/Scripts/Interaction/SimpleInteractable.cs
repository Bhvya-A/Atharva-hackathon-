using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Test interactable used by bootstrap. Replace or extend for relics, doors, and puzzles later.
    /// </summary>
    public class SimpleInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] string prompt = "Inspect";
        [SerializeField] string inspectedMessage = "Nothing useful... yet.";

        Renderer _renderer;
        Color _baseColor;
        bool _inspected;

        public string Prompt => _inspected ? "Already inspected" : prompt;
        public bool CanInteract => !_inspected;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _baseColor = _renderer.material.color;
        }

        public void Interact()
        {
            if (!CanInteract)
                return;

            _inspected = true;
            if (_renderer != null)
                _renderer.material.color = Color.Lerp(_baseColor, Color.yellow, 0.65f);

            HorrorAudioManager.Instance?.PlayInteraction();
            Debug.Log($"[TheOtherSide] {name}: {inspectedMessage}");
        }
    }
}
