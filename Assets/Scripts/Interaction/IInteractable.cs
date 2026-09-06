using UnityEngine;

namespace MedicalShiftSimulator.Interaction
{
    public interface IInteractable
    {
        string InteractionPrompt { get; }
        void Interact(GameObject interactor);
    }
}
