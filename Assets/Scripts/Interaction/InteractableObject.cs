using UnityEngine;
using UnityEngine.Events;

namespace MedicalShiftSimulator.Interaction
{
    public class InteractableObject : MonoBehaviour, IInteractable
    {
        [SerializeField] private string interactionPrompt = "Interagir";
        [SerializeField] private UnityEvent<GameObject> onInteract;

        public string InteractionPrompt => interactionPrompt;

        public void Interact(GameObject interactor)
        {
            Debug.Log($"{interactor.name} interagiu com {gameObject.name}.");
            onInteract?.Invoke(interactor);
        }
    }
}
