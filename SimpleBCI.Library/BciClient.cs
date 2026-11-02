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
    public sealed class BciClient : IDisposable
    {
        private readonly BoardShim board;
        private readonly BoardType boardType;
        private readonly IBoardConfiguration? boardConfiguration;
        private readonly ImmutableBoardInformation boardInfo;
        private readonly List<string> availableFeatures;
        private static bool autoStartApi = true;

        #region Classifier and detector components related fields and instancess
        private readonly NeuroPawnMotorImageryClassifier? motorImageryClassifier;
        private readonly BlinkDetector? blinkDetector;
        private readonly FaaTendencyClassifier? faaTendency;
        private readonly CognitiveWorkloadEstimator? cognitiveWorkload;

        /// <summary>
        /// Returns the names of the features supported by this board configuration.
        /// </summary>
        /// <remarks>Possible names are "BlinkDetection", "FaaTendency", "MotorImagery", and "CognitiveWorkload".</remarks>
        public IReadOnlyCollection<string> GetAvailableFeatures() =>
            Array.AsReadOnly(availableFeatures.ToArray());

        /// <summary>Whether blink detection is supported by this board configuration.</summary>
        public bool IsBlinkDetectionAvailable => blinkDetector != null;

        /// <summary>
        /// The frontal electrode pair selected for blink detection, or an empty
        /// list when none of the supported pairs is available.
        /// </summary>
        public IReadOnlyList<ElectrodePosition> BlinkDetectionChannels =>
            blinkDetector?.SelectedChannels ?? Array.Empty<ElectrodePosition>();

        /// <summary>Whether FAA tendency estimation is supported by this board configuration.</summary>
        public bool IsFaaTendencyAvailable => faaTendency != null;

        /// <summary>The left/right electrode pair selected for FAA, or empty when unavailable.</summary>
        public IReadOnlyList<ElectrodePosition> FaaChannels =>
            faaTendency?.SelectedChannels ?? Array.Empty<ElectrodePosition>();

        /// <summary>Whether motor imagery classification is supported by this board configuration.</summary>
        public bool IsMotorImageryAvailable => motorImageryClassifier != null;

        /// <summary>Whether personalized cognitive workload estimation is supported.</summary>
        public bool IsCognitiveWorkloadAvailable => cognitiveWorkload != null;

        /// <summary>The channel or channels selected for workload estimation, or empty when unavailable.</summary>
        public IReadOnlyList<ElectrodePosition> CognitiveWorkloadChannels =>
            cognitiveWorkload?.SelectedChannels ?? Array.Empty<ElectrodePosition>();
        #endregion

        /// <summary>
        /// Creates a SimpleBCI instance for the selected board.
        /// </summary>
        /// <param name="selection">The board to use.</param>
        /// <param name="channelLayout">
        /// Optional: Electrode names in order of EEG data inputs,
        /// i.e. ["TP9", "AF7", "AF8", "TP10"].
        /// </param>
        public BciClient(SimpleBCIBoardSelection selection, SimpleBCIParameters? parameters = null)
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

            availableFeatures = new List<string>();

            blinkDetector = TryCreateFeature(
                "BlinkDetection",
                () => new BlinkDetector(board, boardInfo));
            faaTendency = TryCreateFeature(
                "FaaTendency",
                () => new FaaTendencyClassifier(board, boardInfo));
            motorImageryClassifier = TryCreateFeature(
                "MotorImagery",
                () => new NeuroPawnMotorImageryClassifier(board, boardInfo));
            cognitiveWorkload = TryCreateFeature(
                "CognitiveWorkload",
                () => new CognitiveWorkloadEstimator(board, boardInfo));
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
        private T? TryCreateFeature<T>(string featureName, Func<T> create) where T : class
        {
            try
            {
                T feature = create();
                availableFeatures.Add(featureName);
                return feature;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static NotSupportedException FeatureNotSupported(string feature) =>
            new NotSupportedException($"{feature} is not supported by the selected board.");

        /// <summary>
        /// If enabled Api will autostart after instantiation.
        /// </summary>
        /// <param name="autoStart">Autostart option.</param>
        public static void SetAutoStartApi(bool autoStart)
        {
            autoStartApi = autoStart;
        }

        /// <summary>
        /// Returns set autostart option.
        /// </summary>
        /// <returns>Status</returns>
        public static bool IsAutoStartApiEnabled()
        {
            return autoStartApi;
        }

        /// <summary>
        /// Checks current API status.
        /// </summary>
        /// <returns>Status</returns>
        public bool IsApiRunning()
        {
            return board.is_prepared();
        }

        /// <summary>
        /// Returns and estimates the user's most likely emotional state based on EEG data.
        /// </summary>
        /// <returns>Estimated emotion state.</returns>
        public FaaTendencyState GetFaaTendency()
        {
            if (faaTendency == null)
            {
                throw FeatureNotSupported("FAA calculation");
            }

            return faaTendency.GetFaaState();
        }

        /// <summary>Records one EEG window during a low-workload baseline calibration.</summary>
        public void AddCognitiveWorkloadBaselineSample()
        {
            RequireCognitiveWorkload().AddBaselineSample();
        }

        /// <summary>Finishes personalized baseline calibration after at least ten samples.</summary>
        public void FinishCognitiveWorkloadCalibration()
        {
            RequireCognitiveWorkload().FinishBaselineCalibration();
        }

        /// <summary>Whether cognitive workload calibration has been completed.</summary>
        public bool IsCognitiveWorkloadCalibrated => RequireCognitiveWorkload().IsCalibrated;

        /// <summary>Returns workload relative to the calibrated personal baseline.</summary>
        public CognitiveWorkloadState GetCognitiveWorkloadState()
        {
            return RequireCognitiveWorkload().GetState();
        }

        private CognitiveWorkloadEstimator RequireCognitiveWorkload() => cognitiveWorkload ??
            throw FeatureNotSupported("Cognitive workload estimation (requires Fz or a supported frontal pair)");

        /// <summary>
        /// Returns the current blinkstate.
        /// <see cref="Start"/> must be called before calling this method.
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
        /// Returns the motor imahgery state.
        /// <see cref="Start"/> must be called before calling this method.
        /// </summary>
        /// <returns>MotorImageryState</returns>
        public MotorImageryState GetMotorImageryState()
        {
            return RequireMotorImagery().GetMotorImageryState();
        }

        /// <summary>
        /// Records one calibration trial. Call it 4 s after a calibration cue.
        /// </summary>
        public void AddMotorImageryCalibrationTrial()
        {
            RequireMotorImagery().AddCalibrationTrial();
        }

        /// <summary>
        /// Finishes the calibration. Call it once, after 10-20 calibration trials.
        /// </summary>
        public void FinishMotorImageryCalibration()
        {
            RequireMotorImagery().FinishCalibration();
        }

        /// <summary>Whether motor imagery calibration has been completed for this API session.</summary>
        public bool IsMotorImageryCalibrated => RequireMotorImagery().IsCalibrated;

        private NeuroPawnMotorImageryClassifier RequireMotorImagery() =>
            motorImageryClassifier ?? throw FeatureNotSupported(
                "Motor imagery classification (requires a 125 Hz board with FC3, FC4, C5, C3, C4, C6, CP3 and CP4 mapped)");


        /// <summary>
        /// Starts the API.
        /// </summary>
        public bool Start()
        {
            try
            {
                board.prepare_session();
                board.start_stream();
                boardConfiguration?.Apply(new BrainFlowBoardConfigurationContext(board));
                faaTendency?.StartWorker();
                cognitiveWorkload?.StartWorker();
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
        public void Stop()
        {
            try
            {
                faaTendency?.StopWorker().GetAwaiter().GetResult();
                cognitiveWorkload?.StopWorker().GetAwaiter().GetResult();
                motorImageryClassifier?.Dispose();

                if (IsApiRunning())
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

        public void Dispose()
        {
            Stop();
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
