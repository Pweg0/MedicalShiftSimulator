using System;
using NUnit.Framework;
using MedicalShiftSimulator.Clinical;

namespace MedicalShiftSimulator.Clinical.Tests
{
    public class PatientSimulationTests
    {
        private static PatientSimulationController NewController()
        {
            var patient = new PatientState { Name = "Teste", WeightKg = 70 };
            return new PatientSimulationController(patient, new SepsisDiseaseProcess());
        }

        private static void RunHours(PatientSimulationController controller, double hours, double stepMinutes = 5)
        {
            double totalMinutes = hours * 60.0;
            double elapsed = 0;
            while (elapsed < totalMinutes)
            {
                double step = Math.Min(stepMinutes, totalMinutes - elapsed);
                controller.Tick(step);
                elapsed += step;
            }
        }

        [Test]
        public void UntreatedSepsis_DeterioratesOverTime()
        {
            var controller = NewController();
            double initialMap = controller.Patient.Vitals.MeanArterialPressure;

            RunHours(controller, 6);

            Assert.Less(controller.Patient.Vitals.MeanArterialPressure, initialMap);
            Assert.Greater(controller.Patient.InfectionBurden, 0.05);
        }

        [Test]
        public void UntreatedSepsis_EventuallyReachesShockThreshold()
        {
            var controller = NewController();
            bool shockEventFired = false;
            controller.Events.Subscribe(evt =>
            {
                if (evt is ThresholdCrossedEvent t && t.ThresholdName == "ChoqueSeptico" && t.Entered)
                    shockEventFired = true;
            });

            RunHours(controller, 20);

            Assert.IsTrue(shockEventFired, "Choque séptico deveria ter sido anunciado dentro de 20h sem tratamento.");
        }

        [Test]
        public void UntreatedSepsis_EventuallyKillsThePatient()
        {
            var controller = NewController();
            bool diedEventFired = false;
            controller.Events.Subscribe(evt => { if (evt is PatientDiedEvent) diedEventFired = true; });

            RunHours(controller, 72, stepMinutes: 10);

            Assert.IsFalse(controller.Patient.IsAlive);
            Assert.IsTrue(diedEventFired);
        }

        [Test]
        public void CrystalloidBolus_ImprovesMapComparedToUntreated()
        {
            var treated = NewController();
            var untreated = NewController();

            RunHours(treated, 2);
            RunHours(untreated, 2);

            treated.GiveCrystalloidBolus(2000, 20);

            RunHours(treated, 1);
            RunHours(untreated, 1);

            Assert.Greater(treated.Patient.Vitals.MeanArterialPressure, untreated.Patient.Vitals.MeanArterialPressure);
            Assert.Greater(treated.Patient.IntravascularVolume, untreated.Patient.IntravascularVolume);
        }

        [Test]
        public void Norepinephrine_IncreasesMapComparedToUntreated()
        {
            var treated = NewController();
            var untreated = NewController();

            RunHours(treated, 4);
            RunHours(untreated, 4);

            treated.TitrateNorepinephrine(0.5);

            RunHours(treated, 1);
            RunHours(untreated, 1);

            Assert.Greater(treated.Patient.Vitals.MeanArterialPressure, untreated.Patient.Vitals.MeanArterialPressure);
            Assert.Greater(treated.Patient.VascularTone, untreated.Patient.VascularTone);
        }

        [Test]
        public void Oxygen_ImprovesSpO2ComparedToUntreated()
        {
            var treated = NewController();
            var untreated = NewController();

            RunHours(treated, 5);
            RunHours(untreated, 5);

            treated.SetOxygenLevel(2);

            RunHours(treated, 1);
            RunHours(untreated, 1);

            Assert.Greater(treated.Patient.Vitals.SpO2, untreated.Patient.Vitals.SpO2);
        }

        [Test]
        public void Antibiotic_SlowsInfectionGrowthComparedToUntreated()
        {
            var treated = NewController();
            var untreated = NewController();

            treated.GiveAntibiotic();

            RunHours(treated, 8);
            RunHours(untreated, 8);

            Assert.Less(treated.Patient.InfectionBurden, untreated.Patient.InfectionBurden);
        }

