using brainflow;

namespace SimpleBCI.API.Processing.Helper
{
    static class WaveExtractor
    {
        public static double[] ExtractAlpha(double[] raw, int samplingRate)
            => ExtractBand(raw, samplingRate, 8.0, 13.0);

        public static double[] ExtractBeta(double[] raw, int samplingRate)
            => ExtractBand(raw, samplingRate, 13.0, 30.0);

        public static double[] ExtractDelta(double[] raw, int samplingRate)
            => ExtractBand(raw, samplingRate, 0.5, 4.0);

        public static double[] ExtractGamma(double[] raw, int samplingRate)
            => ExtractBand(raw, samplingRate, 30.0, 45.0);

        public static double[] ExtractTheta(double[] raw, int samplingRate)
            => ExtractBand(raw, samplingRate, 4.0, 8.0);

        public static double CalculateMeanSquare(double[] extractedSignal)
        {
            if (extractedSignal == null)
            {
                throw new ArgumentNullException(nameof(extractedSignal));
            }

            if (extractedSignal.Length == 0)
            {
                throw new ArgumentException(
                    "Signal is empty.",
                    nameof(extractedSignal));
            }

            double sum = 0.0;

            foreach (double sample in extractedSignal)
            {
                if (!double.IsFinite(sample))
                {
                    throw new ArgumentException(
                        "Signal contains non-finite values.",
                        nameof(extractedSignal));
                }

                sum += sample * sample;
            }

            return sum / extractedSignal.Length;
        }

        private static double[] ExtractBand(
            double[] raw,
            int samplingRate,
            double lowFrequency,
            double highFrequency)
        {
            ArgumentNullException.ThrowIfNull(raw);

            if (raw.Length == 0)
            {
                throw new ArgumentException("EEG data is empty.", nameof(raw));
            }

            if (samplingRate <= 0 || highFrequency >= samplingRate / 2.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(samplingRate),
                    "Sampling rate must support the requested frequency band.");
            }

            double[] filtered = (double[])raw.Clone();

            DataFilter.perform_bandpass(
                filtered,
                samplingRate,
                (float)((lowFrequency + highFrequency) / 2.0),
                (float)((highFrequency - lowFrequency) / 2.0),
                4,
                (int)FilterTypes.BUTTERWORTH,
                0.0);

            return filtered;
        }
    }
}
