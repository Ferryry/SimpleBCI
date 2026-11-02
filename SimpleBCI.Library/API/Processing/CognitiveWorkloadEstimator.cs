using brainflow;
using SimpleBCI.API.Processing.Helper;
using SimpleBCI.API.States;

namespace SimpleBCI.API.Processing
{
    /// <summary>
    /// Estimates relative workload from frontal theta power. This is an exploratory,
    /// personalized state estimate and is not a measure of a person's absolute workload.
    /// </summary>
    internal sealed class CognitiveWorkloadEstimator
    {
        private const double WindowDurationSeconds = 4.0;
        private const int MinimumBaselineSamples = 10;
        private const double ElevatedStandardDeviations = 1.0;

        private readonly BoardShim board;
        private readonly ImmutableBoardInformation boardInformation;
        private readonly int samplingRate;
        private readonly int[] channels;
        private readonly ElectrodePosition[] selectedPositions;
        private readonly object stateLock = new object();
        private readonly object workerLock = new object();
        private readonly List<double> baselineSamples = new List<double>();
        private double? baselineMean;
        private double baselineStandardDeviation;
        private CognitiveWorkloadState latestState = CognitiveWorkloadState.Unknown;
        private CancellationTokenSource? workerCancellation;
        private Task? workerTask;

        public CognitiveWorkloadEstimator(BoardShim board, ImmutableBoardInformation boardInformation)
        {
            ArgumentNullException.ThrowIfNull(board);
            ArgumentNullException.ThrowIfNull(boardInformation);

            (selectedPositions, channels) = SelectChannels(boardInformation.Channels);
            if (channels.Length == 0)
            {
                throw new NotSupportedException(
                    "Cognitive workload estimation requires Fz or a complete frontal pair (F3/F4, AF7/AF8, F7/F8, FP1/FP2).");
            }

            this.board = board;
            this.boardInformation = boardInformation;
            samplingRate = boardInformation.SamplingRate;
        }

        public IReadOnlyList<ElectrodePosition> SelectedChannels => Array.AsReadOnly(selectedPositions);

        private static (ElectrodePosition[] Positions, int[] Indices) SelectChannels(
            IReadOnlyDictionary<ElectrodePosition, int> available)
        {
            if (available.TryGetValue(ElectrodePosition.Fz, out int fzIndex))
            {
                return (new[] { ElectrodePosition.Fz }, new[] { fzIndex });
            }

            (ElectrodePosition Left, ElectrodePosition Right)[] priorities =
            {
                (ElectrodePosition.F3, ElectrodePosition.F4),
                (ElectrodePosition.Af7, ElectrodePosition.Af8),
                (ElectrodePosition.F7, ElectrodePosition.F8),
                (ElectrodePosition.Fp1, ElectrodePosition.Fp2)
            };

            foreach ((ElectrodePosition left, ElectrodePosition right) in priorities)
            {
                if (available.TryGetValue(left, out int leftIndex) &&
                    available.TryGetValue(right, out int rightIndex))
                {
                    return (new[] { left, right }, new[] { leftIndex, rightIndex });
                }
            }

            return (Array.Empty<ElectrodePosition>(), Array.Empty<int>());
        }

        public bool IsCalibrated
        {
            get { lock (stateLock) return baselineMean.HasValue; }
        }

        public int BaselineSampleCount
        {
            get { lock (stateLock) return baselineSamples.Count; }
        }

        public void AddBaselineSample()
        {
            double sample = ReadLogFrontalThetaPower();
            lock (stateLock)
            {
                if (baselineMean.HasValue)
                {
                    throw new InvalidOperationException("Baseline calibration is already complete.");
                }

                baselineSamples.Add(sample);
            }
        }

        public void FinishBaselineCalibration()
        {
            lock (stateLock)
            {
                if (baselineSamples.Count < MinimumBaselineSamples)
                {
                    throw new InvalidOperationException(
                        $"Record at least {MinimumBaselineSamples} baseline samples first.");
                }

                baselineMean = baselineSamples.Average();
                baselineStandardDeviation = Math.Sqrt(baselineSamples
                    .Average(value => Math.Pow(value - baselineMean.Value, 2)));
                latestState = CognitiveWorkloadState.LowerCognitiveWorkload;
            }
        }

        public void StartWorker()
        {
            lock (workerLock)
            {
                if (workerTask is { IsCompleted: false })
                {
                    return;
                }

                workerCancellation?.Dispose();
                workerCancellation = new CancellationTokenSource();
                workerTask = Task.Run(() => RunWorkerAsync(workerCancellation.Token));
            }
        }

        public async Task StopWorker()
        {
            Task? task;
            CancellationTokenSource? cancellation;
            lock (workerLock)
            {
                task = workerTask;
                cancellation = workerCancellation;
                if (task is null || cancellation is null) return;
                cancellation.Cancel();
            }

            try
            { 
                await task.ConfigureAwait(false);
            }
            finally
            {
                lock (workerLock)
                {
                    if (workerTask == task)
                    {
                        workerTask = null;
                        workerCancellation = null;
                        cancellation.Dispose();
                    }
                }
            }
        }

        public CognitiveWorkloadState GetState()
        {
            lock (stateLock) return latestState;
        }

        private async Task RunWorkerAsync(CancellationToken token)
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            while (!token.IsCancellationRequested)
            {
                if (IsCalibrated)
                {
                    try
                    {
                        double current = ReadLogFrontalThetaPower();
                        lock (stateLock)
                        {
                            double threshold = baselineMean!.Value +
                                ElevatedStandardDeviations * baselineStandardDeviation;

                            latestState = current > threshold
                                ? CognitiveWorkloadState.HigherCognitiveWorkload
                                : CognitiveWorkloadState.LowerCognitiveWorkload;
                        }
                    }
                    catch (InvalidOperationException) { }
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    {
                        return;
                    }
                }
                catch (OperationCanceledException) { 
                    return;
                }
            }
        }

        private double ReadLogFrontalThetaPower()
        {
            int sampleCount = (int)Math.Ceiling(samplingRate * WindowDurationSeconds);
            double[,] data = board.get_current_board_data(sampleCount, boardInformation.Preset);

            if (data.GetLength(1) < sampleCount)
            {
                throw new InvalidOperationException("Not enough EEG data is available for workload estimation.");
            }

            int requiredRow = channels.Max();
            if (data.GetLength(0) <= requiredRow)
            {
                throw new InvalidOperationException("A selected workload channel is missing from board data.");
            }

            int nfft = 1;

            while (nfft * 2 <= sampleCount)
            {
                nfft *= 2;
            }
            
            int overlap = nfft / 2;
            
            double[] logThetaPowers = new double[channels.Length];
            for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                double[] signal = new double[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    signal[i] = data[channels[channelIndex], i];
                }

                var psd = DataFilter.get_psd_welch(
                    signal, nfft, overlap, samplingRate, (int)WindowOperations.HANNING);
                double thetaPower = PowerSpectrumCalculator.CalculateBandPower(
                    psd.ToValueTuple(), 4.0, 8.0);
                if (thetaPower <= 0)
                {
                    throw new InvalidOperationException("Theta power must be positive.");
                }

                logThetaPowers[channelIndex] = Math.Log(thetaPower);
            }

            return logThetaPowers.Average();
        }
    }
}
