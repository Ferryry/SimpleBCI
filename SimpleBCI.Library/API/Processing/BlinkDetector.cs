using brainflow;
using SimpleBCI.API.States;

namespace SimpleBCI.API.Processing
{
    /// <summary>
    /// Detects single and double blink events by analyzing EEG signals
    /// from the AF7 and AF8 channels using BrainFlow.
    /// </summary>
    internal class BlinkDetector
    {
        private readonly BoardShim board;
        private readonly ImmutableBoardInformation boardInformation;
        private readonly int samplingRate;
        private readonly int[] channels;
        private readonly ElectrodePosition[] selectedChannels;
        private readonly int timestampChannel;

        private double lastProcessedTimestamp = double.NegativeInfinity;
        private double? previousBlinkTimestamp;
        private const double BlinkThreshold = 180.0;
        private const double MinBlinkInterval = 0.1;
        private const double MaxBlinkDuration = 0.3;
        private const double MinBlinkDuration = 0.05;
        private const double DoubleBlinkWindow = 1.2;
        private const double ChannelMergeWindow = 0.1;
        private const double AnalysisWindowSeconds = 2.0;

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
        /// <exception cref="NotSupportedException">
        /// Thrown if the board does not provide AF7 and AF8 EEG channels.
        /// </exception>
        public BlinkDetector(BoardShim board, ImmutableBoardInformation boardInformation)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (boardInformation == null)
            {
                throw new ArgumentNullException(nameof(boardInformation));
            }

            this.board = board;
            this.boardInformation = boardInformation;
            samplingRate = boardInformation.SamplingRate;
            timestampChannel = BoardShim.get_timestamp_channel(
                board.get_board_id(),
                boardInformation.Preset);

            (selectedChannels, channels) = SelectChannels(boardInformation.Channels);
            if (channels.Length == 0)
            {
                throw new NotSupportedException(
                    "Blink detection requires one complete frontal channel pair: FP1/FP2, AF7/AF8, or F7/F8.");
            }
        }

        /// <summary>The frontal electrode pair selected for blink detection.</summary>
        public IReadOnlyList<ElectrodePosition> SelectedChannels => Array.AsReadOnly(selectedChannels);

        private static (ElectrodePosition[] Positions, int[] Indices) SelectChannels(
            IReadOnlyDictionary<ElectrodePosition, int> availableChannels)
        {
            (ElectrodePosition Left, ElectrodePosition Right)[] priorities =
            {
                (ElectrodePosition.Fp1, ElectrodePosition.Fp2),
                (ElectrodePosition.Af7, ElectrodePosition.Af8),
                (ElectrodePosition.F7, ElectrodePosition.F8)
            };

            foreach ((ElectrodePosition left, ElectrodePosition right) in priorities)
            {
                if (availableChannels.TryGetValue(left, out int leftIndex) &&
                    availableChannels.TryGetValue(right, out int rightIndex))
                {
                    return (new[] { left, right }, new[] { leftIndex, rightIndex });
                }
            }

            return (Array.Empty<ElectrodePosition>(), Array.Empty<int>());
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
            int windowSampleCount = (int)Math.Ceiling(samplingRate * AnalysisWindowSeconds);
            double[,] data;
            try
            {
                data = board.get_current_board_data(windowSampleCount, boardInformation.Preset);
            }
            catch (BrainFlowError)
            {
                throw new SimpleBCIException(
                    "Could not read EEG data. Make sure the board has started successfully.");
            }
            int sampleCount = data.GetLength(1);

            if (sampleCount == 0)
            {
                return BlinkState.NotBlinked;
            }

            int requiredRow = Math.Max(timestampChannel, channels.Max());
            if (data.GetLength(0) <= requiredRow)
            {
                throw new InvalidOperationException("EEG channels or timestamp channel are not in the data array.");
            }

            double[][] signals = channels.Select(_ => new double[sampleCount]).ToArray();
            double[] timestamps = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                {
                    signals[channelIndex][i] = data[channels[channelIndex], i];
                }
                timestamps[i] = data[timestampChannel, i];
            }

