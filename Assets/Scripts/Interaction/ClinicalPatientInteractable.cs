using UnityEngine;
using MedicalShiftSimulator.Interaction;

namespace MedicalShiftSimulator.ClinicalBridge
{
    [RequireComponent(typeof(PatientSimulationBehaviour))]
    [RequireComponent(typeof(ClinicalGameplayUI))]
    public class ClinicalPatientInteractable : MonoBehaviour, IInteractable
    {
        public string InteractionPrompt => "E - Avaliar paciente";

        public void Interact(GameObject interactor)
        {
            GetComponent<ClinicalGameplayUI>().OpenPatient();
        }
    }
}
