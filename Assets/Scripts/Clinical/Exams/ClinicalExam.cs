using System;
using System.Collections.Generic;

namespace MedicalShiftSimulator.Clinical
{
    public enum ClinicalExamStatus
    {
        Ordered,
        WaitingCollection,
        Processing,
        Completed
    }

    public sealed class ClinicalExamDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public double CollectionMinutes { get; }
        public double ProcessingMinutes { get; }

        public ClinicalExamDefinition(string id, string displayName, double collectionMinutes, double processingMinutes)
        {
            Id = id;
            DisplayName = displayName;
            CollectionMinutes = Math.Max(0.1, collectionMinutes);
            ProcessingMinutes = Math.Max(0.1, processingMinutes);
        }
    }

    public static class ClinicalExamCatalog
    {
        private static readonly IReadOnlyList<ClinicalExamDefinition> Definitions =
            new List<ClinicalExamDefinition>
            {
                new ClinicalExamDefinition("hemograma", "Hemograma", 5, 10),
                new ClinicalExamDefinition("sodio", "Sódio", 5, 15),
                new ClinicalExamDefinition("potassio", "Potássio", 5, 15),
                new ClinicalExamDefinition("creatinina", "Creatinina", 5, 15),
                new ClinicalExamDefinition("ureia", "Ureia", 5, 15),
                new ClinicalExamDefinition("glicemia", "Glicemia", 5, 10),
                new ClinicalExamDefinition("lactato", "Lactato", 2, 3),
                new ClinicalExamDefinition("gasometria_arterial", "Gasometria arterial", 2, 3),
                new ClinicalExamDefinition("pcr", "PCR", 10, 30),
                new ClinicalExamDefinition("hemoculturas", "Hemoculturas", 15, 120)
            };

        public static IReadOnlyList<ClinicalExamDefinition> All => Definitions;

        public static ClinicalExamDefinition Find(string id)
        {
            for (int i = 0; i < Definitions.Count; i++)
            {
                if (string.Equals(Definitions[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return Definitions[i];
            }

            return null;
        }
    }

    public sealed class ClinicalExamResult
    {
        public string ExamId { get; }
        public string DisplayValue { get; }
        public string Unit { get; }
        public string ReferenceRange { get; }
        
        public double? NumericValue { get; }
public bool IsAbnormal { get; }

public ClinicalExamResult(string examId, string displayValue, string unit, string referenceRange, bool isAbnormal)
        {
            ExamId = examId;
            DisplayValue = displayValue;
            Unit = unit;
            ReferenceRange = referenceRange;
            IsAbnormal = isAbnormal;
        }

        public ClinicalExamResult(string examId, string displayValue, string unit, string referenceRange, bool isAbnormal, double numericValue)
            : this(examId, displayValue, unit, referenceRange, isAbnormal)
        {
            NumericValue = numericValue;
        }
    }

    public sealed class PatientExamOrder
    {
        public ClinicalExamDefinition Definition { get; }
        public PatientState Patient { get; }
        public double OrderedAtMinutes { get; }
        public double CollectionDueAtMinutes { get; internal set; }
        public double ResultDueAtMinutes { get; internal set; }
        public bool CollectionStarted { get; internal set; }
        public ClinicalExamStatus Status { get; internal set; }
        public ClinicalExamResult Result { get; internal set; }

        internal PatientExamOrder(ClinicalExamDefinition definition, PatientState patient, double orderedAtMinutes)
        {
            Definition = definition;
            Patient = patient;
            OrderedAtMinutes = orderedAtMinutes;
            CollectionDueAtMinutes = 0;
            ResultDueAtMinutes = 0;
            CollectionStarted = false;
            Status = ClinicalExamStatus.Ordered;
        }
    }
}
