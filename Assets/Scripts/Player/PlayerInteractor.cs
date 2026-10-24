using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Center-screen interaction probe. Raycasts from the active camera's exact
    /// center, rejects the player's own colliders and trigger volumes, and only
    /// accepts the nearest solid hit. Because the nearest hit must itself be the
    /// interactable, a wall between the camera and a relic blocks the prompt and
    /// the pickup.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("Raycast")]
        [SerializeField] Camera viewCamera;
        [SerializeField] float interactDistance = 2.6f;
        [Tooltip("Layers that can block the probe or contain interactables. Exclude the player and held-item layers.")]
        [SerializeField] LayerMask interactMask = ~0;
        [Tooltip("Optional extra filter for the player's own body. Leave empty to auto-detect from this component's hierarchy.")]
        [SerializeField] Transform playerRoot;

        [Header("Debug")]
        [SerializeField] bool logTargetChanges;

        readonly RaycastHit[] _hits = new RaycastHit[16];

        IInteractable _current;
        bool _hasTarget;

        public IInteractable Current => _current;
        public bool HasTarget => _hasTarget;
        public float InteractionDistance => interactDistance;

        public void Configure(Camera cam)
        {
            viewCamera = cam;
        }

        public void Configure(float distance, LayerMask mask)
        {
            interactDistance = Mathf.Max(0.1f, distance);
            interactMask = mask;
        }

        void Update()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsGameplayActive)
            {
                SetCurrent(null);
                HUDController.Instance?.SetCrosshairState(false);
                HUDController.Instance?.SetCrosshairVisible(false);
                return;
            }

            PlayerHealth health = PlayerHealth.Instance;
            if (health != null && health.IsDead)
            {
                SetCurrent(null);
                HUDController.Instance?.SetCrosshairState(false);
                HUDController.Instance?.SetCrosshairVisible(false);
                return;
            }

            HUDController.Instance?.SetCrosshairVisible(true);

            if (viewCamera == null)
                viewCamera = Camera.main;

            IInteractable target = FindTarget();
            SetCurrent(target);

            bool canInteract = target != null && target.CanInteract;
            HUDController.Instance?.SetCrosshairState(canInteract);
            HUDController.Instance?.SetPrompt(canInteract ? target.Prompt : string.Empty);

            if (canInteract && GameInput.InteractPressed)
                TryInteract();
        }

        /// <summary>
        /// Interacts with the current target. Update() calls this on the interact
        /// binding; it is public so UI buttons and automated tests can reuse the
        /// exact same path (clear target, clear prompt, then Interact()).
        /// </summary>
        public bool TryInteract()
        {
            if (_current == null || !_current.CanInteract)
                return false;

            // Clear the target first: Interact() may destroy the object and we
            // must not keep a stale reference for the next frame.
            IInteractable target = _current;
            SetCurrent(null);
            HUDController.Instance?.SetPrompt(string.Empty);
            HUDController.Instance?.SetCrosshairState(false);
            target.Interact();
            return true;
        }

        IInteractable FindTarget()
        {
            if (viewCamera == null)
                return null;

            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(
                ray,
                _hits,
                Mathf.Max(0.1f, interactDistance),
                interactMask,
                QueryTriggerInteraction.Ignore);

            if (count <= 0)
                return null;

            Transform root = ResolvePlayerRoot();
            float nearest = float.MaxValue;
            IInteractable found = null;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider == null)
                    continue;

                Transform hitTransform = hit.collider.transform;
                if (IsPartOfPlayer(hitTransform, root))
                    continue;

                // A held relic lives on Ignore Raycast and has disabled colliders,
                // but keep the guard in case a future prop forgets that rule.
                if (hit.collider.GetComponentInParent<RelicHolder>() != null)
                    continue;

                if (hit.distance >= nearest)
                    continue;

                IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable == null)
                {
                    // Solid, non-interactable geometry blocks everything behind it.
                    nearest = hit.distance;
                    found = null;
                    continue;
                }

                nearest = hit.distance;
                found = interactable;
            }

            if (logTargetChanges && found != _current)
                Debug.Log($"[TheOtherSide] Interact target: {(found != null ? found.GetType().Name : "none")}");

            return found;
        }

        Transform ResolvePlayerRoot()
        {
            if (playerRoot != null)
                return playerRoot;

            FirstPersonController controller = GetComponentInParent<FirstPersonController>();
            return controller != null ? controller.transform : transform.root;
        }

        static bool IsPartOfPlayer(Transform candidate, Transform root)
        {
            if (candidate == null || root == null)
                return false;

            return candidate == root || candidate.IsChildOf(root) || root.IsChildOf(candidate);
        }

        void SetCurrent(IInteractable target)
        {
            if (target == null && _current == null)
            {
                _hasTarget = false;
                return;
            }

            _current = target;
            _hasTarget = target != null;
        }
    }
}
