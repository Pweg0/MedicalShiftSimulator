namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Persistent oxygen support setting (0 = room air, 1 = nasal cannula,
    /// 2 = mask / high-flow). Never finishes on its own - the level is simply
    /// kept on the patient and PatientState.RecalculateVitals reads it every
    /// tick via ClinicalFormulas.OxygenBenefit.
    /// </summary>
    public class OxygenTherapyEffect : TreatmentEffect
    {
        public override string Name => "Oxigenoterapia";

        public int Level { get; private set; }

        public void SetLevel(int level)
        {
            Level = (int)ClinicalFormulas.Clamp(level, 0, 2);
        }

        public override void Apply(PatientState patient, double deltaHours, double simulationTimeMinutes)
        {
            patient.OxygenSupportLevel = Level;
        }
    }
}
