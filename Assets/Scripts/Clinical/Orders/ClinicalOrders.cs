using System;
using System.Collections.Generic;

namespace MedicalShiftSimulator.Clinical
{
    public enum ClinicalOrderType
    {
        Oxygen,
        Crystalloid,
        Norepinephrine,
        Antibiotic,
        LaboratoryExam
    }

    public enum ClinicalOrderPriority
    {
        Routine = 0,
        Urgent = 1,
        Immediate = 2
    }

    public enum ClinicalOrderStatus
    {
        Ordered,
        Queued,
        InProgress,
        Completed,
        Cancelled
    }

    public sealed class ClinicalOrderParameters
    {
        public int OxygenLevel { get; }
        public double VolumeMl { get; }
        public double NorepinephrineDose { get; }
        public string ExamId { get; }

        public ClinicalOrderParameters(int oxygenLevel = 0, double volumeMl = 0, double norepinephrineDose = 0, string examId = null)
        {
            OxygenLevel = oxygenLevel;
            VolumeMl = volumeMl;
            NorepinephrineDose = norepinephrineDose;
            ExamId = examId;
        }
    }

    public sealed class ClinicalOrder
    {
        public int Id { get; }
        public PatientState Patient { get; }
        public ClinicalOrderType Type { get; }
        public ClinicalOrderParameters Parameters { get; }
        public double PrescribedAtMinutes { get; }
        public ClinicalOrderPriority Priority { get; }
        public ClinicalOrderStatus Status { get; internal set; }
        public double? StartedAtMinutes { get; internal set; }
        public double? CompletedAtMinutes { get; internal set; }
        public double ExecutionDurationMinutes { get; internal set; }
        public PatientExamOrder ExamOrder { get; internal set; }

        internal ClinicalOrder(int id, PatientState patient, ClinicalOrderType type, ClinicalOrderParameters parameters, double prescribedAtMinutes, ClinicalOrderPriority priority, double executionDurationMinutes)
        {
            Id = id;
            Patient = patient;
            Type = type;
            Parameters = parameters;
            PrescribedAtMinutes = prescribedAtMinutes;
            Priority = priority;
            Status = ClinicalOrderStatus.Ordered;
            ExecutionDurationMinutes = Math.Max(0.1, executionDurationMinutes);
        }

        public string Summary()
        {
            switch (Type)
            {
                case ClinicalOrderType.Oxygen: return "Oxigênio nível " + Parameters.OxygenLevel;
                case ClinicalOrderType.Crystalloid: return "Cristaloide " + Parameters.VolumeMl.ToString("F0") + " mL";
                case ClinicalOrderType.Norepinephrine: return "Noradrenalina " + Parameters.NorepinephrineDose.ToString("F2") + " mcg/kg/min";
                case ClinicalOrderType.Antibiotic: return "Antibiótico";
                default: return "Exame: " + Parameters.ExamId;
            }
        }
    }

    public sealed class NursingTaskQueue
    {
        private readonly List<ClinicalOrder> _pending = new List<ClinicalOrder>();
        public IReadOnlyList<ClinicalOrder> Pending => _pending;

        public void Enqueue(ClinicalOrder order)
        {
            if (order == null || _pending.Contains(order)) return;
            _pending.Add(order);
        }

        public ClinicalOrder TakeNext()
        {
            ClinicalOrder best = null;
            for (int i = 0; i < _pending.Count; i++)
            {
                var candidate = _pending[i];
                if (candidate.Status == ClinicalOrderStatus.Cancelled || candidate.Status == ClinicalOrderStatus.Completed)
                    continue;

                if (best == null || candidate.Priority > best.Priority ||
                    (candidate.Priority == best.Priority && candidate.PrescribedAtMinutes < best.PrescribedAtMinutes))
                    best = candidate;
            }

            if (best != null) _pending.Remove(best);
            return best;
        }

        public void Remove(ClinicalOrder order)
        {
            _pending.Remove(order);
        }
    }
}
