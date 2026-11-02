using brainflow;
using SimpleBCI.API.Processing.Helper;
using SimpleBCI.API.States;

namespace SimpleBCI.API.Processing
{
    internal sealed class FaaTendencyClassifier
    {
        private const int WindowCount = 5;
        private const double WindowDurationSeconds = 2.0;

        private readonly BoardShim board;
        private readonly ImmutableBoardInformation boardInformation;
        private readonly int samplingRate;
        private readonly int leftChannel;
        private readonly int rightChannel;
        private readonly ElectrodePosition leftPosition;
        private readonly ElectrodePosition rightPosition;
        private readonly object stateLock = new object();
        private readonly object workerLock = new object();
        private readonly Queue<double> faaHistory = new Queue<double>();
        private FaaTendencyState latestState = FaaTendencyState.Unknown;

        private CancellationTokenSource? workerCancellation;
        private Task? workerTask;

        public FaaTendencyClassifier(
            BoardShim board,
            ImmutableBoardInformation boardInformation)
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

            (leftPosition, rightPosition, leftChannel, rightChannel) = SelectChannels(boardInformation.Channels);
            if (leftChannel < 0 || rightChannel < 0)
            {
                throw new NotSupportedException(
                    "FAA calculation requires one complete left/right pair: F3/F4, F7/F8, AF7/AF8, or FP1/FP2.");
            }
        }

        public IReadOnlyList<ElectrodePosition> SelectedChannels =>
            Array.AsReadOnly(new[] { leftPosition, rightPosition });

        private static (ElectrodePosition Left, ElectrodePosition Right, int LeftIndex, int RightIndex)
            SelectChannels(IReadOnlyDictionary<ElectrodePosition, int> available)
        {
            (ElectrodePosition Left, ElectrodePosition Right)[] priorities =
            {
                (ElectrodePosition.F3, ElectrodePosition.F4),
                (ElectrodePosition.F7, ElectrodePosition.F8),
                (ElectrodePosition.Af7, ElectrodePosition.Af8),
                (ElectrodePosition.Fp1, ElectrodePosition.Fp2)
            };

            foreach ((ElectrodePosition left, ElectrodePosition right) in priorities)
            {
                if (available.TryGetValue(left, out int leftIndex) &&
                    available.TryGetValue(right, out int rightIndex))
                {
                    return (left, right, leftIndex, rightIndex);
                }
            }

            return (ElectrodePosition.Unspecific, ElectrodePosition.Unspecific, -1, -1);
        }

        /// <summary>
        /// Starts periodic emotion detection with a one-second interval.
        /// </summary>
        public void StartWorker()
        {
            lock (workerLock)
            {
                if (workerTask != null && !workerTask.IsCompleted)
                {
                    return;
                }

                if (workerCancellation != null)
                {
                    workerCancellation.Dispose();
                }

                workerCancellation = new CancellationTokenSource();
                CancellationToken token = workerCancellation.Token;
                workerTask = Task.Run(() => RunWorkerAsync(token));
            }
        }

        /// <summary>
        /// Stops the periodic emotion detection worker.
        /// </summary>
        public async Task StopWorker()
        {
            Task? currentTask;
            CancellationTokenSource? currentCancellation;

            lock (workerLock)
            {
                currentTask = workerTask;
                currentCancellation = workerCancellation;
                if (currentTask == null || currentCancellation == null)
                {
                    return;
                }

                currentCancellation.Cancel();
            }

            try
            {
                await currentTask.ConfigureAwait(false);
            }
            finally
            {
                lock (workerLock)
                {
                    if (workerTask == currentTask)
                    {
                        workerTask = null;
                        workerCancellation = null;
                        currentCancellation.Dispose();
                    }
                }
            }
        }

        private async Task RunWorkerAsync(CancellationToken cancellationToken)
        {
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UpdateFaaState();
                }
                catch (InvalidOperationException) { }

                try
                {
                    bool hasNextTick = await timer
                        .WaitForNextTickAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (!hasNextTick)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Returns the most recently calculated result without reading the board.
        /// </summary>
        public FaaTendencyState GetFaaState()
        {
            lock (stateLock)
            {
                return latestState;
            }
        }

        internal void UpdateFaaState()
        {
            int windowSampleCount = (int)Math.Ceiling(samplingRate * WindowDurationSeconds);
            double[,] data = board.get_current_board_data(windowSampleCount, boardInformation.Preset);

            int sampleCount = data.GetLength(1);
            if (sampleCount < windowSampleCount)
            {
                throw new InvalidOperationException("Not enough EEG data available.");
            }

            if (data.GetLength(0) <= Math.Max(leftChannel, rightChannel))
            {
                throw new InvalidOperationException("A selected FAA channel is missing from the board data.");
            }

            double[] left = new double[sampleCount];
            double[] right = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                left[i] = data[leftChannel, i];
                right[i] = data[rightChannel, i];
            }

            // The NeuroPawn's 2-second window contains 250 samples at 125 Hz,
            // so a fixed NFFT of 256 is too large. Choose the largest power of
            // two strictly smaller than the actual data length.
            int nfft = 1;
            while (nfft * 2 < sampleCount)
            {
                nfft *= 2;
            }
            int overlap = nfft / 2;

            var psdLeft = DataFilter.get_psd_welch(left, nfft, overlap, samplingRate, (int)WindowOperations.HANNING);
            var psdRight = DataFilter.get_psd_welch(right, nfft, overlap, samplingRate, (int)WindowOperations.HANNING);

            double alphaPowerLeft = PowerSpectrumCalculator.CalculateBandPower(
                psdLeft.ToValueTuple(), 8.0, 13.0);
            double alphaPowerRight = PowerSpectrumCalculator.CalculateBandPower(
                psdRight.ToValueTuple(), 8.0, 13.0);

            if (alphaPowerLeft <= 0 || alphaPowerRight <= 0)
            {
                throw new InvalidOperationException("Alpha power must be positive.");
            }

            double faa = Math.Log(alphaPowerRight) - Math.Log(alphaPowerLeft);
            lock (stateLock)
            {
                faaHistory.Enqueue(faa);
                while (faaHistory.Count > WindowCount)
                {
                    faaHistory.Dequeue();
                }

                double meanFaa = faaHistory.Average();
                latestState = CalculateFaaState(meanFaa);
            }
        }

        private static FaaTendencyState CalculateFaaState(double meanFaa)
        {
            if (meanFaa > 1)
            {
                return FaaTendencyState.ApproachRelated;
            }

            if (meanFaa < -1)
            {
                return FaaTendencyState.AvoidanceRelated;
            }

            return FaaTendencyState.NoSignificantAsymmetry;
        }
    }
}