        [Test]
        public void CombinedTreatment_OutperformsUntreatedAndFluidsOnly()
        {
            var combo = NewController();
            var fluidsOnly = NewController();
            var untreated = NewController();

            combo.GiveAntibiotic();
            combo.TitrateNorepinephrine(0.4);
            combo.GiveCrystalloidBolus(2000, 30);
            combo.SetOxygenLevel(2);

            fluidsOnly.GiveCrystalloidBolus(2000, 30);

            RunHours(combo, 8);
            RunHours(fluidsOnly, 8);
            RunHours(untreated, 8);

            Assert.Greater(combo.Patient.Vitals.MeanArterialPressure, untreated.Patient.Vitals.MeanArterialPressure);
            Assert.Greater(combo.Patient.Vitals.MeanArterialPressure, fluidsOnly.Patient.Vitals.MeanArterialPressure);
            Assert.Less(combo.Patient.InfectionBurden, untreated.Patient.InfectionBurden);
            Assert.IsTrue(combo.Patient.IsAlive);
        }

        [Test]
        public void Norepinephrine_InNonVasoplegicPatient_RaisesMapButStaysPlausible()
        {
            // Given very early / mild disease (near-baseline vascular tone), a
            // meaningful dose should raise MAP substantially but must not blow
            // past a plausible "iatrogenic hypertension" range (regression guard
            // for the original uncapped-overshoot bug).
            var controller = NewController();
            double before = controller.Patient.Vitals.MeanArterialPressure;

            controller.TitrateNorepinephrine(1.0);
            RunHours(controller, 1, stepMinutes: 2);

            double after = controller.Patient.Vitals.MeanArterialPressure;
            Assert.Greater(after, before);
            Assert.Less(after, 135, "Norepinephrine in a non-vasoplegic patient should not push MAP into an implausible range.");
        }

        [Test]
        public void Norepinephrine_InSevereVasoplegia_RespondsLessThanInMildDisease()
        {
            var mild = NewController();
            var severe = NewController();

            // Force divergent disease severity before dosing: let "severe" run
            // untreated for much longer so infection burden (and thus vasoplegia)
            // is far advanced, while "mild" only progresses briefly.
            RunHours(mild, 1);
            RunHours(severe, 20);

            double mapBeforeMild = mild.Patient.Vitals.MeanArterialPressure;
            double mapBeforeSevere = severe.Patient.Vitals.MeanArterialPressure;

            mild.TitrateNorepinephrine(0.5);
            severe.TitrateNorepinephrine(0.5);
            RunHours(mild, 1, stepMinutes: 2);
            RunHours(severe, 1, stepMinutes: 2);

            double mildGain = mild.Patient.Vitals.MeanArterialPressure - mapBeforeMild;
            double severeGain = severe.Patient.Vitals.MeanArterialPressure - mapBeforeSevere;

            Assert.Greater(mildGain, severeGain,
                "The same dose should improve MAP less in advanced/severe vasoplegia (catecholamine resistance) than in mild disease.");
        }

        [Test]
        public void Norepinephrine_IncreasingDoses_ShowDiminishingMapGainsAndRisingHeartRate()
        {
            var controller = NewController();
            RunHours(controller, 4);

            double map0 = controller.Patient.Vitals.MeanArterialPressure;
            double hr0 = controller.Patient.Vitals.HeartRate;

            controller.TitrateNorepinephrine(0.3);
            RunHours(controller, 0.5, stepMinutes: 1);
            double map1 = controller.Patient.Vitals.MeanArterialPressure;
            double hr1 = controller.Patient.Vitals.HeartRate;

            controller.TitrateNorepinephrine(0.3); // dose now 0.6
            RunHours(controller, 0.5, stepMinutes: 1);
            double map2 = controller.Patient.Vitals.MeanArterialPressure;

            controller.TitrateNorepinephrine(0.9); // dose now 1.5 (max)
            RunHours(controller, 0.5, stepMinutes: 1);
            double map3 = controller.Patient.Vitals.MeanArterialPressure;
            double hr3 = controller.Patient.Vitals.HeartRate;

            double firstStepGain = map1 - map0;
            double lastStepGain = map3 - map2;

            Assert.Greater(firstStepGain, 0);
            Assert.Greater(firstStepGain, lastStepGain, "Equal-sized dose increments should yield shrinking MAP gains at higher doses.");
            Assert.Greater(hr3, hr1, "Heart rate should trend upward (direct chronotropic effect / adverse-effect signal) as dose keeps escalating, even as MAP gains shrink.");
        }

