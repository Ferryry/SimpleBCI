using brainflow;
using SimpleBCI.API;
using SimpleBCI.API.Processing;
using SimpleBCI.API.States;

namespace SimpleBCI
{
    /// <summary>
    /// Provides access to supported BCI features such as blink detection,
    /// FAA calculation, and motor imagery classification.
    /// </summary>
    public sealed class SimpleBCI
    {
        private readonly BoardShim board;
        private readonly MotorImageryClassifier? motorImageryClassifier;
        private readonly BlinkDetector? blinkDetector;
        private readonly FaaStateCalculator? emotionDetector;
        // private readonly FeatureCalculator? featureCalculator;
        private readonly ImmutableBoardInformation boardInfo;

        /// <summary>
        /// Creates a SimpleBCI instance for the selected board.
        /// </summary>
        /// <param name="selection">The board to use.</param>
        /// <param name="channelLayout">
        /// Optional: Electrode names in order of EEG data inputs,
        /// i.e. ["TP9", "AF7", "AF8", "TP10"].
        /// </param>
        public SimpleBCI(SimpleBoardSelection selection, SimpleBCIParameters? parameters = null)
        {
            ArgumentNullException.ThrowIfNull(selection);

            if (parameters == null)
            {
                parameters = new SimpleBCIParameters();
            }

            board = CreateBoard(selection, parameters);
            boardInfo = CreateBoardInformation(board, parameters);

            blinkDetector = TryCreateFeature(() => new BlinkDetector(board, boardInfo));
            emotionDetector = TryCreateFeature(() => new FaaStateCalculator(board, boardInfo));
            motorImageryClassifier = TryCreateFeature(() => new MotorImageryClassifier(board));
        }

        private BoardShim CreateBoard(SimpleBoardSelection selection, SimpleBCIParameters parameters)
        {
            BrainFlowInputParams inputParams = new BrainFlowInputParams();

            if (parameters.SerialPort is not null)
            {
                inputParams.serial_port = parameters.SerialPort;
            }

            if (parameters.SerialNumber is not null)
            {
                inputParams.serial_number = parameters.SerialNumber;
            }

            if (parameters.AdditionalParams is not null)
            {
                inputParams.other_info = parameters.AdditionalParams;
            }

            return new BoardShim((int)selection.Board, inputParams);
        }

        private static ImmutableBoardInformation CreateBoardInformation(BoardShim board, SimpleBCIParameters parameters)
        {
            int preset = (int)BrainFlowPresets.DEFAULT_PRESET;
            return new ImmutableBoardInformation(board.get_board_id(), preset, parameters);
        }

        /// <summary>
        /// Tries to create a feature and returns null if the selected board does not support it.
        /// </summary>
        /// <typeparam name="T">The feature component type.</typeparam>
        /// <param name="create">A function that creates the feature component.</param>
        /// <returns>The created component, or null if the board does not support the feature.</returns>
        private static T? TryCreateFeature<T>(Func<T> create) where T : class
        {
            try
            {
                return create();
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static NotSupportedException FeatureNotSupported(string feature) =>
            new NotSupportedException($"{feature} is not supported by the selected board.");

        /// <summary>
        /// Checks current API status.
        /// </summary>
        /// <returns>Status</returns>
        public bool IsAPIRunning()
        {
            return board.is_prepared();
        }

        /// <summary>
        /// Returns and estimates the user's most likely emotional state based on EEG data.
        /// </summary>
        /// <returns>Estimated emotion state.</returns>
        public FaaState GetFaaState()
        {
            if (emotionDetector == null)
            {
                throw FeatureNotSupported("FAA calculation");
            }

            return emotionDetector.GetFaaState();
        }

        /// <summary>
        /// Returns the current blinkstate.
        /// <see cref="StartAPI"/> must be called before calling this method.
        /// </summary>
        /// <returns>Blinkstate.</returns>
        public BlinkState GetCurrentBlinkState()
        {
            if (blinkDetector == null)
            {
                throw FeatureNotSupported("Blink detection");
            }

            return blinkDetector.GetCurrentBlinkState();
        }

        /// <summary>
        /// Returns the current brainstate classified and predicted by the local ML.
        /// <see cref="StartAPI"/> must be called before calling this method.
        /// </summary>
        /// <returns>Brainstate.</returns>
        public MotorImageryState GetMotorImageryState()
        {
            if (motorImageryClassifier == null)
            {
                throw FeatureNotSupported("Motor imagery classification");
            }

            return motorImageryClassifier.GetMotorImageryState();
        }

        /*public double[] GetFocusIndex()
        {
            return (featureCalculator ?? throw FeatureNotSupported("Focus index calculation")).GetFocusIndex();
        }

        public double[] GetRelaxationIndex()
        {
            return (featureCalculator ?? throw FeatureNotSupported("Relaxation index calculation")).GetRelaxationIndex();
        }

        public double[] GetArousalIndex()
        {
            return (featureCalculator ?? throw FeatureNotSupported("Arousal index calculation")).GetArousalIndex();
        }*/

        /// <summary>
        /// Starts the API.
        /// </summary>
        public bool StartAPI()
        {
            try
            {
                board.prepare_session();
                board.start_stream();
                emotionDetector?.StartWorker();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Stops the API.
        /// </summary>
        public void StopAPI()
        {
            emotionDetector?.StopWorker().GetAwaiter().GetResult();

            if (IsAPIRunning())
            {
                board.stop_stream();
                board.release_session();
            }
        }
    }
}

