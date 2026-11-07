using brainflow;
using SimpleBCI.API.States;

namespace SimpleBCI.API.Processing
{
    /// <summary>
    /// Runs <see cref="MotorImageryModel"/> on the live board stream.
    /// Call <see cref="AddCalibrationTrial"/> and <see cref="GetMotorImageryState"/> 4 s after an imagery cue.
    /// </summary>
    internal sealed class NeuroPawnMotorImageryClassifier : IDisposable
    {
        public const double MinScore = 0.70; // less confident answers become Neutral

        private readonly BoardShim board;
        private readonly int preset;
        private readonly int[] rows;
        private readonly MotorImageryModel model;

        public NeuroPawnMotorImageryClassifier(BoardShim board, ImmutableBoardInformation boardInfo)
        {
            ArgumentNullException.ThrowIfNull(board);
            ArgumentNullException.ThrowIfNull(boardInfo);

            if (boardInfo.SamplingRate != MotorImageryModel.SamplingRate)
            {
                throw new NotSupportedException(
                    $"The motor imagery model needs {MotorImageryModel.SamplingRate} Hz EEG.");
            }

            rows = new int[MotorImageryModel.ChannelNames.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var position = Enum.Parse<ElectrodePosition>(MotorImageryModel.ChannelNames[i], ignoreCase: true);
                if (!boardInfo.Channels.TryGetValue(position, out rows[i]))
                {
                    throw new NotSupportedException(
                        "The motor imagery model needs the electrodes " +
                        string.Join(", ", MotorImageryModel.ChannelNames) + ".");
                }
            }

            string modelPath = Path.Combine(AppContext.BaseDirectory, "Model", "MotorImagery.onnx");
            if (!File.Exists(modelPath))
            {
                throw new SimpleBCIException($"Motor imagery model not found: {modelPath}");
            }

            this.board = board;
            preset = boardInfo.Preset;
            model = new MotorImageryModel(modelPath);
        }

        public bool IsCalibrated => model.IsCalibrated;

        /// <summary>Calibration: call 4 s after each calibration cue (left or right, labels are not needed).</summary>
        public void AddCalibrationTrial()
        {
            model.AddCalibrationEpoch(ReadEpoch());
        }

        /// <summary>Calibration: call once after the calibration trials.</summary>
        public void FinishCalibration()
        {
            model.FinishCalibration();
        }

        /// <summary>Game: call 4 s after showing the "imagine now" cue.</summary>
        public MotorImageryState GetMotorImageryState()
        {
            double[] p = model.Predict(ReadEpoch());

            Console.WriteLine($"Left: {p[0]}, Right: {p[1]}");
            if (Math.Max(p[0], p[1]) < MinScore)
            {
                return MotorImageryState.Neutral;
            }

            return p[0] > p[1] ? MotorImageryState.Left : MotorImageryState.Right;
        }

        private double[,] ReadEpoch()
        {
            double[,] data = board.get_current_board_data(MotorImageryModel.WindowLength, preset);
            int n = data.GetLength(1);
            if (n < MotorImageryModel.WindowLength)
            {
                throw new SimpleBCIException("Not enough EEG yet: start the API at least 6 s before the first trial.");
            }

            var raw = new double[rows.Length, n];
            for (int c = 0; c < rows.Length; c++)
            {
                for (int t = 0; t < n; t++)
                {
                    raw[c, t] = data[rows[c], t];
                }
            }

            return MotorImageryModel.Preprocess(raw);
        }

        public void Dispose()
        {
            model.Dispose();
        }
    }
}
