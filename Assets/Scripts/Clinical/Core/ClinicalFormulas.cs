using System;

namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Pure, stateless physiology formulas used by PatientState to turn internal
    /// drivers (infection burden, vascular tone, volume status, drug doses...)
    /// into observable vital signs. Kept static and side-effect free so every
    /// formula can be unit tested in isolation.
    ///
    /// See Docs/ClinicalModel.md (project root) for the medical rationale,
    /// units and simplifications behind every constant defined here. This is a
    /// deliberately simplified gameplay model, not a validated medical device -
    /// it aims for plausible DIRECTION and INTERACTION of variables, not
    /// diagnostic accuracy.
    ///
    /// Revision history: see Docs/ClinicalModel.md "Log de calibração". This
    /// file currently reflects Revision 4 (fixes for validation findings
    /// P1-P5: physiological pulse pressure instead of a raw DBP clamp, an
    /// explicit flow/perfusion variable decoupled from raw MAP, a real
    /// mcg/kg/min mapping for norepinephrine dosing, a saturating respiratory
    /// response instead of a hard ceiling, and time-lagged consciousness).
    /// </summary>
    public static class ClinicalFormulas
    {
        // ---- Baselines (healthy adult, resting) ----
        public const double BaselineMAP = 93.0;
        public const double BaselineHR = 75.0;
        public const double BaselineRR = 16.0;
        public const double BaselineSpO2 = 98.0;
        public const double BaselineTemp = 37.0;
        public const double BaselineUrineOutput = 70.0;
        public const double BaselinePulsePressure = 40.0;
        public const double BaselineLactate = 1.0;

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // ================= Vascular tone (SVR, relative to normal = 1.0) =================

        public const double VasodilationCoefficient = 0.80;
        public const double SvrTimeConstantHours = 0.1; // ~6 min pharmacodynamic lag
        public const double SvrFloor = 0.15;             // numerical safety rail (never fully reaches 0)
        public const double SvrCeiling = 2.5;            // numerical safety rail, far above any value formulas below can actually produce

        // ---- Norepinephrine dose-response -----------------------------------
        // REVISION 4: Dose is now defined to be in real mcg/kg/min (matches the
        // units clinicians actually titrate in). ED50/Hill were recalibrated
        // for this unit (previously they were tuned in an unlabeled "internal
        // unit" that made a real low starting dose of 0.05 mcg/kg/min produce
        // an almost-zero effect - validation finding P2). Emax itself did NOT
        // change: it represents the maximum achievable pharmacological effect
        // on SVR, a property of the drug/receptor system, not of the units
        // used to express dose, so the Revision 2 calibration for it still
        // applies.
        public const double NorepinephrineEmax = 0.6;
        // Chosen so a clinically-typical "low" dose (0.05 mcg/kg/min) sits at
        // roughly half of ED50 (visible, sub-maximal effect) and a "high"
        // dose (1.0 mcg/kg/min) is close to saturating - see
        // Docs/ClinicalModel.md for the exact response table. This is a
        // plausible order-of-magnitude pick based on typical clinical dosing
        // ranges, NOT derived from a specific pharmacological dose-response
        // study - still an arbitrary parameter pending medical review.
        public const double NorepinephrineED50 = 0.1; // mcg/kg/min
        public const double NorepinephrineHillCoefficient = 1.4;
        // Catecholamine resistance: the more vasoplegic the patient already is
        // (driven by infection burden), the LESS effective each dose becomes -
        // this is what makes severe/refractory septic shock respond poorly to
        // vasopressors, and is what stops a single dose from "fixing" a healthy
        // patient's pressure disproportionately once combined with the afterload
        // feedback below.
        public const double NorepinephrineResistanceFactor = 0.5;
        public const double NorepinephrineResponsivenessFloor = 0.5;
        // Direct (largely beta-adrenergic) chronotropic effect, independent of
        // the reflex/MAP-driven heart rate term. Grows linearly with dose (no
        // saturation) so escalating doses carry a visible, escalating heart-rate
        // cost even after MAP has been corrected - a proxy for the real risk of
        // tachyarrhythmia from excessive vasopressor dosing. Still expressed
        // per "dose unit", now mcg/kg/min - i.e. +5 bpm per 1.0 mcg/kg/min.
        public const double NorepinephrineChronotropicPerDose = 5.0;

        public static double NorepinephrineResponsiveness(double infectionBurden)
        {
            double severity = Clamp(infectionBurden, 0, 1);
            return Clamp(1.0 - NorepinephrineResistanceFactor * severity, NorepinephrineResponsivenessFloor, 1.0);
        }

        public static double NorepinephrineHill(double doseMcgPerKgPerMin)
        {
            if (doseMcgPerKgPerMin <= 0) return 0;
            double doseN = Math.Pow(doseMcgPerKgPerMin, NorepinephrineHillCoefficient);
            double ed50N = Math.Pow(NorepinephrineED50, NorepinephrineHillCoefficient);
            return doseN / (doseN + ed50N);
        }

        /// <summary>
        /// Norepinephrine's contribution to the SVR target. Depends on BOTH the
        /// dose in mcg/kg/min (sigmoidal, saturating) and the current degree of
        /// vasoplegia (blunted in severe sepsis - catecholamine resistance).
        /// </summary>
        public static double NorepinephrineEffect(double doseMcgPerKgPerMin, double infectionBurden)
        {
            return NorepinephrineEmax * NorepinephrineResponsiveness(infectionBurden) * NorepinephrineHill(doseMcgPerKgPerMin);
        }

        public static double SvrTarget(double infectionBurden, double norepinephrineDose)
        {
            double target = 1.0 - VasodilationCoefficient * infectionBurden + NorepinephrineEffect(norepinephrineDose, infectionBurden);
            return Clamp(target, SvrFloor, SvrCeiling);
        }

        /// <summary>
        /// Exact exponential relaxation toward SvrTarget (closed-form solution of
        /// dSVR/dt = (target - SVR) / tau). Step-size independent - see
        /// Docs/ClinicalModel.md "Estabilidade numérica".
        /// </summary>
        public static double StepSvr(double currentSvr, double infectionBurden, double norepinephrineDose, double deltaHours)
        {
            double target = SvrTarget(infectionBurden, norepinephrineDose);
            double decay = Math.Exp(-deltaHours / SvrTimeConstantHours);
            return target + (currentSvr - target) * decay;
        }

        // ================= Intravascular volume (relative, 1.0 = normovolemic) =================

        public const double CapillaryLeakRatePerHour = 0.15;
        public const double VolumeFloor = 0.15; // numerical safety rail

        /// <summary>Exact exponential decay of volume under capillary leak.</summary>
        public static double StepVolume(double currentVolume, double infectionBurden, double deltaHours)
        {
            double decayRate = CapillaryLeakRatePerHour * Math.Max(0, infectionBurden);
            double next = currentVolume * Math.Exp(-decayRate * deltaHours);
            return Math.Max(VolumeFloor, next);
        }

        /// <summary>Relative volume gained per mL of crystalloid infused, scaled to patient weight.</summary>
        public static double CrystalloidVolumeEffect(double volumeMl, double weightKg)
        {
            double effectiveWeight = Math.Max(weightKg, 1.0);
            return (volumeMl / 1000.0) * 0.10 * (70.0 / effectiveWeight);
        }

        // ================= Hemodynamics =================

        public const double VolumeHypovolemiaFloor = 0.15;
        public const double VolumeBonusSaturation = 0.15;
        public const double VolumeBonusSharpness = 2.0;

        public static double VolumeFactor(double volume)
        {
            double v = Math.Max(0, volume);
            if (v <= 1.0)
            {
                return VolumeHypovolemiaFloor + (1.0 - VolumeHypovolemiaFloor) * v;
            }
            return 1.0 + VolumeBonusSaturation * (1.0 - Math.Exp(-(v - 1.0) * VolumeBonusSharpness));
        }

        // Afterload/cardiac-output coupling - see Revision 2 notes. This is the
        // mechanism that both (a) stops norepinephrine from driving MAP
        // arbitrarily high, and (b), as of Revision 4, doubles as the "flow"
        // half of the explicit perfusion variable below.
        public const double AfterloadSensitivity = 0.6;
        public const double LowAfterloadCardiacOutputBoost = 0.15;

        public static double AfterloadFactor(double svr)
        {
            if (svr > 1.0)
            {
                return 1.0 / (1.0 + AfterloadSensitivity * (svr - 1.0));
            }
            return 1.0 + LowAfterloadCardiacOutputBoost * (1.0 - svr);
        }

        public static double Map(double svr, double volume)
        {
            double raw = BaselineMAP * svr * VolumeFactor(volume) * AfterloadFactor(svr);
            // Wide numerical safety rail only - see class remarks. Formulas above
            // should never realistically approach either bound.
            return Clamp(raw, 5, 180);
        }

        // ---- Pulse pressure / blood pressure --------------------------------
        // REVISION 4 (fixes P1): pulse pressure used to be an absolute quantity
        // that depended only on SVR, completely independent of MAP. In terminal
        // shock (MAP collapsing toward the single digits while SVR is also very
        // low, which widens pulse pressure), PP/3 could exceed MAP itself and
        // DBP = MAP - PP/3 went negative - a physically meaningless value that
        // was previously masked by nothing (no clamp existed on DBP at all).
        //
        // The fix models pulse pressure as a FRACTION of MAP (modulated by
        // vascular tone) instead of an independent absolute quantity - this is
        // the physiologically correct framing (pulse pressure is bounded by,
        // and scales with, the underlying perfusion pressure and arterial
        // compliance/tone), not a raw clamp on the output. Because the fraction
        // is clamped well below 3.0, DBP = MAP*(1 - fraction/3) is
        // mathematically guaranteed to stay >= 0 for any MAP >= 0, without ever
        // clamping DBP directly.
        public static readonly double PulsePressureBaselineFraction = BaselinePulsePressure / BaselineMAP; // ~0.43
        public const double PulsePressureFractionMin = 0.15; // severe vasoconstriction: narrow pulse pressure
        public const double PulsePressureFractionMax = 0.85; // severe vasoplegia: wide pulse pressure, but still << 3.0

        public static double PulsePressureFraction(double svr)
        {
            double fraction = PulsePressureBaselineFraction * (2.0 - svr);
            return Clamp(fraction, PulsePressureFractionMin, PulsePressureFractionMax);
        }

        public static double PulsePressure(double map, double svr)
        {
            return Math.Max(0, map) * PulsePressureFraction(svr);
        }

        public static (double systolic, double diastolic) BloodPressure(double map, double svr)
        {
            double pp = PulsePressure(map, svr);
            double dbp = map - pp / 3.0;
            double sbp = dbp + pp;
            return (sbp, dbp);
        }

        // ---- Explicit flow / effective perfusion (REVISION 4, fixes P3) ----
        // Real tissue oxygen delivery depends on FLOW (cardiac output reaching
        // the tissue bed), not on pressure alone - a patient can have a
        // "normal" MAP achieved mostly through vasoconstriction on an
        // inadequate circulating volume, which is not the same, physiologically,
        // as the same MAP achieved with adequate volume and near-normal tone.
        // Previously lactate/urine/consciousness were derived purely from
        // MAP, so the model could not tell these two situations apart
        // (validation finding P3).
        //
        // EffectiveFlow reuses the SAME VolumeFactor/AfterloadFactor terms
        // already driving MAP (this is the CO-analogue half of
        // MAP = baseline * SVR * flow), now exposed as a first-class quantity.
        // Below a MAP autoregulation threshold, organ perfusion becomes
        // pressure-dependent even if flow is nominally adequate (a real
        // autoregulation concept: below a critical closing pressure, capillary
        // beds under-perfuse regardless of upstream flow) - modeled as a
        // simple gate multiplying the flow term.
        public static double EffectiveFlow(double volume, double svr)
        {
            return VolumeFactor(volume) * AfterloadFactor(svr);
        }

        public const double AutoregulationThresholdMAP = 65.0;

        public static double PressureAutoregulationGate(double map)
        {
            return Clamp(map / AutoregulationThresholdMAP, 0, 1.0);
        }

        /// <summary>
        /// The explicit "effective perfusion" variable driving lactate, urine
        /// output and consciousness (Revision 4). 1.0 = normal tissue
        /// perfusion. Deliberately DISTINCT from MAP: two patients can share
        /// the same MAP with different EffectivePerfusion if their
        /// volume/SVR mix differs (see Docs/ClinicalModel.md, test
        /// SamePressureDifferentPerfusion_*).
        /// </summary>
        public static double EffectivePerfusion(double map, double volume, double svr)
        {
            double flow = EffectiveFlow(volume, svr);
            double gate = PressureAutoregulationGate(map);
            return Clamp(flow * gate, 0, 1.3);
        }

        public static double HeartRate(double map, double temperature, double norepinephrineDose = 0)
        {
            double hrFromMap;
            if (map < 40)
            {
                // Decompensation: baroreceptor reflex fails and HR starts trending
                // back down (pre-arrest), instead of climbing forever.
                hrFromMap = 1.8 * (BaselineMAP - 40) - 1.2 * (40 - map);
            }
            else
            {
                hrFromMap = 1.8 * Math.Max(0, BaselineMAP - map);
            }
            double hrFromFever = 8.0 * Math.Max(0, temperature - 37.0);
            double hrFromDrug = NorepinephrineChronotropicPerDose * Math.Max(0, norepinephrineDose);
            return Clamp(BaselineHR + hrFromMap + hrFromFever + hrFromDrug, 40, 200);
        }

        // ---- Respiratory rate (REVISION 4, fixes P4) ------------------------
        // Previously a linear sum of drive terms clamped hard at 50 - the
        // validation battery showed this ceiling being hit routinely (not as a
        // rare edge case) in every scenario that reached severe shock, i.e. the
        // clamp was doing the work a real saturating dynamic should do (the
        // same class of problem MAP had before Revision 2).
        //
        // Fix: the same unbounded "drive" (hypoxia + lactate + fever) is now
        // passed through a smooth saturating curve (1 - exp(-drive/scale))
        // instead of being clamped. This asymptotically approaches the same
        // ceiling value used before (baseline + 34 = 50) but never reaches it
        // exactly and never has a discontinuous "wall" - marginal RR gain per
        // unit of additional drive shrinks continuously, the same physiological
        // pattern (a saturating dynamic instead of a hard limit) used for MAP.
        public const double RespiratoryDriveScale = 17.0;
        public const double RespiratoryMaxAdditionalRate = 34.0;

        public static double RespiratoryDrive(double spo2, double lactate, double temperature)
        {
            double fromHypoxia = 0.6 * Math.Max(0, 95 - spo2);
            double fromLactate = 3.0 * Math.Max(0, lactate - 1.0);
            double fromFever = 2.0 * Math.Max(0, temperature - 37.0);
            return fromHypoxia + fromLactate + fromFever;
        }

        public static double RespiratoryRate(double spo2, double lactate, double temperature)
        {
            double drive = RespiratoryDrive(spo2, lactate, temperature);
            double saturatedAddition = RespiratoryMaxAdditionalRate * (1.0 - Math.Exp(-drive / RespiratoryDriveScale));
            // Wide numerical safety rail only (60) - the real limit is the
            // asymptote above (~50), approached smoothly, never clamped to.
            return Clamp(BaselineRR + saturatedAddition, 8, 60);
        }

        // ================= Oxygenation =================

        public const double LungInjuryCoefficient = 55.0;
        public const double OxygenShuntResistance = 0.5;
        public const double SpO2Floor = 35.0;

        public static double OxygenBenefit(int supportLevel)
        {
            switch (supportLevel)
            {
                case 1: return 6.0;
                case 2: return 14.0;
                default: return 0.0;
            }
        }

        public static double SpO2(double infectionBurden, int oxygenSupportLevel)
        {
            double shuntPenalty = Clamp(infectionBurden, 0, 1) * OxygenShuntResistance;
            double benefit = OxygenBenefit(oxygenSupportLevel) * (1.0 - shuntPenalty);
            double value = BaselineSpO2 - LungInjuryCoefficient * infectionBurden + benefit;
            return Clamp(value, SpO2Floor, 100);
        }

        // ================= Temperature =================

        public const double FeverAmplitude = 2.5;
        public const double TempTimeConstantHours = 1.0;
        public const double ColdShockSvrThreshold = 0.4;

        public static double TempTarget(double infectionBurden, double svr)
        {
            double target = BaselineTemp + FeverAmplitude * infectionBurden;
            if (svr < ColdShockSvrThreshold)
            {
                double coldness = (ColdShockSvrThreshold - svr) * 5.0;
                target -= coldness; // severe vasoplegic shock trends toward hypothermia
            }
            return target;
        }

        /// <summary>Exact exponential relaxation toward TempTarget.</summary>
        public static double StepTemperature(double currentTemp, double infectionBurden, double svr, double deltaHours)
        {
            double target = TempTarget(infectionBurden, svr);
            double decay = Math.Exp(-deltaHours / TempTimeConstantHours);
            double next = target + (currentTemp - target) * decay;
            return Clamp(next, 32, 41);
        }

        // ================= Renal perfusion / urine output =================
        // REVISION 4: now also scaled by EffectiveFlow, not just the MAP-based
        // fraction - same rationale as EffectivePerfusion above (excessive
        // vasoconstriction can reduce renal blood flow even at a "good" MAP).
        // At EffectiveFlow == 1 (normal volume/tone) this reduces exactly to
        // the Revision 2 formula, so the healthy baseline (92 mL/h shown
        // throughout the validation battery) is unchanged.

        public const double RenalPerfusionThresholdMAP = 65.0;
        public const double RenalShutdownMAP = 30.0;

        public static double UrineOutput(double map, double volume, double svr)
        {
            double perfusionFraction = Clamp((map - RenalShutdownMAP) / (RenalPerfusionThresholdMAP - RenalShutdownMAP), 0, 1.2);
            double flow = Clamp(EffectiveFlow(volume, svr), 0, 1.2);
            double output = BaselineUrineOutput * Math.Pow(perfusionFraction, 1.5) * flow;
            return Clamp(output, 0, 150);
        }

        // ================= Lactate (tissue perfusion / anaerobic metabolism) =================
        // REVISION 4: production/clearance now driven by EffectivePerfusion
        // (flow x pressure-gate) instead of a MAP-only perfusion index - see
        // "Explicit flow / effective perfusion" above. Formula shape (linear
        // production above 1-perfusion, first-order clearance) is unchanged.

        public const double LactateProductionRatePerHour = 2.5;
        public const double LactateClearanceRatePerHour = 0.35;

        /// <summary>
        /// Exact solution of dL/dt = production - (clearanceRate*perfusion)*L
        /// over the tick (production/perfusion held constant across the tick).
        /// </summary>
        public static double StepLactate(double currentLactate, double map, double volume, double svr, double deltaHours)
        {
            double perfusion = EffectivePerfusion(map, volume, svr);
            double production = LactateProductionRatePerHour * Math.Max(0, 1 - perfusion);
            double lambda = LactateClearanceRatePerHour * perfusion;

            double next;
            if (lambda > 1e-9)
            {
                double steadyState = production / lambda;
                next = steadyState + (currentLactate - steadyState) * Math.Exp(-lambda * deltaHours);
            }
            else
            {
                next = currentLactate + production * deltaHours;
            }
            return Clamp(next, 0.5, 20);
        }

        // ================= Consciousness (REVISION 4, fixes P5) =============
        // Previously recomputed from scratch every tick from raw (map, spo2)
        // with hard thresholds - the level could flip instantly between any
        // two states in a single tick. Now modeled as a continuous "mentation
        // score" (0..1) that relaxes exponentially toward a target derived
        // from perfusion and oxygenation (same closed-form relaxation pattern
        // used for SVR/temperature), and the DISCRETE level is only a
        // thresholded read-out of that lagging score - so the displayed level
        // changes only once the underlying score has drifted across a
        // threshold, never instantaneously with the raw inputs.
        //
        // The time constant (30 min) is an assumed pacing choice for
        // gameplay legibility, not a value taken from a specific clinical
        // encephalopathy-resolution study - flagged as arbitrary.
        public const double ConsciousnessTimeConstantHours = 0.5;
        public const double ConsciousnessSpo2Floor = 70.0;
        public const double ConsciousnessSpo2Ceiling = 98.0;
        public const double ConsciousnessAlertThreshold = 0.75;
        public const double ConsciousnessConfusedThreshold = 0.50;
        public const double ConsciousnessDrowsyThreshold = 0.25;

        /// <summary>
        /// Instantaneous "target" mentation (0..1) implied by the current
        /// perfusion and oxygenation - the score chases this over time, it is
        /// never assigned directly to the displayed level.
        /// </summary>
        public static double ConsciousnessScoreTarget(double effectivePerfusion, double spo2)
        {
            double perfusionComponent = Clamp(effectivePerfusion, 0, 1);
            double oxygenComponent = Clamp((spo2 - ConsciousnessSpo2Floor) / (ConsciousnessSpo2Ceiling - ConsciousnessSpo2Floor), 0, 1);
            return Math.Min(perfusionComponent, oxygenComponent);
        }

        /// <summary>Exact exponential relaxation of the mentation score toward its target.</summary>
        public static double StepConsciousnessScore(double currentScore, double targetScore, double deltaHours)
        {
            double decay = Math.Exp(-deltaHours / ConsciousnessTimeConstantHours);
            double next = targetScore + (currentScore - targetScore) * decay;
            return Clamp(next, 0, 1);
        }

        public static ConsciousnessLevel ConsciousnessLevelFromScore(double score)
        {
            if (score >= ConsciousnessAlertThreshold) return ConsciousnessLevel.Alert;
            if (score >= ConsciousnessConfusedThreshold) return ConsciousnessLevel.Confused;
            if (score >= ConsciousnessDrowsyThreshold) return ConsciousnessLevel.Drowsy;
            return ConsciousnessLevel.Unresponsive;
        }

        // ================= Infection (sepsis) =================

        public const double InfectionGrowthRatePerHour = 0.20;
        public const double InfectionCarryingCapacity = 1.0;

        /// <summary>Logistic growth delta for this tick (always >= 0).</summary>
        public static double InfectionGrowth(double infectionBurden, double deltaHours)
        {
            double growth = InfectionGrowthRatePerHour * infectionBurden * (1 - infectionBurden / InfectionCarryingCapacity);
            return Math.Max(0, growth) * deltaHours;
        }

        // ================= Antibiotic pharmacodynamics =================

        public const double AntibioticOnsetTauHours = 0.6;
        public const double AntibioticCoverageHours = 6.0;
        public const double AntibioticWaneTauHours = 1.2;
        public const double AntibioticMaxKillRatePerHour = 0.55;

        public static double AntibioticEffectiveLevel(double hoursSinceLastDose)
        {
            if (hoursSinceLastDose < 0) return 0;

            if (hoursSinceLastDose <= AntibioticCoverageHours)
            {
                return 1.0 - Math.Exp(-hoursSinceLastDose / AntibioticOnsetTauHours);
            }

            double levelAtCoverageEnd = 1.0 - Math.Exp(-AntibioticCoverageHours / AntibioticOnsetTauHours);
            double timeSinceCoverageEnd = hoursSinceLastDose - AntibioticCoverageHours;
            return levelAtCoverageEnd * Math.Exp(-timeSinceCoverageEnd / AntibioticWaneTauHours);
        }

        /// <summary>Infection burden killed this tick (always >= 0).</summary>
        public static double InfectionKill(double infectionBurden, double effectiveLevel, double deltaHours)
        {
            return AntibioticMaxKillRatePerHour * effectiveLevel * infectionBurden * deltaHours;
        }
    }
}
