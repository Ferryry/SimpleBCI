using brainflow;

namespace SimpleBCI.API
{
    public sealed class MotorImageryClassifier
    {
        private readonly BoardShim board;
        private readonly MLModel model;
        private readonly MotorImageryFeatureExtractor featureExtractor = new MotorImageryFeatureExtractor();
        private readonly object modelLock = new object();

        /// <summary>
        /// Creates a classifier for a prepared BrainFlow board.
        /// </summary>
        public MotorImageryClassifier(BoardShim board)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            this.board = board;

            int boardId = board.get_board_id();

            int samplingRate = BoardShim.get_sampling_rate(
                boardId,
                (int)BrainFlowPresets.DEFAULT_PRESET);

            if (samplingRate != MotorImageryFeatureExtractor.samplingRate)
            {
                throw new ArgumentException(
                    $"The model requires {MotorImageryFeatureExtractor.samplingRate} Hz EEG data; " +
                    $"the selected board provides {samplingRate} Hz.",
                    nameof(board));
            }

            int[] eegChannels = BoardShim.get_eeg_channels(
                boardId,
                (int)BrainFlowPresets.DEFAULT_PRESET);

            if (eegChannels.Length < MotorImageryFeatureExtractor.channelCount)
            {
                throw new ArgumentException(
                    $"The selected board exposes {eegChannels.Length} EEG channels; " +
                    $"the model requires {MotorImageryFeatureExtractor.channelCount}.",
                    nameof(board));
            }

            model = ModelLoader.GetModel();
        }

        /// <summary>
        /// Classifies one 4x512 window ordered TP9, AF7, AF8, TP10.
        /// </summary>
        public MotorImageryState ClassifyWindow(double[,] eegWindow)
        {
            double[] features = featureExtractor.ExtractFeatures(eegWindow);
            double[] probabilities = Predict(features);
            return InterpretPrediction(probabilities);
        }

        private double[] Predict(double[] modelInput)
        {
            lock (modelLock)
            {
                model.prepare();

                try
                {
                    return model.predict(modelInput);
                }
                finally
                {
                    model.release();
                }
            }
        }

        private static MotorImageryState InterpretPrediction(double[] probabilities)
        {
            // train_new.py encodes the model classes as Left=0, Neutral=1, Right=2.
            if (probabilities is null || probabilities.Length != 3 ||
                probabilities.Any(value => !double.IsFinite(value)))
            {
                throw new InvalidOperationException(
                    "The ONNX model must return three finite class probabilities " +
                    "in the order Left, Neutral, Right.");
            }

            int predictedClass = Array.IndexOf(probabilities, probabilities.Max());
            return predictedClass switch
            {
                0 => MotorImageryState.Left,
                1 => MotorImageryState.Neutral,
                2 => MotorImageryState.Right,
                _ => throw new InvalidOperationException(
                    "The ONNX model returned an unsupported class index.")
            };
        }

        /// <summary>
        /// Classifies the latest complete two-second EEG window. Returns Neutral
        /// until the board has buffered enough samples.
        /// </summary>
        public MotorImageryState GetMotorImageryState()
        {
            double[,] data = board.get_current_board_data(
                MotorImageryFeatureExtractor.windowSize,
                (int)BrainFlowPresets.DEFAULT_PRESET);

            if (data.GetLength(1) < MotorImageryFeatureExtractor.windowSize)
            {
                return MotorImageryState.Neutral;
            }

            int[] eegChannels = BoardShim.get_eeg_channels(
                board.get_board_id(),
                (int)BrainFlowPresets.DEFAULT_PRESET);
            if (eegChannels.Length < MotorImageryFeatureExtractor.channelCount)
            {
                throw new InvalidOperationException(
                    "The selected board no longer exposes the four EEG channels " +
                    "required by this model.");
            }

            var window = new double[
                MotorImageryFeatureExtractor.channelCount,
                MotorImageryFeatureExtractor.windowSize];

            // Muse's default EEG preset exposes channels in the trained order:
            // TP9, AF7, AF8, TP10. BoardShim data rows use board-wide channel indices.
            for (int channel = 0; channel < MotorImageryFeatureExtractor.channelCount; channel++)
            {
                int sourceRow = eegChannels[channel];
                if (sourceRow < 0 || sourceRow >= data.GetLength(0))
                {
                    throw new InvalidOperationException(
                        $"BrainFlow returned an invalid EEG row index ({sourceRow}).");
                }

                for (int sample = 0; sample < MotorImageryFeatureExtractor.windowSize; sample++)
                {
                    window[channel, sample] = data[
                        sourceRow,
                        data.GetLength(1) - MotorImageryFeatureExtractor.windowSize + sample];
                }
            }

            return ClassifyWindow(window);
        }
    }
}
