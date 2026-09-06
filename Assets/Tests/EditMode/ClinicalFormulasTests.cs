using NUnit.Framework;
using MedicalShiftSimulator.Clinical;

namespace MedicalShiftSimulator.Clinical.Tests
{
    public class ClinicalFormulasTests
    {
        [Test]
        public void Map_AtBaselineSvrAndVolume_EqualsBaselineMAP()
        {
            double map = ClinicalFormulas.Map(1.0, 1.0);
            Assert.AreEqual(ClinicalFormulas.BaselineMAP, map, 0.01);
        }

        [Test]
        public void Map_DecreasesWhenSvrDrops()
        {
            double normal = ClinicalFormulas.Map(1.0, 1.0);
            double vasodilated = ClinicalFormulas.Map(0.5, 1.0);
            Assert.Less(vasodilated, normal);
        }

        [Test]
        public void Map_DecreasesWhenVolumeDrops()
        {
            double normal = ClinicalFormulas.Map(1.0, 1.0);
            double hypovolemic = ClinicalFormulas.Map(1.0, 0.3);
            Assert.Less(hypovolemic, normal);
        }

        [Test]
        public void Map_SevereHypovolemia_CanProduceCriticallyLowPressure()
        {
            // With the old hard 0.5 VolumeFactor floor this could never happen -
            // severe volume depletion alone should be able to produce a
            // dangerously low MAP even with completely normal vascular tone.
            double map = ClinicalFormulas.Map(1.0, ClinicalFormulas.VolumeFloor);
            Assert.Less(map, 40);
        }

        // ---- Norepinephrine: dose-response shape ----

        [Test]
        public void NorepinephrineEffect_IncreasesWithDose_AndSaturatesBelowEmax()
        {
            double none = ClinicalFormulas.NorepinephrineEffect(0, infectionBurden: 0);
            double low = ClinicalFormulas.NorepinephrineEffect(0.1, infectionBurden: 0);
            double high = ClinicalFormulas.NorepinephrineEffect(3.0, infectionBurden: 0);

            Assert.AreEqual(0, none, 0.0001);
            Assert.Greater(low, none);
            Assert.Greater(high, low);
            Assert.Less(high, ClinicalFormulas.NorepinephrineEmax + 0.0001);
        }

        [Test]
        public void NorepinephrineEffect_MarginalGain_ShrinksAtHigherDoses()
        {
            // "resposta progressivamente menor em doses maiores": the gain from
            // 0.2->0.5 should be bigger than the gain from 1.0->1.3, even though
            // both steps add the same 0.3 dose.
            double e0 = ClinicalFormulas.NorepinephrineEffect(0.2, 0);
            double e1 = ClinicalFormulas.NorepinephrineEffect(0.5, 0);
            double e2 = ClinicalFormulas.NorepinephrineEffect(1.0, 0);
            double e3 = ClinicalFormulas.NorepinephrineEffect(1.3, 0);

            double lowRangeGain = e1 - e0;
            double highRangeGain = e3 - e2;

            Assert.Greater(lowRangeGain, highRangeGain);
        }

        [Test]
        public void NorepinephrineEffect_IsBlunted_BySevereVasoplegia()
        {
            // "efeito dependente do grau de vasoplegia": the same dose should
            // move SVR less in a severely vasoplegic (high infection burden)
            // patient than in an otherwise healthy one - catecholamine resistance.
            double effectHealthy = ClinicalFormulas.NorepinephrineEffect(0.5, infectionBurden: 0.0);
            double effectSevereShock = ClinicalFormulas.NorepinephrineEffect(0.5, infectionBurden: 1.0);

            Assert.Greater(effectHealthy, effectSevereShock);
            Assert.Greater(effectSevereShock, 0, "Even in severe vasoplegia there should be SOME residual response, not zero.");
        }

        [Test]
        public void NorepinephrineEffect_InNonVasoplegicPatient_DoesNotProduceExtremeSvr()
        {
            // Regression guard for the original bug: a moderate-to-high dose in a
            // patient who isn't in shock should raise SVR meaningfully but should
            // stay in a plausible "iatrogenic hypertension" range, not double it.
            double target = ClinicalFormulas.SvrTarget(infectionBurden: 0.05, norepinephrineDose: 1.0);
            Assert.Less(target, 1.7);
            Assert.Greater(target, 1.0);
        }