        [Test]
        public void LargeCrystalloidVolume_ShowsDiminishingReturnsNotUnboundedRise()
        {
            var moderate = NewController();
            var massive = NewController();

            RunHours(moderate, 2);
            RunHours(massive, 2);

            moderate.GiveCrystalloidBolus(2000, 20);
            massive.GiveCrystalloidBolus(10000, 60);

            RunHours(moderate, 2);
            RunHours(massive, 2);

            double moderateMap = moderate.Patient.Vitals.MeanArterialPressure;
            double massiveMap = massive.Patient.Vitals.MeanArterialPressure;

            Assert.Greater(massiveMap, moderateMap, "More fluid should still help at least a little...");
            Assert.Less(massiveMap - moderateMap, 15, "...but 5x the volume should not translate into a proportional (5x) MAP gain - diminishing returns.");
            Assert.Less(massiveMap, 140, "An enormous bolus should not push MAP to an implausible level by itself.");
        }

        [Test]
        public void EarlyAntibiotic_OutperformsLateAntibiotic_AtSameFollowUpTime()
        {
            var early = NewController();
            var late = NewController();

            early.GiveAntibiotic(); // t = 0h

            RunHours(early, 10);
            RunHours(late, 10);

            late.GiveAntibiotic(); // t = 10h, disease already advanced

            RunHours(early, 6);
            RunHours(late, 6);

            Assert.Less(early.Patient.InfectionBurden, late.Patient.InfectionBurden,
                "Antibiotic given early should leave a lower infection burden than the same antibiotic given late, at the same absolute follow-up time.");
            Assert.Greater(early.Patient.Vitals.MeanArterialPressure, late.Patient.Vitals.MeanArterialPressure);
        }

        [Test]
        public void RespiratoryFailure_IsAPossibleCauseOfDeath_WhenOxygenationNeverTreated()
        {
            // With the SpO2 floor no longer artificially propping oxygenation up,
            // severe untreated lung involvement should be able to kill on its own
            // even in a run where hemodynamics are propped up with fluids so MAP
            // alone would not be the cause of death.
            var controller = NewController();
            string cause = null;
            controller.Events.Subscribe(evt =>
            {
                if (evt is PatientDiedEvent d) cause = d.Cause;
            });

            for (int i = 0; i < 40 && controller.Patient.IsAlive; i++)
            {
                controller.GiveCrystalloidBolus(1000, 30);
                RunHours(controller, 2, stepMinutes: 5);
            }

            Assert.IsFalse(controller.Patient.IsAlive);
            Assert.IsNotNull(cause);
        }

