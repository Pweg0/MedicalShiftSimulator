

using System;
using UnityEngine;
using MedicalShiftSimulator.Clinical;
namespace MedicalShiftSimulator.ClinicalBridge
{
    public class ClinicalGameplayUI : MonoBehaviour
    {
        private PatientSimulationBehaviour _sim;
        private bool _open;
        private bool _monitorView;
        
        private int _mainTab;
        private int _examTab;
        private GUIStyle _title;
        
        private Vector2 _timelineScroll;
        private GUIStyle _section;

        private void Awake()
        {
            _sim = GetComponent<PatientSimulationBehaviour>();
        }

        public void OpenPatient()
        {
            _open = true;
            _monitorView = false;
            _mainTab = 0;
            _examTab = 0;
            _sim.Controller.RecordEvaluation();
        }

        public void OpenMonitor()
        {
            _open = true;
            _monitorView = true;
            _examTab = 0;
        }

        private void Update()
        {
        }

        private void OnGUI()
        {
            if (_sim == null || _sim.Controller == null) return;
            if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                _open = false;
                return;
            }
            if (!_open) return;

            var patient = _sim.Controller.Patient;
            GUILayout.BeginArea(new Rect(390, 18, 470, 700), GUI.skin.box);
            GUILayout.Label(_monitorView ? "MONITOR CLÍNICO" : "PRONTUÁRIO CLÍNICO", Title);
            GUILayout.Label($"{patient.Name} | {(patient.IsAlive ? "Paciente vivo" : "Paciente em óbito")}");
            GUILayout.Label("ESC - fechar");
            GUILayout.Space(8);

            if (!_monitorView)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Avaliação")) _mainTab = 0;
                if (GUILayout.Button("Exames")) _mainTab = 1;
                if (GUILayout.Button("Histórico")) _mainTab = 2;
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }

            if (_monitorView)
            {
                DrawVitals(patient);
                DrawInterventionsAndTime();
            }
            else if (_mainTab == 0)
            {
                DrawAssessment(patient);
            }
            else if (_mainTab == 1)
            {
                DrawExams();
            }
            else
            {
                DrawTimeline();
            }