        [Test]
        public void Map_WithHighNorepinephrineDose_StaysBoundedByAfterloadFeedback()
        {
            // Regression guard: previously this scenario clamped straight to the
            // 150 mmHg ceiling. It should now land well below that because rising
            // SVR reduces the afterload factor (emergent limit, not a MAP clamp).
            double svr = ClinicalFormulas.SvrTarget(infectionBurden: 0.05, norepinephrineDose: 2.0);
            double map = ClinicalFormulas.Map(svr, 1.0);

            Assert.Less(map, 130);
        }

        [Test]
        public void AfterloadFactor_ReducesEffectiveOutput_AboveNormalSvr()
        {
            double atNormal = ClinicalFormulas.AfterloadFactor(1.0);
            double whenConstricted = ClinicalFormulas.AfterloadFactor(1.8);
            Assert.AreEqual(1.0, atNormal, 0.0001);
            Assert.Less(whenConstricted, 1.0);
        }

        [Test]
        public void AfterloadFactor_MildlyBoostsOutput_BelowNormalSvr()
        {
            // "Warm shock" high-output pattern: mild vasodilation alone gives a
            // small compensatory output bump, not a penalty.
            double whenDilated = ClinicalFormulas.AfterloadFactor(0.6);
            Assert.Greater(whenDilated, 1.0);
        }

        [Test]
        public void HeartRate_HasDirectDoseDependentComponent_FromNorepinephrine()
        {
            // Even at a MAP where the reflex term is saturated/absent, a high
            // norepinephrine dose should still carry a visible direct chronotropic
            // (arrhythmia-risk) cost.
            double hrNoDrug = ClinicalFormulas.HeartRate(93, 37, norepinephrineDose: 0);
            double hrHighDose = ClinicalFormulas.HeartRate(93, 37, norepinephrineDose: 1.5);
            Assert.Greater(hrHighDose, hrNoDrug);
        }

        [Test]
        public void HeartRate_IncreasesAsMapDropsAboveDecompensation()
        {
            double hrNormal = ClinicalFormulas.HeartRate(93, 37);
            double hrLow = ClinicalFormulas.HeartRate(60, 37);
            Assert.Greater(hrLow, hrNormal);
        }

        [Test]
        public void HeartRate_IncreasesWithFever()
        {
            double afebrile = ClinicalFormulas.HeartRate(93, 37.0);
            double febrile = ClinicalFormulas.HeartRate(93, 39.5);
            Assert.Greater(febrile, afebrile);
        }

        [Test]
        public void StepSvr_MovesTowardTarget_NotInstantly()
        {
            double next = ClinicalFormulas.StepSvr(1.0, infectionBurden: 1.0, norepinephrineDose: 0, deltaHours: 0.01);
            double target = ClinicalFormulas.SvrTarget(1.0, 0);
            Assert.Less(next, 1.0);          // moved down toward target
            Assert.Greater(next, target);    // but has not fully arrived yet
        }

        [Test]
        public void StepSvr_IsStepSizeIndependent()
        {
            // Same total elapsed time, chopped into 1 big tick vs many small
            // ticks, must produce (numerically) the same result - this is what
            // the closed-form exponential relaxation buys us over a
            // linear-interpolation-capped-at-1 approach.
            double oneBigStep = ClinicalFormulas.StepSvr(1.0, infectionBurden: 1.0, norepinephrineDose: 0.3, deltaHours: 1.0);

            double manySmallSteps = 1.0;
            for (int i = 0; i < 60; i++)
            {
                manySmallSteps = ClinicalFormulas.StepSvr(manySmallSteps, infectionBurden: 1.0, norepinephrineDose: 0.3, deltaHours: 1.0 / 60.0);
            }

            Assert.AreEqual(oneBigStep, manySmallSteps, 0.0005);
        }

        [Test]
        public void StepVolume_IsStepSizeIndependent()
        {
            double oneBigStep = ClinicalFormulas.StepVolume(1.0, infectionBurden: 0.8, deltaHours: 2.0);

            double manySmallSteps = 1.0;
            for (int i = 0; i < 120; i++)
            {
                manySmallSteps = ClinicalFormulas.StepVolume(manySmallSteps, infectionBurden: 0.8, deltaHours: 2.0 / 120.0);
            }

            Assert.AreEqual(oneBigStep, manySmallSteps, 0.0005);
        }

