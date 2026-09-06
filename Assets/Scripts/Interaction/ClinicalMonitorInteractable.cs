using UnityEngine;
using MedicalShiftSimulator.Interaction;

namespace MedicalShiftSimulator.ClinicalBridge
{
    public class ClinicalMonitorInteractable : MonoBehaviour, IInteractable
    {
        public string InteractionPrompt => "E - Visualizar monitor";

        public void Interact(GameObject interactor)
        {
            var patient = FindFirstObjectByType<PatientSimulationBehaviour>();
            if (patient == null) return;

            var ui = patient.GetComponent<ClinicalGameplayUI>();
            if (ui != null) ui.OpenMonitor();
        }
    }
}