            GUILayout.EndArea();
        }

        private void DrawAssessment(PatientState patient)
        {
            GUILayout.Label("Identificação e história", Section);
            GUILayout.Label($"Paciente: {patient.Name}");
            GUILayout.Label($"Queixa: {patient.Complaint}");
            GUILayout.Label($"História: {patient.HistorySummary}");
            GUILayout.Space(8);

            DrawVitals(patient);

            GUILayout.Space(8);
            GUILayout.Label("Exame físico", Section);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Geral")) { _examTab = 0; _sim.Controller.RecordPhysicalExam("Estado geral", ExamFinding(patient, 0)); }
            if (GUILayout.Button("Cardiovascular")) { _examTab = 1; _sim.Controller.RecordPhysicalExam("Cardiovascular", ExamFinding(patient, 1)); }
            if (GUILayout.Button("Respiratório")) { _examTab = 2; _sim.Controller.RecordPhysicalExam("Respiratório", ExamFinding(patient, 2)); }
            if (GUILayout.Button("Perfusão")) { _examTab = 3; _sim.Controller.RecordPhysicalExam("Perfusão periférica", ExamFinding(patient, 3)); }
            if (GUILayout.Button("Neurológico")) { _examTab = 4; _sim.Controller.RecordPhysicalExam("Neurológico", ExamFinding(patient, 4)); }
            GUILayout.EndHorizontal();
            GUILayout.Label(ExamFinding(patient, _examTab), GUILayout.Height(45));
            DrawInterventionsAndTime();
        }

        private void DrawExams()
        {
            GUILayout.Label("Solicitação de exames", Section);
            GUILayout.Label("Os tempos são simulados; resultados não são instantâneos.");
            GUILayout.Space(4);

            for (int i = 0; i < ClinicalExamCatalog.All.Count; i++)
            {
                var definition = ClinicalExamCatalog.All[i];
                var order = _sim.Controller.Exams.GetLatest(definition.Id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(definition.DisplayName, GUILayout.Width(145));
                if (order == null)
                {
                    GUILayout.Label("Não solicitado", GUILayout.Width(150));
                }
                else if (order.Status == ClinicalExamStatus.Completed)
                {
                    string altered = order.Result.IsAbnormal ? " [alterado]" : "";
                    GUILayout.Label("Resultado disponível", GUILayout.Width(150));
                    GUILayout.Label(order.Result.DisplayValue + " " + order.Result.Unit + altered, GUILayout.Width(125));
                }
                else
                {
                    GUILayout.Label(StatusText(order.Status), GUILayout.Width(150));
                    GUILayout.Label("T-" + Math.Max(0.0, order.ResultDueAtMinutes - _sim.Controller.Clock.TotalSimulatedMinutes).ToString("F0") + " min", GUILayout.Width(55));
                }

                bool canOrder = order == null || order.Status == ClinicalExamStatus.Completed;
                GUI.enabled = canOrder;
                if (GUILayout.Button("Solicitar", GUILayout.Width(75)))
                    _sim.Controller.OrderExam(definition.Id);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("A interpretação permanece com o jogador; a UI não classifica diagnóstico ou conduta.");
            DrawInterventionsAndTime();
        }

        private void DrawTimeline()
        {
            GUILayout.Label("Linha do tempo clínica", Section);
            GUILayout.Label("Eventos deste paciente, em tempo simulado.");
            GUILayout.Space(4);
            GUILayout.BeginScrollView(_timelineScroll, GUILayout.Height(470));
            var entries = _sim.Controller.Timeline.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                GUILayout.Label(FormatTimelineTime(entry.SimulationTimeMinutes) + " - " + entry.EventType + ": " + entry.Description);
            }
            GUILayout.EndScrollView();
            DrawInterventionsAndTime();
        }

        private void DrawInterventionsAndTime()
        {
            GUILayout.Space(8);
            GUILayout.Label("Condutas", Section);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Oxigênio"))
                _sim.Controller.SetOxygenLevel(Mathf.Min(_sim.Controller.Oxygen.Level + 1, 2));
            if (GUILayout.Button("Cristaloide 500 mL"))
                _sim.Controller.GiveCrystalloidBolus(500);
            if (GUILayout.Button("Noradrenalina +0,05"))
                _sim.Controller.TitrateNorepinephrine(0.05);
            if (GUILayout.Button("Antibiótico"))
                _sim.Controller.GiveAntibiotic();
            GUILayout.EndHorizontal();
            GUILayout.Label($"Oxigênio: nível {_sim.Controller.Oxygen.Level} | Noradrenalina: {_sim.Controller.Norepinephrine.Dose:F2} mcg/kg/min");

            GUILayout.Space(8);
            GUILayout.Label("Tempo", Section);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_sim.IsPaused ? "Retomar" : "Pausar")) _sim.SetPaused(!_sim.IsPaused);
            if (GUILayout.Button("x1")) _sim.SetTimeScale(1);
            if (GUILayout.Button("x5")) _sim.SetTimeScale(5);
            if (GUILayout.Button("x20")) _sim.SetTimeScale(20);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Tempo simulado: {_sim.Controller.Clock.TotalSimulatedMinutes:F1} min");
        }

        private string StatusText(ClinicalExamStatus status)
        {
            switch (status)
            {
                case ClinicalExamStatus.Ordered: return "Solicitado";
                case ClinicalExamStatus.WaitingCollection: return "Aguardando coleta";
                case ClinicalExamStatus.Processing: return "Em processamento";
                default: return "Resultado disponível";
            }
        }

        private string FormatTimelineTime(double simulationMinutes)
        {
            int totalMinutes = Mathf.Max(0, Mathf.FloorToInt((float)simulationMinutes));
            return (totalMinutes / 60).ToString("00") + ":" + (totalMinutes % 60).ToString("00");
        }






        

        private void DrawVitals(PatientState patient)
        {
            var v = patient.Vitals;
            GUILayout.Label("Sinais vitais atuais", Section);
            GUILayout.Label($"FC: {v.HeartRate:F0} bpm    PA: {v.SystolicBP:F0}/{v.DiastolicBP:F0} mmHg    PAM: {v.MeanArterialPressure:F0} mmHg");
            GUILayout.Label($"FR: {v.RespiratoryRate:F0} irpm    SpO₂: {v.SpO2:F0}%    Temperatura: {v.Temperature:F1} °C");
            GUILayout.Label($"Consciência: {v.Consciousness}");
        }

        private string ExamFinding(PatientState patient, int tab)
        {
            var v = patient.Vitals;
            switch (tab)
            {
                case 0:
                    return patient.IsAlive ? $"Estado geral: paciente com aspecto séptico; temperatura {v.Temperature:F1} °C e consciência {v.Consciousness}." : "Estado geral: paciente sem sinais vitais efetivos.";
                case 1:
                    return $"Cardiovascular: FC {v.HeartRate:F0} bpm, PA {v.SystolicBP:F0}/{v.DiastolicBP:F0} mmHg, PAM {v.MeanArterialPressure:F0} mmHg.";
                case 2:
                    return $"Respiratório: FR {v.RespiratoryRate:F0} irpm, SpO₂ {v.SpO2:F0}%; resposta coerente com o suporte de oxigênio atual.";
                case 3:
                    return patient.EffectivePerfusion < 0.65 ? "Perfusão periférica ruim: extremidades frias, enchimento capilar prolongado e pulsos periféricos reduzidos." : patient.EffectivePerfusion < 0.85 ? "Perfusão periférica limítrofe: extremidades frias e enchimento capilar discretamente prolongado." : "Perfusão periférica preservada: extremidades aquecidas e enchimento capilar adequado.";
                default:
                    return $"Neurológico: nível de consciência {v.Consciousness}, derivado da perfusão efetiva e da oxigenação.";
            }
        }

        private GUIStyle Title
        {
            get
            {
                if (_title == null) _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                return _title;
            }
        }

        private GUIStyle Section
        {
            get
            {
                if (_section == null) _section = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                return _section;
            }
        }
    }
}