        [Test]
        public void StepLactate_IsStepSizeIndependent()
        {
            double oneBigStep = ClinicalFormulas.StepLactate(3.0, map: 50, volume: 1.0, svr: 1.0, deltaHours: 1.0);

            double manySmallSteps = 3.0;
            for (int i = 0; i < 60; i++)
            {
                manySmallSteps = ClinicalFormulas.StepLactate(manySmallSteps, map: 50, volume: 1.0, svr: 1.0, deltaHours: 1.0 / 60.0);
            }

            Assert.AreEqual(oneBigStep, manySmallSteps, 0.0005);
        }

        [Test]
        public void VolumeFactor_DiminishingReturns_AboveNormovolemia()
        {
            double baseline = ClinicalFormulas.VolumeFactor(1.0);
            double moderate = ClinicalFormulas.VolumeFactor(1.3);
            double large = ClinicalFormulas.VolumeFactor(3.0);
            double huge = ClinicalFormulas.VolumeFactor(10.0);

            Assert.Greater(moderate, baseline);
            Assert.Greater(large, moderate);
            // Even a hugely excessive volume load should not keep buying
            // meaningfully more benefit than a merely large one (smooth saturating
            // curve, not a hard clamp).
            Assert.Less(huge - large, 0.01);
        }

        [Test]
        public void SpO2_DropsWithInfectionBurden_AndImprovesWithOxygen()
        {
            double healthy = ClinicalFormulas.SpO2(0.0, 0);
            double sick = ClinicalFormulas.SpO2(0.8, 0);
            double sickWithO2 = ClinicalFormulas.SpO2(0.8, 2);

            Assert.Greater(healthy, sick);
            Assert.Greater(sickWithO2, sick);
        }

        [Test]
        public void SpO2_SevereLungInjury_CanBeCriticallyLow_EvenWithMaxOxygen()
        {
            // Shunt physiology: maximal simple oxygen therapy should NOT fully
            // rescue severe lung injury (previously SpO2 could never drop below a
            // hard-coded 70% floor, which made isolated respiratory failure
            // impossible regardless of severity).
            double severeWithMaxO2 = ClinicalFormulas.SpO2(infectionBurden: 1.0, oxygenSupportLevel: 2);
            Assert.Less(severeWithMaxO2, 65);
        }

        [Test]
        public void SpO2_OxygenBenefit_IsBluntedBySeverity()
        {
            double benefitWhenMild = ClinicalFormulas.SpO2(0.1, 2) - ClinicalFormulas.SpO2(0.1, 0);
            double benefitWhenSevere = ClinicalFormulas.SpO2(1.0, 2) - ClinicalFormulas.SpO2(1.0, 0);
            Assert.Greater(benefitWhenMild, benefitWhenSevere);
        }

        [Test]
        public void UrineOutput_ZeroBelowShutdownMAP()
        {
            double urine = ClinicalFormulas.UrineOutput(20, volume: 1.0, svr: 1.0);
            Assert.AreEqual(0, urine, 0.01);
        }

        [Test]
        public void UrineOutput_NormalAtBaselineMAP()
        {
            double urine = ClinicalFormulas.UrineOutput(93, volume: 1.0, svr: 1.0);
            Assert.Greater(urine, 60);
        }

        [Test]
        public void UrineOutput_ReducedByPoorFlow_EvenAtGoodMAP()
        {
            // Revision 4: excessive vasoconstriction (or inadequate volume)
            // should reduce renal flow even when MAP looks fine.
            double normalFlow = ClinicalFormulas.UrineOutput(93, volume: 1.0, svr: 1.0);
            double poorFlow = ClinicalFormulas.UrineOutput(93, volume: 0.4, svr: 1.8);
            Assert.Less(poorFlow, normalFlow);
        }

        [Test]
        public void StepLactate_RisesUnderPoorPerfusion()
        {
            double lactate = ClinicalFormulas.StepLactate(1.0, map: 30, volume: 1.0, svr: 1.0, deltaHours: 1.0);
            Assert.Greater(lactate, 1.0);
        }

        [Test]
        public void StepLactate_ClearsUnderGoodPerfusion()
        {
            const double elevated = 5.0;
            double lactate = ClinicalFormulas.StepLactate(elevated, map: 93, volume: 1.0, svr: 1.0, deltaHours: 1.0);
            Assert.Less(lactate, elevated);
        }

        // ---- Revision 4 (P1): pulse pressure / DBP -------------------------

