using brainflow;

namespace SimpleBCI.API.Processing
{
    /// <summary>
    /// Builds the feature vector expected by SimpleBCI.ML/train.py.
    /// Input rows must be ordered TP9, AF7, AF8, TP10.
    /// </summary>
    internal sealed class MotorImageryFeatureExtractor
    {
        public const int channelCount = 4;
        public const int samplingRate = 256;
        public const int windowSize = 512;
        public const int featureCount = 18;
        private const int welchNfft = 256;
        private const int welchOverlap = 128;
        private const int filterOrder = 4;
        private const double epsilon = 1e-12;

        private static readonly (double Low, double High)[] Bands =
        {
            (4.0, 8.0),     // Theta 4-8 Hz
            (8.0, 13.0),    // Alpha 8-13 Hz
            (13.0, 30.0)    // Beta 13-30 Hz
        };

        /// <summary>
        /// Extracts 18 features from a raw EEG window containing 512 samples
        /// per channel, corresponding to a 2-second window at 256 Hz.
        /// </summary>
        /// <param name="eegWindow">
        /// A 2D array containing four EEG channels in the order
        /// TP9, AF7, AF8, TP10, each with 512 samples.
        /// </param>
        /// <returns>
        /// An array containing 18 extracted features, including relative
        /// theta, alpha and beta power and inter-channel log-power ratios.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="eegWindow"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown if the input dimensions are incorrect or the EEG samples
        /// contain non-finite values.
        /// </exception>
        public double[] ExtractFeatures(double[,] eegWindow)
        {
            ArgumentNullException.ThrowIfNull(eegWindow);

            if (eegWindow.GetLength(0) != channelCount ||
                eegWindow.GetLength(1) != windowSize)
            {
                throw new ArgumentException(
                    $"Expected {channelCount} EEG channels with {windowSize} samples each, " +
                    "ordered TP9, AF7, AF8, TP10.",
                    nameof(eegWindow));
            }

            double[,] relativePower = new double[channelCount, Bands.Length];

            for (int channel = 0; channel < channelCount; channel++)
            {
                var signal = new double[windowSize];
                for (int sample = 0; sample < windowSize; sample++)
                {
                    double value = eegWindow[channel, sample];
                    if (!double.IsFinite(value))
                    {
                        throw new ArgumentException(
                            "EEG samples must contain only finite values.",
                            nameof(eegWindow)
                        );
                    }

                    signal[sample] = value;
                }

                // Match preprocess_channel() in train.py.
                signal = DataFilter.detrend(signal, (int)DetrendOperations.CONSTANT);

                signal = DataFilter.perform_bandpass(
                    signal,
                    samplingRate,
                    4.0,
                    30.0,
                    filterOrder,
                    (int)FilterTypes.BUTTERWORTH,
                    0.0
                );

                double totalPower = Math.Max(
                    GetBandPower(signal, 4.0, 30.0),
                    epsilon
                );

                for (int band = 0; band < Bands.Length; band++)
                {
                    var (low, high) = Bands[band];
                    relativePower[channel, band] = GetBandPower(signal, low, high) / totalPower;
                }
            }

            var features = new double[featureCount];
            int index = 0;

            // First 12 values: theta, alpha and beta relative power per channel.
            for (int channel = 0; channel < channelCount; channel++)
            {
                for (int band = 0; band < Bands.Length; band++)
                {
                    features[index++] = relativePower[channel, band];
                }
            }

            // Final six values, matching the actual append order in
            // extract_features() in train.py: both ratios per band.
            for (int band = 0; band < Bands.Length; band++)
            {
                features[index++] = Math.Log(
                    (relativePower[1, band] + epsilon) /
                    (relativePower[2, band] + epsilon));
                features[index++] = Math.Log(
                    (relativePower[0, band] + epsilon) /
                    (relativePower[3, band] + epsilon));
            }

            return features;
        }

        /// <summary>
        /// Calculates the power within a specified frequency band of an EEG signal
        /// using Welch's method to estimate the power spectral density (PSD).
        /// </summary>
        /// <param name="signal">The EEG signal to analyze.</param>
        /// <param name="low">The lower frequency boundary of the band in Hz.</param>
        /// <param name="high">The upper frequency boundary of the band in Hz.</param>
        /// <returns>The estimated power within the specified frequency band.</returns>
        private static double GetBandPower(double[] signal, double low, double high)
        {
            Tuple<double[], double[]> psd = DataFilter.get_psd_welch(
                signal,
                welchNfft,
                welchOverlap,
                samplingRate,
                (int)WindowOperations.HANNING);

            return DataFilter.get_band_power(psd, low, high);
        }
    }
}
