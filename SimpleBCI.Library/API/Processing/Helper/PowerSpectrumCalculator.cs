using brainflow;

namespace SimpleBCI.API.Processing.Helper
{
    internal static class PowerSpectrumCalculator
    {
        public static double CalculateBandPower(
            (double[] Frequencies, double[] Power) powerSpectrum,
            double startFrequency,
            double endFrequency)
        {
            ArgumentNullException.ThrowIfNull(powerSpectrum.Frequencies);
            ArgumentNullException.ThrowIfNull(powerSpectrum.Power);

            if (startFrequency < 0 || endFrequency <= startFrequency)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endFrequency),
                    "The frequency range must be non-negative and end above its start.");
            }

            return DataFilter.get_band_power(
                powerSpectrum.ToTuple(),
                startFrequency,
                endFrequency);
        }
    }
}