        [Test]
        public void BloodPressure_AtCriticallyLowMapAndSvr_DiastolicNeverNegative()
        {
            // Regression guard for P1: near-terminal shock (very low MAP, very
            // low SVR widening pulse pressure) used to send DBP negative.
            var bp = ClinicalFormulas.BloodPressure(map: 14.9, svr: 0.3);
            Assert.GreaterOrEqual(bp.diastolic, 0);
        }

        [Test]
        public void BloodPressure_AcrossExtremeMapRange_DiastolicNeverNegative()
        {
            for (double map = 5; map <= 150; map += 5)
            {
                for (double svr = ClinicalFormulas.SvrFloor; svr <= ClinicalFormulas.SvrCeiling; svr += 0.2)
                {
                    var bp = ClinicalFormulas.BloodPressure(map, svr);
                    Assert.GreaterOrEqual(bp.diastolic, 0, $"DBP negative at map={map}, svr={svr}");
                }
            }
        }

        [Test]
        public void PulsePressure_ScalesWithMap_NotIndependentOfIt()
        {
            // The Revision 4 fix specifically requires PP to depend on MAP
            // (not just SVR) - otherwise PP/3 can exceed a very low MAP again.
            double ppHighMap = ClinicalFormulas.PulsePressure(map: 90, svr: 1.0);
            double ppLowMap = ClinicalFormulas.PulsePressure(map: 20, svr: 1.0);
            Assert.Less(ppLowMap, ppHighMap);
        }

        // ---- Revision 4 (P3): explicit flow / effective perfusion ----------

        [Test]
        public void EffectivePerfusion_DiffersAtSameMap_WhenFlowDiffers()
        {
            // Same MAP (~90), reached two different ways: (a) adequate volume
            // and near-normal SVR, vs. (b) low volume compensated by high SVR
            // (vasoconstriction without volume). Perfusion must differ even
            // though pressure does not - this is the core P3 fix.
            double svrA = 1.0, volumeA = 1.05; // adequate volume, near-normal tone
            double mapA = ClinicalFormulas.Map(svrA, volumeA);

            double svrB = 1.6, volumeB = 0.838; // low volume compensated by higher SVR - solved to land near mapA
            double mapB = ClinicalFormulas.Map(svrB, volumeB);

            double perfusionA = ClinicalFormulas.EffectivePerfusion(mapA, volumeA, svrA);
            double perfusionB = ClinicalFormulas.EffectivePerfusion(mapB, volumeB, svrB);

            // Sanity: the two MAPs should be reasonably close for this to be a fair comparison.
            Assert.Less(System.Math.Abs(mapA - mapB), 15, $"Test setup invalid - MAPs too far apart ({mapA} vs {mapB})");
            Assert.Greater(perfusionA, perfusionB, "Volume-supported perfusion should exceed vasoconstriction-only perfusion at a similar MAP.");
        }

        [Test]
        public void StepLactate_SamePressureDifferentPerfusion_ProducesDifferentLactate()
        {
            double map = 90;
            double lactateWithVolume = ClinicalFormulas.StepLactate(1.0, map, volume: 1.1, svr: 1.0, deltaHours: 4.0);
            double lactateVasoconstrictedOnly = ClinicalFormulas.StepLactate(1.0, map, volume: 0.5, svr: 1.6, deltaHours: 4.0);

            Assert.Less(lactateWithVolume, lactateVasoconstrictedOnly,
                "At the same MAP, a volume-depleted/vasoconstricted patient should clear lactate worse than a volume-replete one.");
        }

        [Test]
        public void EffectivePerfusion_GatedByAutoregulation_BelowThresholdMAP()
        {
            // Even with normal flow, perfusion should fall once MAP drops below
            // the autoregulation threshold - flow alone isn't sufficient.
            double perfusionNormalMap = ClinicalFormulas.EffectivePerfusion(map: 90, volume: 1.0, svr: 1.0);
            double perfusionLowMap = ClinicalFormulas.EffectivePerfusion(map: 30, volume: 1.0, svr: 1.0);
            Assert.Less(perfusionLowMap, perfusionNormalMap);
        }

        // ---- Revision 4 (P2): norepinephrine dose in real mcg/kg/min -------

