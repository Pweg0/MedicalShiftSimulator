using System;

namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// Tracks elapsed simulated time (in minutes) for a clinical simulation.
    /// Completely decoupled from Unity's Time class - the host application
    /// (a MonoBehaviour, an EditMode test, a future headless batch runner...)
    /// decides how much simulated time a single Advance() call represents.
    /// </summary>
    public class ClinicalClock
    {
        public double TotalSimulatedMinutes { get; private set; }
        public double TotalSimulatedHours => TotalSimulatedMinutes / 60.0;

        /// <summary>Raised every time the clock advances: (totalMinutes, deltaMinutes).</summary>
        public event Action<double, double> Ticked;

        public void Advance(double deltaMinutes)
        {
            if (deltaMinutes <= 0) return;
            TotalSimulatedMinutes += deltaMinutes;
            Ticked?.Invoke(TotalSimulatedMinutes, deltaMinutes);
        }

        public void Reset()
        {
            TotalSimulatedMinutes = 0;
        }
    }
}
