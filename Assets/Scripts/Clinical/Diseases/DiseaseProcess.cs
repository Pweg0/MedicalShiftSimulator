namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// A disease process evolves a PatientState's internal drivers over time,
    /// independent of any treatment. Multiple processes could in principle run
    /// concurrently on the same patient (comorbidities); this vertical slice
    /// ships exactly one: SepsisDiseaseProcess.
    /// </summary>
    public abstract class DiseaseProcess
    {
        public abstract string Name { get; }

        /// <summary>
        /// Applies deltaHours worth of natural disease progression to the
        /// patient. Implementations should only ever mutate internal drivers
        /// via PatientState's public/internal mutators - never assign
        /// VitalSigns directly.
        /// </summary>
        public abstract void Step(PatientState patient, double deltaHours, double simulationTimeMinutes);
    }
}