        [TestCase(0.05)]
        [TestCase(0.1)]
        [TestCase(0.2)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void NorepinephrineEffect_AtClinicalDoses_IsMonotonicIncreasing(double dose)
        {
            double effect = ClinicalFormulas.NorepinephrineEffect(dose, infectionBurden: 0.1);
            double effectAtNextLowerReferenceDose = ClinicalFormulas.NorepinephrineEffect(dose * 0.5, infectionBurden: 0.1);
            Assert.Greater(effect, effectAtNextLowerReferenceDose);
        }

        [Test]
        public void NorepinephrineEffect_AtLowClinicalDose_IsNoLongerNegligible()
        {
            // Regression guard for P2: 0.05 mcg/kg/min is a real, clinically
            // active low starting dose - it should NOT be barely perceptible.
            double effect = ClinicalFormulas.NorepinephrineEffect(0.05, infectionBurden: 0.1);
            double fractionOfEmax = effect / ClinicalFormulas.NorepinephrineEmax;
            Assert.Greater(fractionOfEmax, 0.15, "0.05 mcg/kg/min should produce a clearly non-negligible fraction of the max effect.");
        }

        [Test]
        public void NorepinephrineEffect_AtHighClinicalDose_NearlySaturates()
        {
            double effect = ClinicalFormulas.NorepinephrineEffect(1.0, infectionBurden: 0.1);
            double fractionOfEmax = effect / ClinicalFormulas.NorepinephrineEmax;
            Assert.Greater(fractionOfEmax, 0.85, "1.0 mcg/kg/min should be close to the saturating high end of clinical dosing.");
        }

        // ---- Revision 4 (P4): respiratory rate saturation, not a hard clamp ----

        [Test]
        public void RespiratoryRate_MarginalGain_ShrinksAtHigherDrive()
        {
            double rrLowDrive = ClinicalFormulas.RespiratoryRate(spo2: 90, lactate: 2, temperature: 37);
            double rrMidDrive = ClinicalFormulas.RespiratoryRate(spo2: 70, lactate: 4, temperature: 38);
            double rrExtremeDrive = ClinicalFormulas.RespiratoryRate(spo2: 35, lactate: 20, temperature: 41);
            double rrMoreExtremeDrive = ClinicalFormulas.RespiratoryRate(spo2: 35, lactate: 40, temperature: 41);

            double midGain = rrMidDrive - rrLowDrive;
            double extremeGain = rrMoreExtremeDrive - rrExtremeDrive;

            Assert.Greater(midGain, extremeGain, "Marginal RR gain should shrink as drive keeps increasing (saturating, not linear-to-a-wall).");
        }

        [Test]
        public void RespiratoryRate_AtExtremeDrive_ApproachesButNeverReachesHardCeiling()
        {
            double rr = ClinicalFormulas.RespiratoryRate(spo2: 35, lactate: 50, temperature: 42);
            Assert.Less(rr, 50.0, "Should approach the old ceiling asymptotically, never reach or exceed it as a hard wall.");
            Assert.Greater(rr, 45.0, "Should still be close to the asymptote at extreme drive.");
        }

        [Test]
        public void RespiratoryDrive_IsUnboundedInput_ButRateStaysSaturated()
        {
            double driveModerate = ClinicalFormulas.RespiratoryDrive(spo2: 80, lactate: 3, temperature: 38);
            double driveExtreme = ClinicalFormulas.RespiratoryDrive(spo2: 35, lactate: 100, temperature: 42);
            Assert.Greater(driveExtreme, driveModerate * 10, "The underlying drive itself is unbounded (sanity check on the test setup).");

            double rrModerate = ClinicalFormulas.RespiratoryRate(80, 3, 38);
            double rrExtreme = ClinicalFormulas.RespiratoryRate(35, 100, 42);
            Assert.Less(rrExtreme - rrModerate, 20, "Despite a huge difference in raw drive, the saturating RR response compresses the difference.");
        }

        // ---- Revision 4 (P5): consciousness inertia / hysteresis -----------

        [Test]
        public void ConsciousnessScore_DoesNotJumpInstantlyToTarget()
        {
            double next = ClinicalFormulas.StepConsciousnessScore(currentScore: 1.0, targetScore: 0.0, deltaHours: 0.02);
            Assert.Greater(next, 0.0);
            Assert.Less(next, 1.0);
        }

        [Test]
        public void ConsciousnessScore_GraduallyApproachesTarget_OverMultipleTicks()
        {
            double score = 1.0; // fully alert
            double target = 0.0; // sudden catastrophic drop in perfusion/oxygenation

            double afterOneTick = ClinicalFormulas.StepConsciousnessScore(score, target, deltaHours: 0.1);
            double afterManyTicks = afterOneTick;
            for (int i = 0; i < 20; i++)
            {
                afterManyTicks = ClinicalFormulas.StepConsciousnessScore(afterManyTicks, target, deltaHours: 0.1);
            }

            Assert.Greater(afterOneTick, afterManyTicks, "Score should keep decaying toward the target over successive ticks, not jump there in one.");
            Assert.Less(afterManyTicks, 0.05, "After enough elapsed time it should nonetheless get very close to the target.");
        }

        [Test]
        public void Consciousness_LevelFromScore_MatchesThresholds()
        {
            Assert.AreEqual(ConsciousnessLevel.Alert, ClinicalFormulas.ConsciousnessLevelFromScore(0.9));
            Assert.AreEqual(ConsciousnessLevel.Confused, ClinicalFormulas.ConsciousnessLevelFromScore(0.6));
            Assert.AreEqual(ConsciousnessLevel.Drowsy, ClinicalFormulas.ConsciousnessLevelFromScore(0.3));
            Assert.AreEqual(ConsciousnessLevel.Unresponsive, ClinicalFormulas.ConsciousnessLevelFromScore(0.1));
        }

        [Test]
        public void ConsciousnessScoreTarget_IsLimitedByWorseOfPerfusionOrOxygenation()
        {
            double goodPerfusionBadOxygen = ClinicalFormulas.ConsciousnessScoreTarget(effectivePerfusion: 1.0, spo2: 70);
            double badPerfusionGoodOxygen = ClinicalFormulas.ConsciousnessScoreTarget(effectivePerfusion: 0.0, spo2: 98);
            Assert.AreEqual(0.0, goodPerfusionBadOxygen, 0.01);
            Assert.AreEqual(0.0, badPerfusionGoodOxygen, 0.01);
        }

        [Test]
        public void AntibioticEffectiveLevel_IsNotImmediate()
        {
            // "não deve produzir melhora imediata": in the first few minutes after
            // dosing, effective level should be small.
            double at5min = ClinicalFormulas.AntibioticEffectiveLevel(5.0 / 60.0);
            Assert.Less(at5min, 0.15);
        }

        [Test]
        public void AntibioticEffectiveLevel_RisesSmoothly_ThenNearlyPlateaus_ThenWanes()
        {
            double at0 = ClinicalFormulas.AntibioticEffectiveLevel(0);
            double at1h = ClinicalFormulas.AntibioticEffectiveLevel(1.0);
            double atCoverageEnd = ClinicalFormulas.AntibioticEffectiveLevel(ClinicalFormulas.AntibioticCoverageHours);
            double wellAfterCoverage = ClinicalFormulas.AntibioticEffectiveLevel(ClinicalFormulas.AntibioticCoverageHours + 3.0);

            Assert.AreEqual(0, at0, 0.0001);
            Assert.Greater(at1h, 0.5);
            Assert.Greater(atCoverageEnd, 0.98);
            Assert.Less(wellAfterCoverage, atCoverageEnd);
        }

        [Test]
        public void ConsciousnessScoreTarget_AlertAtNormalVitals()
        {
            double target = ClinicalFormulas.ConsciousnessScoreTarget(effectivePerfusion: 1.0, spo2: 98);
            Assert.AreEqual(ConsciousnessLevel.Alert, ClinicalFormulas.ConsciousnessLevelFromScore(target));
        }

        [Test]
        public void ConsciousnessScoreTarget_UnresponsiveAtSevereShock()
        {
            double perfusion = ClinicalFormulas.EffectivePerfusion(map: 25, volume: 0.6, svr: 0.5);
            double target = ClinicalFormulas.ConsciousnessScoreTarget(perfusion, spo2: 75);
            Assert.AreEqual(ConsciousnessLevel.Unresponsive, ClinicalFormulas.ConsciousnessLevelFromScore(target));
        }

        [Test]
        public void InfectionGrowth_IsZero_AtCarryingCapacity()
        {
            double growth = ClinicalFormulas.InfectionGrowth(ClinicalFormulas.InfectionCarryingCapacity, 1.0);
            Assert.AreEqual(0, growth, 0.0001);
        }

        [Test]
        public void InfectionKill_ScalesWithEffectiveLevel()
        {
            double noEffect = ClinicalFormulas.InfectionKill(0.5, 0.0, 1.0);
            double fullEffect = ClinicalFormulas.InfectionKill(0.5, 1.0, 1.0);
            Assert.AreEqual(0, noEffect, 0.0001);
            Assert.Greater(fullEffect, 0);
        }
    }
}
