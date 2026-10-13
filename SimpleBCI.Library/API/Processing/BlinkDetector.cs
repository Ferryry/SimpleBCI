using brainflow;
using SimpleBCI.Library.API.States;

namespace SimpleBCI.Library.API.Processing
{
    /// <summary>
    /// Detects single and double blink events by analyzing EEG signals
    /// from the AF7 and AF8 channels using BrainFlow.
    /// </summary>
    internal class BlinkDetector
    {
        private readonly BoardShim board;
        private readonly int samplingRate;
        private readonly int[] eegChannels;
        private readonly int timestampChannel;

        private double lastProcessedTimestamp = double.NegativeInfinity;
        private double? previousBlinkTimestamp;
        private const double BlinkThreshold = 200.0;
        private const double MinBlinkInterval = 0.2;
        private const double MaxBlinkDuration = 0.3;
        private const double MinBlinkDuration = 0.05;
        private const double DoubleBlinkWindow = 1.2;
        private const double ChannelMergeWindow = 0.1;

        /// <summary>
        /// Initializes the blink detector using the specified BrainFlow board
        /// and retrieves its sampling rate, EEG channels and timestamp channel.
        /// </summary>
        /// <param name="board">
        /// The initialized BrainFlow board providing EEG data.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="board"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the board does not provide enough EEG channels.
        /// </exception>
        public BlinkDetector(BoardShim board)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            this.board = board;

            int boardId = board.get_board_id();
            int preset = (int)BrainFlowPresets.DEFAULT_PRESET;

            samplingRate = BoardShim.get_sampling_rate(boardId, preset);
            eegChannels = BoardShim.get_eeg_channels(boardId, preset);
            timestampChannel = BoardShim.get_timestamp_channel(boardId, preset);

            if (eegChannels.Length < 3)
            {
                throw new InvalidOperationException("The board must provide EEG channels for AF7 and AF8.");
            }
        }

        /// <summary>
        /// Analyzes the latest EEG data to detect blink events and determine
        /// whether a single blink or a double blink has occurred.
        /// </summary>
        /// <returns>
        /// The current blink state: NotBlinked, Blinked or DoubleBlinked.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the required EEG or timestamp channels are missing
        /// from the retrieved data.
        /// </exception>
        public BlinkState GetCurrentBlinkState()
        {
            int preset = (int)BrainFlowPresets.DEFAULT_PRESET;
            double[,] data = board.get_current_board_data(MotorImageryFeatureExtractor.windowSize, preset);
            int sampleCount = data.GetLength(1);

            if (sampleCount == 0)
            {
                return BlinkState.NotBlinked;
            }

            int af7Row = eegChannels[1];
            int af8Row = eegChannels[2];
            int requiredRow = Math.Max(timestampChannel, Math.Max(af7Row, af8Row));
            if (data.GetLength(0) <= requiredRow)
            {
                throw new InvalidOperationException("EEG channels or timestamp channel are not in the data array.");
            }

            double[] af7 = new double[sampleCount];
            double[] af8 = new double[sampleCount];
            double[] timestamps = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                af7[i] = data[af7Row, i];
                af8[i] = data[af8Row, i];
                timestamps[i] = data[timestampChannel, i];
            }

            // Process this sliding window only once. Keep a blink timestamp
            // across calls so a double blink can straddle two windows.
            List<int> peaks = FindBlinkEvents(af7, af8);
            bool detectedBlink = false;
            bool detectedDoubleBlink = false;

            foreach (int peak in peaks)
            {
                double blinkTimestamp = timestamps[peak];
                if (blinkTimestamp <= lastProcessedTimestamp)
                {
                    continue;
                }

                detectedBlink = true;
                if (previousBlinkTimestamp.HasValue)
                {
                    double interval = blinkTimestamp - previousBlinkTimestamp.Value;
                    if (interval >= MinBlinkInterval && interval <= DoubleBlinkWindow)
                    {
                        detectedDoubleBlink = true;
                    }
                }

                previousBlinkTimestamp = blinkTimestamp;
            }

            lastProcessedTimestamp = Math.Max(lastProcessedTimestamp, timestamps[^1]);

            if (detectedDoubleBlink)
            {
                previousBlinkTimestamp = null;
                return BlinkState.DoubleBlinked;
            }

