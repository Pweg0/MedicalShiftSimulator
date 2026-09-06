using UnityEngine;
using MedicalShiftSimulator.Clinical;

namespace MedicalShiftSimulator.ClinicalBridge
{
    /// <summary>
    /// Thin Unity adapter that owns a PatientSimulationController and advances
    /// it once per frame. Contains NO clinical logic itself - it only converts
    /// Time.deltaTime into simulated minutes and forwards intervention calls
    /// from the presentation layer (debug UI today, a real medical UI later).
    /// </summary>
    public class PatientSimulationBehaviour : MonoBehaviour
    {
        [Tooltip("How many simulated minutes pass per real-time second.")]
        [SerializeField] private double simulatedMinutesPerSecond = 2.0;
        [SerializeField] private bool paused;
        
        [SerializeField] private string patientComplaint = "Mal-estar intenso e falta de ar";
        [SerializeField] private string patientHistory = "Início recente de febre, prostração e piora progressiva do estado geral.";
[SerializeField] private string patientName = "Paciente da Sala Vermelha";
        [SerializeField] private double patientWeightKg = 70.0;

        public PatientSimulationController Controller { get; private set; }

private void Awake()
        {
            var patient = new PatientState(patientName, patientWeightKg, patientComplaint, patientHistory);
            Controller = new PatientSimulationController(patient, new SepsisDiseaseProcess());
        }

        private void Update()
        {
            if (paused || Controller == null) return;
            Controller.Tick(Time.deltaTime * simulatedMinutesPerSecond);
        }

        public void SetPaused(bool value) => paused = value;
        public bool IsPaused => paused;
        public void SetTimeScale(double minutesPerSecond) => simulatedMinutesPerSecond = minutesPerSecond;
        public double TimeScale => simulatedMinutesPerSecond;
    }
}
