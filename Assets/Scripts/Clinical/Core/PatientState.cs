using System;

namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Full physiological state of a single patient: the internal drivers that
    /// are not directly observable at the bedside (infection burden, vascular
    /// tone, circulating volume, drug levels) plus the derived VitalSigns that
    /// ARE observable. Mutation happens only through explicit methods (or
    /// internal setters used by DiseaseProcess/TreatmentEffect implementations
    /// in this same assembly) so nothing outside the Clinical domain can assign
    /// a vital sign directly.
    /// </summary>
    public class PatientState
    {
        
        public string Complaint { get; internal set; } = "Mal-estar intenso e falta de ar";
        public string HistorySummary { get; internal set; } = "Início recente de febre, prostração e piora progressiva do estado geral.";
public string Name { get; set; } = "Paciente";
        public double WeightKg { get; set; } = 70.0;
        public bool IsAlive { get; private set; } = true;
        public string CauseOfDeath { get; private set; }

        // Internal drivers (not shown directly to the player - see ClinicalDebugUI
        // for how the debug panel surfaces them for validation purposes only).
        public double InfectionBurden { get; internal set; } = 0.05;
        public double IntravascularVolume { get; internal set; } = 1.0;
        public double VascularTone { get; internal set; } = 1.0;
        public double NorepinephrineDose { get; internal set; } = 0.0;
        public int OxygenSupportLevel { get; internal set; } = 0;

        /// <summary>
        /// Explicit flow/effective-perfusion index (Revision 4) - 1.0 = normal
        /// tissue perfusion. Decoupled from MAP: see
        /// ClinicalFormulas.EffectivePerfusion. Drives lactate, urine output
        /// and the consciousness target. Exposed here (not just buried inside
        /// a formula) because the validation findings this fixes explicitly
        /// asked for perfusion to be an explicit, inspectable variable.
        /// </summary>
        public double EffectivePerfusion { get; private set; } = 1.0;

        /// <summary>
        /// Continuous mentation score (0..1) with inertia - see
        /// ClinicalFormulas.StepConsciousnessScore. VitalSigns.Consciousness is
        /// derived from this each tick; it never jumps directly to a target.
        /// </summary>
        public double ConsciousnessScore { get; private set; } = 1.0;

        public VitalSigns Vitals { get; private set; }

        private double _mapBelowLethalMinutes;
        private double _spo2BelowLethalMinutes;
        private const double LethalMAP = 15.0;
        private const double LethalSustainMinutes = 2.0;
        private const double LethalSpO2 = 45.0;
        private const double LethalSpO2SustainMinutes = 5.0;

        public PatientState()
        {
            Vitals = VitalSigns.Baseline();
        }

public PatientState(string name, double weightKg, string complaint, string historySummary)
        {
            Name = name;
            WeightKg = weightKg;
            Complaint = complaint;
            HistorySummary = historySummary;
        }


        /// <summary>Adds a (positive) volume delta, e.g. from a crystalloid bolus.</summary>
        public void InfuseVolume(double deltaVolume)
        {
            IntravascularVolume = Math.Max(ClinicalFormulas.VolumeFloor, IntravascularVolume + deltaVolume);
        }

        /// <summary>Adds a signed delta to infection burden (positive = growth, negative = antibiotic kill).</summary>
        public void ApplyInfectionDelta(double delta)
        {
            InfectionBurden = ClinicalFormulas.Clamp(InfectionBurden + delta, 0, 1.5);
        }

        /// <summary>
        /// Advances derived vital signs by deltaHours of simulated time, based on
        /// whatever the current internal drivers are. Call this AFTER disease
        /// processes and treatment effects have applied their changes for the tick.
        /// </summary>
        public void RecalculateVitals(double deltaHours)
        {
            if (!IsAlive) return;

            VascularTone = ClinicalFormulas.StepSvr(VascularTone, InfectionBurden, NorepinephrineDose, deltaHours);

            double map = ClinicalFormulas.Map(VascularTone, IntravascularVolume);
            var bp = ClinicalFormulas.BloodPressure(map, VascularTone);
            double temp = ClinicalFormulas.StepTemperature(Vitals.Temperature, InfectionBurden, VascularTone, deltaHours);
            double spo2 = ClinicalFormulas.SpO2(InfectionBurden, OxygenSupportLevel);
            EffectivePerfusion = ClinicalFormulas.EffectivePerfusion(map, IntravascularVolume, VascularTone);
            double lactate = ClinicalFormulas.StepLactate(Vitals.Lactate, map, IntravascularVolume, VascularTone, deltaHours);
            double rr = ClinicalFormulas.RespiratoryRate(spo2, lactate, temp);
            double hr = ClinicalFormulas.HeartRate(map, temp, NorepinephrineDose);
            double urine = ClinicalFormulas.UrineOutput(map, IntravascularVolume, VascularTone);

            double consciousnessTarget = ClinicalFormulas.ConsciousnessScoreTarget(EffectivePerfusion, spo2);
            ConsciousnessScore = ClinicalFormulas.StepConsciousnessScore(ConsciousnessScore, consciousnessTarget, deltaHours);
            var consciousness = ClinicalFormulas.ConsciousnessLevelFromScore(ConsciousnessScore);

            Vitals = new VitalSigns
            {
                HeartRate = hr,
                SystolicBP = bp.systolic,
                DiastolicBP = bp.diastolic,
                MeanArterialPressure = map,
                RespiratoryRate = rr,
                SpO2 = spo2,
                Temperature = temp,
                Consciousness = consciousness,
                UrineOutput = urine,
                Lactate = lactate
            };

            _mapBelowLethalMinutes = map < LethalMAP ? _mapBelowLethalMinutes + deltaHours * 60.0 : 0;
            _spo2BelowLethalMinutes = spo2 < LethalSpO2 ? _spo2BelowLethalMinutes + deltaHours * 60.0 : 0;

            if (_mapBelowLethalMinutes >= LethalSustainMinutes)
            {
                IsAlive = false;
                CauseOfDeath = "Choque refratário / colapso cardiovascular (PAM criticamente baixa sustentada)";
            }
            else if (_spo2BelowLethalMinutes >= LethalSpO2SustainMinutes)
            {
                IsAlive = false;
                CauseOfDeath = "Insuficiência respiratória refratária (hipóxia crítica sustentada)";
            }
        }
    }
}
