namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Simplified AVPU-like consciousness scale used by this vertical slice.
    /// </summary>
    public enum ConsciousnessLevel
    {
        Alert,
        Confused,
        Drowsy,
        Unresponsive
    }

    /// <summary>
    /// Snapshot of a patient's observable vital signs at one point in simulated
    /// time. Pure data - no behavior, no Unity dependency. Values here are always
    /// DERIVED by ClinicalFormulas/PatientState, never assigned directly by a
    /// treatment or the presentation layer.
    /// </summary>
    public struct VitalSigns
    {
        public double HeartRate;             // bpm
        public double SystolicBP;            // mmHg
        public double DiastolicBP;           // mmHg
        public double MeanArterialPressure;  // mmHg
        public double RespiratoryRate;       // breaths/min
        public double SpO2;                  // %
        public double Temperature;           // degrees Celsius
        public ConsciousnessLevel Consciousness;
        public double UrineOutput;           // mL/hr
        public double Lactate;               // mmol/L

        public static VitalSigns Baseline()
        {
            return new VitalSigns
            {
                HeartRate = ClinicalFormulas.BaselineHR,
                SystolicBP = 120,
                DiastolicBP = 80,
                MeanArterialPressure = ClinicalFormulas.BaselineMAP,
                RespiratoryRate = ClinicalFormulas.BaselineRR,
                SpO2 = ClinicalFormulas.BaselineSpO2,
                Temperature = ClinicalFormulas.BaselineTemp,
                Consciousness = ConsciousnessLevel.Alert,
                UrineOutput = ClinicalFormulas.BaselineUrineOutput,
                Lactate = ClinicalFormulas.BaselineLactate
            };
        }
    }
}
