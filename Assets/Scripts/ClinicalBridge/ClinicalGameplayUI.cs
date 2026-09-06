
using UnityEngine;
using MedicalShiftSimulator.Clinical;
namespace MedicalShiftSimulator.ClinicalBridge
{
    public class ClinicalGameplayUI : MonoBehaviour
    {
        private PatientSimulationBehaviour _sim;
        private bool _open;
        private bool _monitorView;
        private int _examTab;
        private GUIStyle _title;
        private GUIStyle _section;

        private void Awake()
        {
            _sim = GetComponent<PatientSimulationBehaviour>();
        }

        public void OpenPatient()
        {
            _open = true;
            _monitorView = false;
            _examTab = 0;
        }

        public void OpenMonitor()
        {
            _open = true;
            _monitorView = true;
            _examTab = 0;
        }

        private void Update()
        {
            if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                _open = false;
        }

        private void OnGUI()
        {
            if (_sim == null || _sim.Controller == null) return;

            if (!_open) return;

            var patient = _sim.Controller.Patient;
            GUILayout.BeginArea(new Rect(390, 18, 470, 700), GUI.skin.box);
            GUILayout.Label(_monitorView ? "MONITOR CLÍNICO" : "AVALIAÇÃO CLÍNICA", Title);
            GUILayout.Label($"{patient.Name} | {(patient.IsAlive ? "Paciente vivo" : "Paciente em óbito")}");
            GUILayout.Label("ESC - fechar");
            GUILayout.Space(8);

            if (!_monitorView)
            {
                GUILayout.Label("Identificação e história", Section);
                GUILayout.Label($"Paciente: {patient.Name}");
                GUILayout.Label($"Queixa: {patient.Complaint}");
                GUILayout.Label($"História: {patient.HistorySummary}");
                GUILayout.Space(8);
            }

            DrawVitals(patient);

            if (!_monitorView)
            {
                GUILayout.Space(8);
                GUILayout.Label("Exame físico", Section);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Geral")) _examTab = 0;
                if (GUILayout.Button("Cardiovascular")) _examTab = 1;
                if (GUILayout.Button("Respiratório")) _examTab = 2;
                if (GUILayout.Button("Perfusão")) _examTab = 3;
                if (GUILayout.Button("Neurológico")) _examTab = 4;
                GUILayout.EndHorizontal();
                GUILayout.Label(ExamFinding(patient, _examTab), GUILayout.Height(45));
            }

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
            GUILayout.Label($"Tempo simulado: {_sim.Controller.Clock.TotalSimulatedMinutes:F1} min | evolução observável pelos sinais e achados");

            GUILayout.EndArea();
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
