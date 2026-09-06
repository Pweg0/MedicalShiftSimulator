using System.Linq;


using UnityEngine;
using MedicalShiftSimulator.Clinical;

namespace MedicalShiftSimulator.ClinicalBridge
{
    /// <summary>
    /// Minimal always-on IMGUI debug panel for validating the clinical
    /// simulation: shows live vitals and exposes buttons for the four MVP
    /// interventions (crystalloid, oxygen, norepinephrine, antibiotic). This is
    /// NOT the final medical UI - just enough to poke the simulation and watch
    /// it respond.
    /// </summary>
    [RequireComponent(typeof(PatientSimulationBehaviour))]
    public class ClinicalDebugUI : MonoBehaviour
    {
        private bool _visible;
        private PatientSimulationBehaviour _sim;
        private string _eventLog = "";
        private const int MaxLogLines = 8;
        private Vector2 _scroll;

        private void Awake()
        {
            _sim = GetComponent<PatientSimulationBehaviour>();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            // Awake execution order between components isn't guaranteed, so make
            // sure we end up subscribed even if OnEnable ran before the
            // controller existed.
            TrySubscribe();
        }

        private bool _subscribed;
        private void TrySubscribe()
        {
            if (_subscribed || _sim == null || _sim.Controller == null) return;
            _sim.Controller.Events.Subscribe(OnClinicalEvent);
            _subscribed = true;
        }

        private void OnClinicalEvent(ClinicalEvent evt)
        {
            string line = null;
            if (evt is ThresholdCrossedEvent t)
                line = $"[{t.SimulationTimeMinutes:F0}min] {(t.Entered ? "ENTROU EM" : "SAIU DE")} {t.ThresholdName}";
            else if (evt is InterventionAppliedEvent i)
                line = $"[{i.SimulationTimeMinutes:F0}min] {i.InterventionName}: {i.Details}";
            else if (evt is PatientDiedEvent d)
                line = $"[{d.SimulationTimeMinutes:F0}min] ÓBITO: {d.Cause}";

            if (line == null) return;

            var lines = (_eventLog + "\n" + line).Split('\n').Where(l => l.Length > 0).ToArray();
            _eventLog = string.Join("\n", lines.Skip(System.Math.Max(0, lines.Length - MaxLogLines)));
        }

        private GUIStyle _boldStyle;
        private GUIStyle Bold
        {
            get
            {
                if (_boldStyle == null)
                {
                    _boldStyle = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold };
                }
                return _boldStyle;
            }
        }
        
            private void OnGUI()
        {
            if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F3) _visible = !_visible;
            if (!_visible) return;
            TrySubscribe();
            if (_sim == null || _sim.Controller == null) return;

            var controller = _sim.Controller;
            var p = controller.Patient;
            var v = p.Vitals;

            GUILayout.BeginArea(new Rect(10, 10, 360, 600), GUI.skin.box);
            GUILayout.Label($"{p.Name} ({(p.IsAlive ? "vivo" : "ÓBITO")})", Bold);
            GUILayout.Label($"Tempo simulado: {controller.Clock.TotalSimulatedMinutes:F1} min ({controller.Clock.TotalSimulatedHours:F2} h)");
            GUILayout.Space(6);

            GUILayout.Label($"FC: {v.HeartRate:F0} bpm");
            GUILayout.Label($"PA: {v.SystolicBP:F0}/{v.DiastolicBP:F0} mmHg   (PAM {v.MeanArterialPressure:F0})");
            GUILayout.Label($"FR: {v.RespiratoryRate:F0} irpm");
            GUILayout.Label($"SpO2: {v.SpO2:F0}%");
            GUILayout.Label($"Temperatura: {v.Temperature:F1} °C");
            GUILayout.Label($"Consciência: {v.Consciousness}");
            GUILayout.Label($"Diurese: {v.UrineOutput:F0} mL/h");
            GUILayout.Label($"Lactato: {v.Lactate:F1} mmol/L");
            GUILayout.Space(6);

            GUILayout.Label("Variáveis internas (debug)", Bold);
            GUILayout.Label($"Carga infecciosa: {p.InfectionBurden:F2}");
            GUILayout.Label($"Volume intravascular: {p.IntravascularVolume:F2}");
            GUILayout.Label($"Tônus vascular (RVS relativa): {p.VascularTone:F2}");
            GUILayout.Label($"Perfusão efetiva (fluxo): {p.EffectivePerfusion:F2}");
            GUILayout.Label($"Score de consciência: {p.ConsciousnessScore:F2}");
            GUILayout.Space(10);

            GUILayout.Label("Intervenções", Bold);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cristaloide 500mL")) controller.GiveCrystalloidBolus(500);
            if (GUILayout.Button("Cristaloide 1000mL")) controller.GiveCrystalloidBolus(1000);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"O2: nível {controller.Oxygen.Level}", GUILayout.Width(80));
            if (GUILayout.Button("Ar ambiente")) controller.SetOxygenLevel(0);
            if (GUILayout.Button("Cateter nasal")) controller.SetOxygenLevel(1);
            if (GUILayout.Button("Máscara/AF")) controller.SetOxygenLevel(2);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Noradrenalina: {controller.Norepinephrine.Dose:F2} mcg/kg/min", GUILayout.Width(200));
            if (GUILayout.Button("-0.05")) controller.TitrateNorepinephrine(-0.05);
            if (GUILayout.Button("+0.05")) controller.TitrateNorepinephrine(0.05);
            GUILayout.EndHorizontal();

            if (GUILayout.Button($"Antibiótico (nível de efeito atual: {controller.Antibiotic.CurrentEffectiveLevel:F2})"))
                controller.GiveAntibiotic();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_sim.IsPaused ? "Retomar" : "Pausar")) _sim.SetPaused(!_sim.IsPaused);
            if (GUILayout.Button("Vel. x1")) _sim.SetTimeScale(1);
            if (GUILayout.Button("Vel. x5")) _sim.SetTimeScale(5);
            if (GUILayout.Button("Vel. x20")) _sim.SetTimeScale(20);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("Eventos", Bold);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(100));
            GUILayout.Label(_eventLog);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }
    }
}
