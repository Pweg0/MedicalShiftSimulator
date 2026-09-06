using System;
using System.Collections.Generic;

namespace MedicalShiftSimulator.Clinical
{
    public sealed class ClinicalOrderService
    {
        private readonly PatientSimulationController _controller;
        private readonly List<ClinicalOrder> _orders = new List<ClinicalOrder>();
        private int _nextId = 1;
        private ClinicalOrder _inProgress;

        public NursingTaskQueue NursingQueue { get; } = new NursingTaskQueue();
        public IReadOnlyList<ClinicalOrder> Orders => _orders;
        public ClinicalOrder InProgress => _inProgress;

        public ClinicalOrderService(PatientSimulationController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public ClinicalOrder PrescribeOxygen(int level, ClinicalOrderPriority priority = ClinicalOrderPriority.Urgent)
        {
            var order = Create(ClinicalOrderType.Oxygen, new ClinicalOrderParameters(oxygenLevel: level), priority, 1);
            return Enqueue(order);
        }

        public ClinicalOrder PrescribeCrystalloid(double volumeMl, ClinicalOrderPriority priority = ClinicalOrderPriority.Urgent)
        {
            var order = Create(ClinicalOrderType.Crystalloid, new ClinicalOrderParameters(volumeMl: volumeMl), priority, 20);
            return Enqueue(order);
        }

        public ClinicalOrder PrescribeNorepinephrine(double dose, ClinicalOrderPriority priority = ClinicalOrderPriority.Immediate)
        {
            var order = Create(ClinicalOrderType.Norepinephrine, new ClinicalOrderParameters(norepinephrineDose: dose), priority, 2);
            return Enqueue(order);
        }

        public ClinicalOrder PrescribeAntibiotic(ClinicalOrderPriority priority = ClinicalOrderPriority.Urgent)
        {
            var order = Create(ClinicalOrderType.Antibiotic, new ClinicalOrderParameters(), priority, 5);
            return Enqueue(order);
        }

        public ClinicalOrder PrescribeExam(string examId, ClinicalOrderPriority priority = ClinicalOrderPriority.Urgent)
        {
            var order = Create(ClinicalOrderType.LaboratoryExam, new ClinicalOrderParameters(examId: examId), priority, 0.1);
            order.ExamOrder = _controller.Exams.OrderPendingCollection(examId);
            return Enqueue(order);
        }

        public void Advance()
        {
            double now = _controller.Clock.TotalSimulatedMinutes;

            for (int i = 0; i < _orders.Count; i++)
            {
                if (_orders[i].Status == ClinicalOrderStatus.Ordered)
                    _orders[i].Status = ClinicalOrderStatus.Queued;
            }

            if (_inProgress != null && now - _inProgress.StartedAtMinutes.Value >= _inProgress.ExecutionDurationMinutes)
            {
                Complete(_inProgress, now);
                _inProgress = null;
            }

            if (_inProgress == null)
            {
                var next = NursingQueue.TakeNext();
                if (next != null) Start(next, now);
            }
        }

        public bool Cancel(ClinicalOrder order)
        {
            if (order == null || order.Status == ClinicalOrderStatus.Completed || order.Status == ClinicalOrderStatus.Cancelled)
                return false;

            order.Status = ClinicalOrderStatus.Cancelled;
            NursingQueue.Remove(order);
            if (_inProgress == order)
            {
                if (order.Type == ClinicalOrderType.Norepinephrine)
                    _controller.SetNorepinephrineDose(0);
                _inProgress = null;
            }

            _controller.Timeline.Add(_controller.Clock.TotalSimulatedMinutes, "Ordem cancelada", order.Summary());
            return true;
        }

        private ClinicalOrder Create(ClinicalOrderType type, ClinicalOrderParameters parameters, ClinicalOrderPriority priority, double durationMinutes)
        {
            return new ClinicalOrder(_nextId++, _controller.Patient, type, parameters, _controller.Clock.TotalSimulatedMinutes, priority, durationMinutes);
        }

        private ClinicalOrder Enqueue(ClinicalOrder order)
        {
            _orders.Add(order);
            NursingQueue.Enqueue(order);
            _controller.Timeline.Add(_controller.Clock.TotalSimulatedMinutes, "Prescrição", order.Summary());
            return order;
        }

        private void Start(ClinicalOrder order, double now)
        {
            order.Status = ClinicalOrderStatus.InProgress;
            order.StartedAtMinutes = now;
            _inProgress = order;
            _controller.Timeline.Add(now, "Enfermagem iniciou", order.Summary());

            switch (order.Type)
            {
                case ClinicalOrderType.Oxygen:
                    _controller.SetOxygenLevel(order.Parameters.OxygenLevel);
                    break;
                case ClinicalOrderType.Crystalloid:
                    _controller.GiveCrystalloidBolus(order.Parameters.VolumeMl, order.ExecutionDurationMinutes);
                    break;
                case ClinicalOrderType.Norepinephrine:
                    _controller.SetNorepinephrineDose(order.Parameters.NorepinephrineDose);
                    break;
                case ClinicalOrderType.Antibiotic:
                    _controller.GiveAntibiotic();
                    break;
                case ClinicalOrderType.LaboratoryExam:
                    _controller.Exams.BeginCollection(order.ExamOrder);
                    order.ExecutionDurationMinutes = order.ExamOrder.Definition.CollectionMinutes;
                    break;
            }
        }

        private void Complete(ClinicalOrder order, double now)
        {
            order.Status = ClinicalOrderStatus.Completed;
            order.CompletedAtMinutes = now;
            _controller.Timeline.Add(now, "Enfermagem concluiu", order.Summary());
        }
    }
}