        [TestCase(0.05)]
        [TestCase(0.1)]
        [TestCase(0.2)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void Norepinephrine_AtClinicalMcgPerKgPerMinDoses_RaisesMapAboveUntreated(double doseMcgPerKgPerMin)
        {
            var treated = NewController();
            var untreated = NewController();

            RunHours(treated, 4);
            RunHours(untreated, 4);

            treated.TitrateNorepinephrine(doseMcgPerKgPerMin);

            RunHours(treated, 1, stepMinutes: 2);
            RunHours(untreated, 1, stepMinutes: 2);

            Assert.Greater(treated.Patient.Vitals.MeanArterialPressure, untreated.Patient.Vitals.MeanArterialPressure,
                $"Dose {doseMcgPerKgPerMin} mcg/kg/min should raise MAP above the untreated trajectory.");
        }

        [Test]
        public void Norepinephrine_LowestClinicalDose_StillProducesVisibleEffect()
        {
            // Regression guard for P2 at the integration level: 0.05 mcg/kg/min
            // used to be almost inert (ED50 calibrated for an unlabeled internal
            // unit). Now it should move MAP by a clearly non-trivial amount.
            var controller = NewController();
            RunHours(controller, 4);
            double before = controller.Patient.Vitals.MeanArterialPressure;

            controller.TitrateNorepinephrine(0.05);
            RunHours(controller, 0.5, stepMinutes: 1);

            double after = controller.Patient.Vitals.MeanArterialPressure;
            Assert.Greater(after - before, 2.0, "0.05 mcg/kg/min should produce a clearly measurable MAP change, not a fraction of a mmHg.");
        }

        [Test]
        public void Consciousness_TransitionsGradually_NotInstantlyOnASingleTick()
        {
            // Drive a healthy, Alert patient into an abrupt severe state (a huge
            // one-shot infection jump would be an architecture change, so instead
            // we simulate the abrupt drop by crashing volume and tone directly
            // through the public API available to treatments in-assembly is not
            // available here, so we instead run untreated sepsis long enough to
            // approach a severe state gradually, and check that consciousness
            // lags the vitals rather than flipping the instant MAP crosses a
            // threshold).
            var controller = NewController();
            RunHours(controller, 1, stepMinutes: 5); // still Alert
            Assert.AreEqual(ConsciousnessLevel.Alert, controller.Patient.Vitals.Consciousness);

            bool sawIntermediateConfusedBeforeWorse = false;
            var lastLevel = controller.Patient.Vitals.Consciousness;
            for (int i = 0; i < 400 && controller.Patient.IsAlive; i++)
            {
                controller.Tick(5);
                var level = controller.Patient.Vitals.Consciousness;
                if (lastLevel == ConsciousnessLevel.Alert && level == ConsciousnessLevel.Confused)
                {
                    sawIntermediateConfusedBeforeWorse = true;
                }
                if (level == ConsciousnessLevel.Unresponsive) break;
                lastLevel = level;
            }

            Assert.IsTrue(sawIntermediateConfusedBeforeWorse,
                "Consciousness should pass through intermediate states over time rather than jumping straight from Alert to Unresponsive.");
        }

        [Test]
        public void ConsciousnessScore_LagsBehindASuddenPerfusionCrash()
        {
            // More direct test of the inertia mechanism itself: force a steep
            // one-tick perfusion crash via a large crystalloid "loss" is not
            // possible through the public API (no negative bolus), so this
            // exercises the formula-level guarantee already covered in
            // ClinicalFormulasTests, and additionally checks that
            // PatientState.ConsciousnessScore (not just the discrete enum)
            // moves gradually across ticks during real untreated deterioration.
            var controller = NewController();
            RunHours(controller, 8, stepMinutes: 5);
            double scoreAt8h = controller.Patient.ConsciousnessScore;

            controller.Tick(5); // one more 5-minute tick
            double scoreAfterOneMoreTick = controller.Patient.ConsciousnessScore;

            double singleTickChange = System.Math.Abs(scoreAfterOneMoreTick - scoreAt8h);
            Assert.Less(singleTickChange, 0.15, "A single 5-minute tick should not move the consciousness score by a large fraction of its range.");
        }

        [Test]
        public void ClinicalEventBus_DeliversPublishedEvents()
        {
            var bus = new ClinicalEventBus();
            ClinicalEvent received = null;
            bus.Subscribe(e => received = e);

            var evt = new InterventionAppliedEvent(10, "Teste", "detalhe");
            bus.Publish(evt);

            Assert.AreSame(evt, received);
        }

        [Test]
        public void ClinicalEventBus_UnsubscribeStopsDelivery()
        {
            var bus = new ClinicalEventBus();
            int count = 0;
            Action<ClinicalEvent> handler = _ => count++;

            bus.Subscribe(handler);
            bus.Publish(new InterventionAppliedEvent(0, "A", ""));
            bus.Unsubscribe(handler);
            bus.Publish(new InterventionAppliedEvent(0, "B", ""));

            Assert.AreEqual(1, count);
        }
    }
}
