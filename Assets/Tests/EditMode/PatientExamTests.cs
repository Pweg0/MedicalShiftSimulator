
using System;
using NUnit.Framework;
using MedicalShiftSimulator.Clinical;

namespace MedicalShiftSimulator.Clinical.Tests
{
    public class PatientExamTests
    {
        [Test]
        public void OrderStartsOrderedAndBelongsToPatient()
        {
            var controller = CreateController();
            var order = controller.OrderExam("lactato");

            Assert.AreEqual(ClinicalExamStatus.Ordered, order.Status);
            Assert.AreSame(controller.Patient, order.Patient);
        }

        [Test]
        public void ExamDoesNotCompleteBeforeConfiguredTime()
        {
            var controller = CreateController();
            var order = controller.OrderExam("lactato");

            controller.Tick(4.9);

            Assert.AreNotEqual(ClinicalExamStatus.Completed, order.Status);
            Assert.IsNull(order.Result);
        }

[Test]
        public void ExamProgressesThroughCollectionAndProcessingStates()
        {
            var controller = CreateController();
            var order = controller.OrderExam("lactato");

            controller.Tick(1.0);
            Assert.AreEqual(ClinicalExamStatus.WaitingCollection, order.Status);
            controller.Tick(1.0);
            Assert.AreEqual(ClinicalExamStatus.Processing, order.Status);
            controller.Tick(3.0);
            Assert.AreEqual(ClinicalExamStatus.Completed, order.Status);
        }


        [Test]
        public void ExamCompletesAfterCollectionAndProcessingTime()
        {
            var controller = CreateController();
            var order = controller.OrderExam("lactato");

            controller.Tick(5.0);

            Assert.AreEqual(ClinicalExamStatus.Completed, order.Status);
            Assert.IsNotNull(order.Result);
            Assert.AreEqual("lactato", order.Result.ExamId);
        }

        [Test]
        public void FastAndSlowExamsHaveDifferentCompletionTimes()
        {
            var controller = CreateController();
            var lactate = controller.OrderExam("lactato");
            var cultures = controller.OrderExam("hemoculturas");

            controller.Tick(5.0);

            Assert.AreEqual(ClinicalExamStatus.Completed, lactate.Status);
            Assert.AreNotEqual(ClinicalExamStatus.Completed, cultures.Status);
        }

[Test]
        public void LactateReflectsPerfusionAtReleaseAndRemainsRecorded()
        {
            var controller = CreateController();
            controller.Tick(0.1);
            var firstOrder = controller.OrderExam("lactato");
            controller.Tick(5.0);
            var firstResult = firstOrder.Result;

            controller.Tick(120.0);
            var secondOrder = controller.OrderExam("lactato");
            controller.Tick(5.0);

            Assert.IsNotNull(firstResult);
            Assert.IsNotNull(secondOrder.Result);
            Assert.Greater(Math.Abs(secondOrder.Result.NumericValue.Value - firstResult.NumericValue.Value), 0.05);
            Assert.AreSame(firstResult, firstOrder.Result);
        }

        [Test]
        public void ClockAccelerationCompletesExamWithOneLargeAdvance()
        {
            var controller = CreateController();
            var order = controller.OrderExam("hemograma");

            controller.Tick(20.0);

            Assert.AreEqual(ClinicalExamStatus.Completed, order.Status);
            Assert.IsTrue(controller.Clock.TotalSimulatedMinutes >= 20.0);
        }

        private static PatientSimulationController CreateController()
        {
            var patient = new PatientState { Name = "Teste" };
            return new PatientSimulationController(patient, new SepsisDiseaseProcess());
        }
    }
}
