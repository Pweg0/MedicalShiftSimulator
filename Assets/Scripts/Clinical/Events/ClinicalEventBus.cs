using System;
using System.Collections.Generic;

namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Minimal publish/subscribe bus for clinical events. One instance per
    /// patient simulation (owned by its PatientSimulationController) - not a
    /// static singleton, so multiple patients can each have an isolated event
    /// stream once the simulator supports more than one bed.
    /// </summary>
    public class ClinicalEventBus
    {
        private readonly List<Action<ClinicalEvent>> _subscribers = new List<Action<ClinicalEvent>>();

        public void Subscribe(Action<ClinicalEvent> handler)
        {
            if (handler != null) _subscribers.Add(handler);
        }

        public void Unsubscribe(Action<ClinicalEvent> handler)
        {
            _subscribers.Remove(handler);
        }

        public void Publish(ClinicalEvent evt)
        {
            if (evt == null) return;
            var snapshot = _subscribers.ToArray();
            foreach (var handler in snapshot)
            {
                handler(evt);
            }
        }
    }
}
