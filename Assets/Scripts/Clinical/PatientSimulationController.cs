using System.Collections.Generic;

namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Owns a single patient's full simulation: clock, disease processes,
    /// active treatment effects and the event bus. Call Tick(deltaMinutes)
    /// once per update from whatever host loop drives simulated time (a Unity
    /// MonoBehaviour, an EditMode test, a future headless batch run...).
    ///
    /// Tick order matters and is deliberate:
    ///   1) Treatments apply their inputs (fluids infuse, drug levels update).
    ///   2) The disease process progresses using those updated inputs.
    ///   3) Vitals are recalculated from the resulting drivers.
    ///   4) Events are published and thresholds/death are checked.
    /// </summary>
    public class PatientSimulationController
    {
        public PatientState Patient { get; }
        public ClinicalClock Clock { get; }
        public ClinicalEventBus Events { get; }

        private readonly List<DiseaseProcess> _diseases = new List<DiseaseProcess>();
        private readonly List<TreatmentEffect> _transientEffects = new List<TreatmentEffect>();

        public NorepinephrineInfusionEffect Norepinephrine { get; }
        public OxygenTherapyEffect Oxygen { get; }
        public AntibioticEffect Antibiotic { get; }

        private bool _shockAnnounced;

        public PatientSimulationController(PatientState patient, DiseaseProcess initialDisease)
        {
            Patient = patient;
            Clock = new ClinicalClock();
            Events = new ClinicalEventBus();

            _diseases.Add(initialDisease);

            Norepinephrine = new NorepinephrineInfusionEffect();
            Oxygen = new OxygenTherapyEffect();
            Antibiotic = new AntibioticEffect();
        }

        public void GiveCrystalloidBolus(double volumeMl, double durationMinutes = 20)
        {
            _transientEffects.Add(new CrystalloidBolusEffect(volumeMl, durationMinutes));
            Events.Publish(new InterventionAppliedEvent(Clock.TotalSimulatedMinutes, "Cristaloide",
                $"{volumeMl:F0} mL em {durationMinutes:F0} min"));
        }

        public void TitrateNorepinephrine(double delta)
        {
            Norepinephrine.Titrate(delta);
            Events.Publish(new InterventionAppliedEvent(Clock.TotalSimulatedMinutes, "Noradrenalina",
                $"dose = {Norepinephrine.Dose:F2}"));
        }

        public void SetOxygenLevel(int level)
        {
            Oxygen.SetLevel(level);
            Events.Publish(new InterventionAppliedEvent(Clock.TotalSimulatedMinutes, "Oxigênio",
                $"nível = {Oxygen.Level}"));
        }

        public void GiveAntibiotic()
        {
            Antibiotic.GiveDose();
            Events.Publish(new InterventionAppliedEvent(Clock.TotalSimulatedMinutes, "Antibiótico", "dose administrada"));
        }

        public void Tick(double deltaMinutes)
        {
            if (deltaMinutes <= 0 || !Patient.IsAlive) return;

            Clock.Advance(deltaMinutes);
            double deltaHours = deltaMinutes / 60.0;
            double nowMinutes = Clock.TotalSimulatedMinutes;

            Norepinephrine.Apply(Patient, deltaHours, nowMinutes);
            Oxygen.Apply(Patient, deltaHours, nowMinutes);
            Antibiotic.Apply(Patient, deltaHours, nowMinutes);

            for (int i = 0; i < _transientEffects.Count; i++)
            {
                _transientEffects[i].Apply(Patient, deltaHours, nowMinutes);
            }
            _transientEffects.RemoveAll(e => !e.IsActive);

            for (int i = 0; i < _diseases.Count; i++)
            {
                _diseases[i].Step(Patient, deltaHours, nowMinutes);
            }

            bool wasAlive = Patient.IsAlive;
            Patient.RecalculateVitals(deltaHours);

            Events.Publish(new VitalsUpdatedEvent(nowMinutes, Patient.Vitals));
            CheckThresholds(nowMinutes);

            if (wasAlive && !Patient.IsAlive)
            {
                Events.Publish(new PatientDiedEvent(nowMinutes, Patient.CauseOfDeath));
            }
        }

        private void CheckThresholds(double nowMinutes)
        {
            bool inShock = Patient.Vitals.MeanArterialPressure < 65 && Patient.Vitals.Lactate > 2.0;
            if (inShock && !_shockAnnounced)
            {
                _shockAnnounced = true;
                Events.Publish(new ThresholdCrossedEvent(nowMinutes, "ChoqueSeptico", true));
            }
            else if (!inShock && _shockAnnounced &&
                     Patient.Vitals.MeanArterialPressure >= 65 && Patient.Vitals.Lactate < 2.0)
            {
                _shockAnnounced = false;
                Events.Publish(new ThresholdCrossedEvent(nowMinutes, "ChoqueSeptico", false));
            }
        }
    }
}
