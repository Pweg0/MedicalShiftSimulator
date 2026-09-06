namespace MedicalShiftSimulator.Clinical
{
    /// <summary>Base type for anything published on a ClinicalEventBus.</summary>
    public abstract class ClinicalEvent
    {
        public double SimulationTimeMinutes { get; }

        protected ClinicalEvent(double simulationTimeMinutes)
        {
            SimulationTimeMinutes = simulationTimeMinutes;
        }
    }

    /// <summary>Fired once per controller Tick with the freshly recalculated vitals.</summary>
    public class VitalsUpdatedEvent : ClinicalEvent
    {
        public VitalSigns Vitals { get; }

        public VitalsUpdatedEvent(double simulationTimeMinutes, VitalSigns vitals) : base(simulationTimeMinutes)
        {
            Vitals = vitals;
        }
    }

    /// <summary>Fired whenever a clinician-facing intervention is applied.</summary>
    public class InterventionAppliedEvent : ClinicalEvent
    {
        public string InterventionName { get; }
        public string Details { get; }

        public InterventionAppliedEvent(double simulationTimeMinutes, string interventionName, string details) : base(simulationTimeMinutes)
        {
            InterventionName = interventionName;
            Details = details;
        }
    }

    /// <summary>Fired when a named clinical threshold is entered or exited (e.g. septic shock).</summary>
    public class ThresholdCrossedEvent : ClinicalEvent
    {
        public string ThresholdName { get; }
        public bool Entered { get; }

        public ThresholdCrossedEvent(double simulationTimeMinutes, string thresholdName, bool entered) : base(simulationTimeMinutes)
        {
            ThresholdName = thresholdName;
            Entered = entered;
        }
    }

    /// <summary>Fired once, the first time a patient's physiology becomes non-viable.</summary>
    public class PatientDiedEvent : ClinicalEvent
    {
        public string Cause { get; }

        public PatientDiedEvent(double simulationTimeMinutes, string cause) : base(simulationTimeMinutes)
        {
            Cause = cause;
        }
    }
}
