
using UnityEngine.InputSystem;
using UnityEngine;

namespace MedicalShiftSimulator.Interaction
{
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private float interactionRadius = 1.5f;
        [SerializeField] private LayerMask interactableMask = ~0;
        

        public IInteractable CurrentInteractable => _current;
        public string CurrentPrompt => _current != null ? _current.InteractionPrompt : string.Empty;
private IInteractable _current;

private void Update()
        {
            _current = FindNearestInteractable();

            if (_current != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                _current.Interact(gameObject);
            }
        }

private void OnGUI()
        {
            if (_current == null || string.IsNullOrEmpty(_current.InteractionPrompt)) return;
            GUI.Box(new Rect(Screen.width * 0.5f - 130, Screen.height - 90, 260, 38), _current.InteractionPrompt);
        }


        private IInteractable FindNearestInteractable()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, interactionRadius, interactableMask);
            IInteractable nearest = null;
            float nearestSqrDist = float.MaxValue;

            foreach (Collider hit in hits)
            {
                if (!hit.TryGetComponent(out IInteractable interactable))
                {
                    continue;
                }

                float sqrDist = (hit.transform.position - transform.position).sqrMagnitude;
                if (sqrDist < nearestSqrDist)
                {
                    nearestSqrDist = sqrDist;
                    nearest = interactable;
                }
            }

            return nearest;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactionRadius);
        }
    }
}