            if (detectedBlink)
            {
                return BlinkState.Blinked;
            }

            return BlinkState.NotBlinked;
        }

        /// <summary>
        /// Detects blink events by correcting the moving baseline of the AF7
        /// and AF8 signals, finding negative peaks in both channels, and
        /// merging peaks that occur close together.
        /// </summary>
        /// <param name="af7">The EEG signal from the AF7 channel.</param>
        /// <param name="af8">The EEG signal from the AF8 channel.</param>
        /// <returns>
        /// A sorted list of sample indices representing detected blink events.
        /// </returns>
        private List<int> FindBlinkEvents(double[] af7, double[] af8)
        {
            double[] correctedAf7 = RemoveMovingBaseline(af7);
            double[] correctedAf8 = RemoveMovingBaseline(af8);
            List<int> channelPeaks = new();

            FindNegativePeaks(correctedAf7, channelPeaks);
            FindNegativePeaks(correctedAf8, channelPeaks);
            channelPeaks.Sort();

            return MergeNearbyPeaks(channelPeaks);
        }

        /// <summary>
        /// Removes moving baseline. 
        /// </summary>
        /// <param name="signal">raw eeg data.</param>
        /// <returns>Corrected raw eeg data.</returns>
        private double[] RemoveMovingBaseline(double[] signal)
        {
            // A centered half-second baseline window removes slow drift. The
            // threshold still probably needs calibration for the board's units/setup.
            int radius = Math.Max(1, samplingRate / 4);
            double[] corrected = new double[signal.Length];

            for (int i = 0; i < signal.Length; i++)
            {
                int start = Math.Max(0, i - radius);
                int end = Math.Min(signal.Length - 1, i + radius);
                double sum = 0;

                for (int j = start; j <= end; j++)
                {
                    sum += signal[j];
                }

                corrected[i] = signal[i] - sum / (end - start + 1);
            }

            return corrected;
        }

        /// <summary>
        /// Detects negative peaks in an EEG signal and accepts only those
        /// that satisfy the configured amplitude, distance and duration thresholds.
        /// </summary>
        /// <param name="signal">
        /// The baseline-corrected EEG signal to analyze.
        /// </param>
        /// <param name="peaks">
        /// The list to which the indices of accepted peaks are added.
        /// </param>
        private void FindNegativePeaks(double[] signal, List<int> peaks)
        {
            int minWidth = (int)Math.Ceiling(samplingRate * MinBlinkDuration);
            int maxWidth = (int)Math.Floor(samplingRate * MaxBlinkDuration);
            int minDistance = (int)Math.Ceiling(samplingRate * MinBlinkInterval);
            int lastAcceptedPeak = -minDistance;

            for (int i = 1; i < signal.Length - 1; i++)
            {
                bool isLocalMinimum = signal[i] < signal[i - 1] && signal[i] <= signal[i + 1];
                if (!isLocalMinimum || Math.Abs(signal[i]) < BlinkThreshold)
                {
                    continue;
                }

                if (i - lastAcceptedPeak < minDistance)
                {
                    continue;
                }

                double halfAmplitude = signal[i] / 2.0;
                int left = i;
                int right = i;

                while (left > 0 && signal[left] < halfAmplitude)
                {
                    left--;
                }

                while (right < signal.Length - 1 && signal[right] < halfAmplitude)
                {
                    right++;
                }

                int width = right - left;
                if (width < minWidth || width > maxWidth)
                {
                    continue;
                }

                peaks.Add(i);
                lastAcceptedPeak = i;
            }
        }

        /// <summary>
        /// Filters out peaks that occur within the configured merge distance
        /// of the last accepted peak.
        /// </summary>
        /// <param name="peaks">
        /// A list of peak indices sorted in ascending order.
        /// </param>
        /// <returns>
        /// A list containing only peaks that satisfy the minimum distance
        /// requirement.
        /// </returns>
        private List<int> MergeNearbyPeaks(List<int> peaks)
        {
            List<int> result = new();
            int mergeDistance = (int)Math.Ceiling(samplingRate * ChannelMergeWindow);

            foreach (int peak in peaks)
            {
                if (result.Count == 0 || peak - result[^1] > mergeDistance)
                {
                    result.Add(peak);
                }
            }

            return result;
        }
    }
}