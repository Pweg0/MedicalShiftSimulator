using System;
using System.Collections.Generic;

namespace MedicalShiftSimulator.Clinical
{
    public sealed class PatientExamService
    {
        private readonly List<PatientExamOrder> _orders = new List<PatientExamOrder>();
        private readonly ClinicalTimeline _timeline;

        public PatientState Patient { get; }
        public ClinicalClock Clock { get; }
        public IReadOnlyList<PatientExamOrder> Orders => _orders;

        public PatientExamService(PatientState patient, ClinicalClock clock, ClinicalTimeline timeline)
        {
            Patient = patient ?? throw new ArgumentNullException(nameof(patient));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        }

        public PatientExamOrder Order(string examId)
        {
            var order = CreateOrder(examId);
            BeginCollection(order, false);
            _timeline.Add(Clock.TotalSimulatedMinutes, "Exame solicitado", order.Definition.DisplayName + " solicitado");
            return order;
        }

        public PatientExamOrder OrderPendingCollection(string examId)
        {
            return CreateOrder(examId);
        }

        public void BeginCollection(PatientExamOrder order, bool markWaitingStatus = true)
        {
            if (order == null || order.Patient != Patient || order.CollectionStarted || order.Status == ClinicalExamStatus.Completed)
                return;

            order.CollectionStarted = true;
            order.CollectionDueAtMinutes = Clock.TotalSimulatedMinutes + order.Definition.CollectionMinutes;
            order.ResultDueAtMinutes = order.CollectionDueAtMinutes + order.Definition.ProcessingMinutes;
            if (markWaitingStatus) order.Status = ClinicalExamStatus.WaitingCollection;
            _timeline.Add(Clock.TotalSimulatedMinutes, "Coleta iniciada", order.Definition.DisplayName);
        }



        public PatientExamOrder GetLatest(string examId)
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_orders[i].Definition.Id, examId, StringComparison.OrdinalIgnoreCase))
                    return _orders[i];
            }

            return null;
        }

        public void Advance()
        {
            for (int i = 0; i < _orders.Count; i++)
            {
                var order = _orders[i];
                if (!order.CollectionStarted || order.Status == ClinicalExamStatus.Completed) continue;

                if (order.Status == ClinicalExamStatus.Ordered)
                    order.Status = ClinicalExamStatus.WaitingCollection;

                if (Clock.TotalSimulatedMinutes >= order.CollectionDueAtMinutes)
                    order.Status = ClinicalExamStatus.Processing;

                if (Clock.TotalSimulatedMinutes >= order.ResultDueAtMinutes)
                {
                    order.Status = ClinicalExamStatus.Completed;
                    order.Result = BuildResult(order.Definition.Id, order.Patient);
                    _timeline.Add(Clock.TotalSimulatedMinutes, "Resultado liberado", order.Definition.DisplayName + ": " + order.Result.DisplayValue + " " + order.Result.Unit);
                }
            }
        }

        private ClinicalExamResult BuildResult(string examId, PatientState patient)
        {
            var v = patient.Vitals;
            double perfusion = patient.EffectivePerfusion;
            double infection = patient.InfectionBurden;

            switch (examId)
            {
                case "hemograma":
                    double leukocytes = 7.0 + infection * 18.0;
                    return new ClinicalExamResult(examId, leukocytes.ToString("F1"), "mil/µL", "4,0-10,0", leukocytes < 4.0 || leukocytes > 10.0);
                case "sodio":
                    double sodium = 140.0 - Math.Max(0.0, 1.0 - patient.IntravascularVolume) * 4.0;
                    return new ClinicalExamResult(examId, sodium.ToString("F0"), "mmol/L", "135-145", sodium < 135 || sodium > 145);
                case "potassio":
                    double potassium = 4.0 + Math.Max(0.0, 1.0 - perfusion) * 0.8;
                    return new ClinicalExamResult(examId, potassium.ToString("F1"), "mmol/L", "3,5-5,0", potassium < 3.5 || potassium > 5.0);
                case "creatinina":
                    // Provisional renal proxy: no renal compartment exists yet, so low effective perfusion is used without mutating physiology.
                    double creatinine = 1.0 + Math.Max(0.0, 1.0 - perfusion) * 3.0 + Math.Max(0.0, 1.0 - v.MeanArterialPressure / 65.0) * 1.0;
                    return new ClinicalExamResult(examId, creatinine.ToString("F1"), "mg/dL", "0,6-1,3", creatinine > 1.3);
                case "ureia":
                    double urea = 30.0 + Math.Max(0.0, 1.0 - perfusion) * 60.0;
                    return new ClinicalExamResult(examId, urea.ToString("F0"), "mg/dL", "10-50", urea > 50.0);
                case "glicemia":
                    double glucose = 90.0 + infection * 35.0 + Math.Max(0.0, v.Temperature - 37.0) * 8.0;
                    return new ClinicalExamResult(examId, glucose.ToString("F0"), "mg/dL", "70-99", glucose < 70 || glucose > 99);
                case "lactato":
                    return new ClinicalExamResult(examId, v.Lactate.ToString("F1"), "mmol/L", "0,5-2,0", v.Lactate > 2.0, v.Lactate);
                case "gasometria_arterial":
                    double pH = 7.40 - Math.Max(0.0, v.Lactate - 2.0) * 0.05;
                    double pco2 = 40.0 + Math.Max(0.0, v.RespiratoryRate - 16.0) * 0.5;
                    double po2 = v.SpO2;
                    string gas = "pH " + pH.ToString("F2") + " | PaCO2 " + pco2.ToString("F0") + " | PaO2 " + po2.ToString("F0");
                    return new ClinicalExamResult(examId, gas, "mmHg", "pH 7,35-7,45", pH < 7.35 || pH > 7.45 || po2 < 80.0);
                case "pcr":
                    double crp = 2.0 + infection * 180.0;
                    return new ClinicalExamResult(examId, crp.ToString("F0"), "mg/L", "<5", crp > 5.0);
                case "hemoculturas":
                    string culture = infection > 0.20 ? "Crescimento em investigação" : "Sem crescimento até o momento";
                    return new ClinicalExamResult(examId, culture, "", "Sem crescimento", infection > 0.20);
                default:
                    throw new InvalidOperationException("Exame sem gerador de resultado: " + examId);
            }
        }
        private PatientExamOrder CreateOrder(string examId)
        {
            var definition = ClinicalExamCatalog.Find(examId);
            if (definition == null) throw new ArgumentException("Exame não cadastrado: " + examId, nameof(examId));
            var order = new PatientExamOrder(definition, Patient, Clock.TotalSimulatedMinutes);
            _orders.Add(order);
            return order;
        }
    }
}
