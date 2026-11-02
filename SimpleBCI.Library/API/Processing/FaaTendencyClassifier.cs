using brainflow;
using SimpleBCI.API.Processing.Helper;
using SimpleBCI.API.States;

namespace SimpleBCI.API.Processing
{
    internal sealed class FaaTendencyClassifier
    {
        private const int WindowCount = 5;
        private const int Nfft = 256;
        private const int Overlap = 128;
        private const double WindowDurationSeconds = 2.0;

        private readonly BoardShim board;
        private readonly ImmutableBoardInformation boardInformation;
        private readonly int samplingRate;
        private readonly int af7Channel;
        private readonly int af8Channel;
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

            if (!boardInformation.Channels.TryGetValue(ElectrodePosition.Af7, out af7Channel) ||
                !boardInformation.Channels.TryGetValue(ElectrodePosition.Af8, out af8Channel))
            {
                throw new NotSupportedException(
                    "FAA calculation requires EEG channels named AF7 and AF8.");
            }
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

            if (data.GetLength(0) <= Math.Max(af7Channel, af8Channel))
            {
                throw new InvalidOperationException("AF7 or AF8 channel is missing from the board data.");
            }

            double[] af7 = new double[sampleCount];
            double[] af8 = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                af7[i] = data[af7Channel, i];
                af8[i] = data[af8Channel, i];
            }

            var psdAF7 = DataFilter.get_psd_welch(af7, Nfft, Overlap, samplingRate, (int)WindowOperations.HANNING);
            var psdAF8 = DataFilter.get_psd_welch(af8, Nfft, Overlap, samplingRate, (int)WindowOperations.HANNING);

            double alphaPowerAF7 = PowerSpectrumCalculator.CalculateBandPower(
                psdAF7.ToValueTuple(), 8.0, 13.0);
            double alphaPowerAF8 = PowerSpectrumCalculator.CalculateBandPower(
                psdAF8.ToValueTuple(), 8.0, 13.0);

            if (alphaPowerAF7 <= 0 || alphaPowerAF8 <= 0)
            {
                throw new InvalidOperationException("Alpha power must be positive.");
            }

            double faa = Math.Log(alphaPowerAF8) - Math.Log(alphaPowerAF7);
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

