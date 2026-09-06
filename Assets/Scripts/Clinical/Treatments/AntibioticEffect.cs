namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Represents ongoing antibiotic coverage. Each dose resets the onset
    /// timer; the effective level ramps up (onset), plateaus (coverage
    /// window), then wanes if not re-dosed - see
    /// ClinicalFormulas.AntibioticEffectiveLevel. The effective level scales
    /// how fast InfectionBurden is killed off each tick, so it never sets
    /// infection status directly.
    /// </summary>
    public class AntibioticEffect : TreatmentEffect
    {
        public override string Name => "Antibiótico";

        public bool HasBeenGiven { get; private set; }
        public double HoursSinceLastDose { get; private set; }
        public double CurrentEffectiveLevel { get; private set; }

        public void GiveDose()
        {
            HasBeenGiven = true;
            HoursSinceLastDose = 0;
        }

        public override void Apply(PatientState patient, double deltaHours, double simulationTimeMinutes)
        {
            if (!HasBeenGiven)
            {
                CurrentEffectiveLevel = 0;
                return;
            }

            HoursSinceLastDose += deltaHours;
            CurrentEffectiveLevel = ClinicalFormulas.AntibioticEffectiveLevel(HoursSinceLastDose);

            double kill = ClinicalFormulas.InfectionKill(patient.InfectionBurden, CurrentEffectiveLevel, deltaHours);
            patient.ApplyInfectionDelta(-kill);
        }
    }
}
