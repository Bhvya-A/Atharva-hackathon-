using UnityEngine;

namespace TheOtherSide
{
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] float interactDistance = 2.5f;
        [SerializeField] LayerMask interactMask = ~0;

        IInteractable _current;

        public IInteractable Current => _current;

        public void Configure(Camera cam)
        {
            viewCamera = cam;
        }

        void Update()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsGameplayActive)
            {
                SetCurrent(null);
                return;
            }

            if (viewCamera == null)
                viewCamera = Camera.main;

            _current = FindTarget();
            HUDController.Instance?.SetPrompt(_current != null && _current.CanInteract ? _current.Prompt : string.Empty);

            if (_current != null && _current.CanInteract && GameInput.InteractPressed)
                _current.Interact();
        }

        IInteractable FindTarget()
        {
            if (viewCamera == null)
                return null;

            Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Collide))
                return null;

            return hit.collider.GetComponentInParent<IInteractable>();
        }

        void SetCurrent(IInteractable target)
        {
            _current = target;
            HUDController.Instance?.SetPrompt(string.Empty);
        }
    }
}
