namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Persistent norepinephrine infusion. Dose is titrated up/down by the
    /// clinician (debug UI) in discrete steps; PatientState.RecalculateVitals
    /// converts the current dose into a vascular tone contribution every tick
    /// via an Emax dose-response curve (ClinicalFormulas.NorepinephrineEffect).
    /// </summary>
    public class NorepinephrineInfusionEffect : TreatmentEffect
    {
        public override string Name => "Noradrenalina";

        public double Dose { get; private set; }

        public void Titrate(double delta)
        {
            Dose = ClinicalFormulas.Clamp(Dose + delta, 0, 1.5);
        }

        public override void Apply(PatientState patient, double deltaHours, double simulationTimeMinutes)
        {
            patient.NorepinephrineDose = Dose;
        }
    }
}
