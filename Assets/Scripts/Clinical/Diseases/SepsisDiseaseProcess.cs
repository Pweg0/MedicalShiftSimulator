namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Models sepsis progressing to septic shock if untreated: infection burden
    /// grows logistically over time and drives capillary leak (progressive loss
    /// of effective circulating volume). See Docs/ClinicalModel.md for the
    /// growth/leak formulas and the rationale behind the constants used.
    /// </summary>
    public class SepsisDiseaseProcess : DiseaseProcess
    {
        public override string Name => "Sepse";

        public override void Step(PatientState patient, double deltaHours, double simulationTimeMinutes)
        {
            double growth = ClinicalFormulas.InfectionGrowth(patient.InfectionBurden, deltaHours);
            patient.ApplyInfectionDelta(growth);

            patient.IntravascularVolume = ClinicalFormulas.StepVolume(patient.IntravascularVolume, patient.InfectionBurden, deltaHours);
        }
    }
}
