namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// A treatment effect modifies a patient's internal physiological drivers
    /// over time (or instantaneously, spread across a short infusion). It never
    /// assigns VitalSigns directly - only the underlying drivers (volume,
    /// vascular tone inputs, infection burden...), which
    /// PatientState.RecalculateVitals() then turns into observable vitals.
    /// </summary>
    public abstract class TreatmentEffect
    {
        public abstract string Name { get; }

        /// <summary>True while this effect should keep receiving Apply() calls.</summary>
        public bool IsActive { get; protected set; } = true;

        public abstract void Apply(PatientState patient, double deltaHours, double simulationTimeMinutes);
    }
}
