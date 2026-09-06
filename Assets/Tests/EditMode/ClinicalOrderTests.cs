using NUnit.Framework;
using System.Linq;
using MedicalShiftSimulator.Clinical;

namespace MedicalShiftSimulator.Clinical.Tests
{
    public class ClinicalOrderTests
    {
        [Test]
        public void PrescriptionCreatesQueuedOrderForCorrectPatient()
        {
            var controller = CreateController();
            var order = controller.Orders.PrescribeOxygen(2);

            Assert.AreEqual(ClinicalOrderStatus.Ordered, order.Status);
            Assert.AreSame(controller.Patient, order.Patient);
            Assert.AreEqual(ClinicalOrderType.Oxygen, order.Type);
        }

        [Test]
        public void TreatmentDoesNotStartAtPrescription()
        {
            var controller = CreateController();
            var order = controller.Orders.PrescribeCrystalloid(500);

            Assert.AreEqual(1.0, controller.Patient.IntravascularVolume, 0.001);
            Assert.AreEqual(0.0, controller.Norepinephrine.Dose, 0.001);
            Assert.AreEqual(ClinicalOrderStatus.Ordered, order.Status);
        }

        [Test]
        public void ImmediatePriorityStartsBeforeRoutine()
        {
            var controller = CreateController();
            var routine = controller.Orders.PrescribeCrystalloid(500, ClinicalOrderPriority.Routine);
            var immediate = controller.Orders.PrescribeNorepinephrine(0.1, ClinicalOrderPriority.Immediate);

            controller.Tick(1.0);

            Assert.AreEqual(ClinicalOrderStatus.InProgress, immediate.Status);
            Assert.AreEqual(ClinicalOrderStatus.Queued, routine.Status);
        }

        [Test]
        public void CrystalloidTakesConfiguredInfusionTime()
        {
            var controller = CreateController();
            var order = controller.Orders.PrescribeCrystalloid(500);

            controller.Tick(1.0);
            Assert.AreEqual(ClinicalOrderStatus.InProgress, order.Status);
            controller.Tick(18.0);
            Assert.AreEqual(ClinicalOrderStatus.InProgress, order.Status);
            controller.Tick(1.0);
            Assert.AreEqual(ClinicalOrderStatus.Completed, order.Status);
        }

        [Test]
        public void OxygenRemainsActiveAfterOrderCompletion()
        {
            var controller = CreateController();
            var order = controller.Orders.PrescribeOxygen(2);

            controller.Tick(1.0);
            controller.Tick(1.0);
            controller.Tick(10.0);

            Assert.AreEqual(ClinicalOrderStatus.Completed, order.Status);
            Assert.AreEqual(2, controller.Oxygen.Level);
            Assert.AreEqual(2, controller.Patient.OxygenSupportLevel);
        }

        [Test]
        public void NorepinephrineCanStartChangeAndSuspend()
        {
            var controller = CreateController();
            var start = controller.Orders.PrescribeNorepinephrine(0.10);
            controller.Tick(1.0);
            controller.Tick(2.0);
            Assert.AreEqual(ClinicalOrderStatus.Completed, start.Status);
            Assert.AreEqual(0.10, controller.Norepinephrine.Dose, 0.001);

            var change = controller.Orders.PrescribeNorepinephrine(0.20);
            controller.Tick(2.0);
            controller.Tick(2.0);
            Assert.AreEqual(ClinicalOrderStatus.Completed, change.Status);
            Assert.AreEqual(0.20, controller.Norepinephrine.Dose, 0.001);

            var stop = controller.Orders.PrescribeNorepinephrine(0.0);
            controller.Tick(2.0);
            controller.Tick(2.0);
            Assert.AreEqual(ClinicalOrderStatus.Completed, stop.Status);
            Assert.AreEqual(0.0, controller.Norepinephrine.Dose, 0.001);
        }

        [Test]
        public void TimelineSeparatesPrescriptionAndExecution()
        {
            var controller = CreateController();
            controller.Orders.PrescribeAntibiotic();
            controller.Tick(1.0);

            Assert.IsTrue(controller.Timeline.Entries.Any(e => e.EventType == "Prescrição"));
            Assert.IsTrue(controller.Timeline.Entries.Any(e => e.EventType == "Enfermagem iniciou"));
        }

        [Test]
        public void ExamCollectionStartsOnlyWhenNursingStartsOrder()
        {
            var controller = CreateController();
            var order = controller.Orders.PrescribeExam("lactato");
            var exam = order.ExamOrder;

            Assert.AreEqual(ClinicalExamStatus.Ordered, exam.Status);
            controller.Tick(1.0);
            Assert.AreEqual(ClinicalExamStatus.WaitingCollection, exam.Status);
            controller.Tick(1.0);
            Assert.AreEqual(ClinicalExamStatus.Processing, exam.Status);
            controller.Tick(3.0);
            Assert.AreEqual(ClinicalExamStatus.Completed, exam.Status);
        }

        [Test]
        public void CancelledOrderDoesNotExecute()
        {
            var controller = CreateController();
            var order = controller.Orders.PrescribeOxygen(2);

            Assert.IsTrue(controller.Orders.Cancel(order));
            controller.Tick(5.0);

            Assert.AreEqual(ClinicalOrderStatus.Cancelled, order.Status);
            Assert.AreEqual(0, controller.Oxygen.Level);
        }

        [Test]
        public void MultipleOrdersRemainPatientScopedAndProgressWithClockAcceleration()
        {
            var controller = CreateController();
            var oxygen = controller.Orders.PrescribeOxygen(1);
            var antibiotic = controller.Orders.PrescribeAntibiotic();
            var exam = controller.Orders.PrescribeExam("hemograma");

            controller.Tick(30.0);
            controller.Tick(30.0);
            controller.Tick(30.0);

            Assert.AreSame(controller.Patient, oxygen.Patient);
            Assert.AreSame(controller.Patient, antibiotic.Patient);
            Assert.AreSame(controller.Patient, exam.Patient);
            Assert.AreEqual(ClinicalOrderStatus.Completed, oxygen.Status);
            Assert.AreEqual(ClinicalOrderStatus.Completed, antibiotic.Status);
            Assert.AreEqual(ClinicalExamStatus.Completed, exam.ExamOrder.Status);
        }

        private static PatientSimulationController CreateController()
        {
            var patient = new PatientState { Name = "Teste" };
            return new PatientSimulationController(patient, new SepsisDiseaseProcess());
        }
    }
}