            // Process this sliding window only once. Keep a blink timestamp
            // across calls so a double blink can straddle two windows.
            List<int> peaks = FindBlinkEvents(signals);
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
        /// Detects positive and negative peaks directly in the selected frontal
        /// channels and merges peaks that occur close together across channels.
        /// </summary>
        /// <param name="signals">The EEG signals from the selected channel pair.</param>
        /// <returns>
        /// A sorted list of sample indices representing detected blink events.
        /// </returns>
        private List<int> FindBlinkEvents(double[][] signals)
        {
            List<BlinkPeak> channelPeaks = new();

            foreach (double[] signal in signals)
            {
                channelPeaks.AddRange(FindPeaks(signal, polarity: 1));
                channelPeaks.AddRange(FindPeaks(signal, polarity: -1));
            }
            channelPeaks.Sort((left, right) => left.Index.CompareTo(right.Index));

            return MergeNearbyPeaks(channelPeaks.Select(peak => peak.Index).ToList());
        }

        /// <summary>
        /// Detects peaks of the requested polarity using amplitude, prominence,
        /// distance and duration thresholds.
        /// </summary>
        private List<BlinkPeak> FindPeaks(double[] signal, int polarity)
        {
            List<BlinkPeak> candidates = new();
            double[] oriented = signal.Select(value => value * polarity).ToArray();
            int minWidth = (int)(samplingRate * MinBlinkDuration);
            int maxWidth = (int)(samplingRate * MaxBlinkDuration);

            int i = 1;
            while (i < oriented.Length - 1)
            {
                if (oriented[i] <= oriented[i - 1])
                {
                    i++;
                    continue;
                }

                int plateauEnd = i;
                while (plateauEnd < oriented.Length - 1 && oriented[plateauEnd + 1] == oriented[i])
                {
                    plateauEnd++;
                }

                // A plateau that reaches the final sample has no right-hand
                // neighbor, so it cannot be classified as a local maximum.
                if (plateauEnd == oriented.Length - 1)
                {
                    break;
                }

                if (oriented[plateauEnd] > oriented[plateauEnd + 1])
                {
                    int peakIndex = (i + plateauEnd) / 2;
                    double peakHeight = oriented[peakIndex];
                    if (peakHeight >= BlinkThreshold)
                    {
                        double prominence = CalculateProminence(oriented, peakIndex, peakHeight);
                        if (prominence >= BlinkThreshold * 0.5)
                        {
                            double width = CalculateWidth(oriented, peakIndex, peakHeight, prominence);
                            if (width >= minWidth && width <= maxWidth)
                            {
                                candidates.Add(new BlinkPeak(peakIndex, peakHeight));
                            }
                        }
                    }

                    i = plateauEnd + 1;
                }
                else
                {
                    i++;
                }
            }

            int minDistance = (int)(samplingRate * MinBlinkInterval);
            List<BlinkPeak> selected = new();
            foreach (BlinkPeak candidate in candidates.OrderByDescending(peak => peak.Height))
            {
                if (selected.All(peak => Math.Abs(peak.Index - candidate.Index) >= minDistance))
                {
                    selected.Add(candidate);
                }
            }

            return selected;
        }

        private static double CalculateProminence(double[] signal, int peakIndex, double peakHeight)
        {
            double leftBase = peakHeight;
            for (int i = peakIndex; i >= 0; i--)
            {
                if (i < peakIndex && signal[i] > peakHeight) break;
                leftBase = Math.Min(leftBase, signal[i]);
            }

            double rightBase = peakHeight;
            for (int i = peakIndex; i < signal.Length; i++)
            {
                if (i > peakIndex && signal[i] > peakHeight) break;
                rightBase = Math.Min(rightBase, signal[i]);
            }

            return peakHeight - Math.Max(leftBase, rightBase);
        }

        private static double CalculateWidth(double[] signal, int peakIndex, double peakHeight, double prominence)
        {
            double contour = peakHeight - prominence * 0.5;
            int left = peakIndex;
            while (left > 0 && signal[left] > contour) left--;
            double leftIntersection = left;
            if (left < peakIndex && signal[left + 1] != signal[left])
            {
                leftIntersection += (contour - signal[left]) / (signal[left + 1] - signal[left]);
            }

            int right = peakIndex;
            while (right < signal.Length - 1 && signal[right] > contour) right++;
            double rightIntersection = right;
            if (right > peakIndex && signal[right - 1] != signal[right])
            {
                rightIntersection -= (contour - signal[right]) / (signal[right - 1] - signal[right]);
            }

            return rightIntersection - leftIntersection;
        }

        private readonly record struct BlinkPeak(int Index, double Height);

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

