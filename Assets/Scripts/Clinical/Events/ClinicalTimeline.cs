using System;
using System.Collections.Generic;

namespace MedicalShiftSimulator.Clinical
{
    public sealed class ClinicalTimelineEntry
    {
        public double SimulationTimeMinutes { get; }
        public string EventType { get; }
        public string Description { get; }

        public ClinicalTimelineEntry(double simulationTimeMinutes, string eventType, string description)
        {
            SimulationTimeMinutes = simulationTimeMinutes;
            EventType = eventType;
            Description = description;
        }
    }

    public sealed class ClinicalTimeline
    {
        private readonly List<ClinicalTimelineEntry> _entries = new List<ClinicalTimelineEntry>();
        public IReadOnlyList<ClinicalTimelineEntry> Entries => _entries;

        public void Add(double simulationTimeMinutes, string eventType, string description)
        {
            _entries.Add(new ClinicalTimelineEntry(simulationTimeMinutes, eventType, description));
        }
    }
}
