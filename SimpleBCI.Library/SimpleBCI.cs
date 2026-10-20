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
        private readonly BoardType boardType;
        private readonly IBoardConfiguration? boardConfiguration;
        // Temporarily disabled while the classifier's sampling-rate handling is revised.
        // private readonly MotorImageryClassifier? motorImageryClassifier;
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
        public SimpleBCI(SimpleBCIBoardSelection selection, SimpleBCIParameters? parameters = null)
        {
            ArgumentNullException.ThrowIfNull(selection);

            if (parameters == null)
            {
                parameters = new SimpleBCIParameters();
            }

            boardType = selection.Board;
            boardConfiguration = parameters.BoardConfiguration;
            if (boardConfiguration is not null && boardConfiguration.BoardType != boardType)
            {
                throw new ArgumentException(
                    $"The supplied board configuration targets {boardConfiguration.BoardType}, " +
                    $"but the selected board is {boardType}.",
                    nameof(parameters));
            }

            board = CreateBoard(selection, parameters);
            boardInfo = CreateBoardInformation(board, parameters);

            blinkDetector = TryCreateFeature(() => new BlinkDetector(board, boardInfo));
            emotionDetector = TryCreateFeature(() => new FaaStateCalculator(board, boardInfo));
            // motorImageryClassifier = TryCreateFeature(() => new MotorImageryClassifier(board));
        }

        private BoardShim CreateBoard(SimpleBCIBoardSelection selection, SimpleBCIParameters parameters)
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
                throw FeatureNotSupported(
                    "Blink detection (requires AF7 and AF8 to be mapped to EEG inputs)");
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
            throw FeatureNotSupported("Motor imagery classification (temporarily disabled)");
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
                boardConfiguration?.Apply(new BrainFlowBoardConfigurationContext(board));
                emotionDetector?.StartWorker();
                return true;
            }
            catch (Exception e)
            {
                ReleaseBoardAfterFailedStart();
                throw WrapBoardException("Could not start the selected EEG board.", e);
            }
        }

        /// <summary>
        /// Stops the API.
        /// </summary>
        public void StopAPI()
        {
            try
            {
                emotionDetector?.StopWorker().GetAwaiter().GetResult();

                if (IsAPIRunning())
                {
                    board.stop_stream();
                    board.release_session();
                }
            }
            catch (Exception e)
            {
                throw WrapBoardException("Could not stop the selected EEG board.", e);
            }
        }

        private static SimpleBCIException WrapBoardException(string message, Exception exception)
        {
            string detail = exception.Message;
            if (exception is BrainFlowError)
            {
                detail = "The EEG board could not be accessed. Check that it is connected, " +
                         "powered on, and available on the configured port.";
            }

            return new SimpleBCIException($"{message} {detail}");
        }

        private void ReleaseBoardAfterFailedStart()
        {
            try
            {
                if (board.is_prepared())
                {
                    board.stop_stream();
                    board.release_session();
                }
            }
            catch
            {
                // Preserve the startup failure as the error reported to the caller.
            }
        }

        private sealed class BrainFlowBoardConfigurationContext : IBoardConfigurationContext
        {
            private readonly BoardShim board;

            public BrainFlowBoardConfigurationContext(BoardShim board)
            {
                this.board = board;
            }

            public void SendCommand(string command)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(command);
                board.config_board(command);
            }

            public void Wait(TimeSpan duration)
            {
                if (duration < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(duration));
                }

                Thread.Sleep(duration);
            }
        }
    }
}

