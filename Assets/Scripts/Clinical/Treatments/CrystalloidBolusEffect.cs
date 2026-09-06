using System;

namespace MedicalShiftSimulator.Clinical
{
    /// <summary>
    /// A single crystalloid bolus infused over a fixed duration. Its effect on
    /// circulating volume scales with total volume and patient weight (see
    /// ClinicalFormulas.CrystalloidVolumeEffect). Finishes and removes itself
    /// once fully infused - the controller may hold several of these at once if
    /// multiple boluses are given in succession.
    /// </summary>
    public class CrystalloidBolusEffect : TreatmentEffect
    {
        public override string Name => "Cristaloide";

        private readonly double _totalVolumeMl;
        private readonly double _durationHours;
        private double _volumeRemainingMl;

        public CrystalloidBolusEffect(double totalVolumeMl, double durationMinutes = 20)
        {
            _totalVolumeMl = totalVolumeMl;
            _volumeRemainingMl = totalVolumeMl;
            _durationHours = Math.Max(durationMinutes, 1) / 60.0;
        }

        public override void Apply(PatientState patient, double deltaHours, double simulationTimeMinutes)
        {
            if (_volumeRemainingMl <= 0)
            {
                IsActive = false;
                return;
            }

            double rateMlPerHour = _totalVolumeMl / _durationHours;
            double infusedThisTick = Math.Min(_volumeRemainingMl, rateMlPerHour * deltaHours);
            _volumeRemainingMl -= infusedThisTick;

            double deltaVolume = ClinicalFormulas.CrystalloidVolumeEffect(infusedThisTick, patient.WeightKg);
            patient.InfuseVolume(deltaVolume);

            if (_volumeRemainingMl <= 0.001)
            {
                IsActive = false;
            }
        }
    }
}
