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
        private readonly int af7Channel;
        private readonly int af8Channel;
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

            if (!boardInformation.Channels.TryGetValue(ElectrodePosition.Af7, out af7Channel) ||
                !boardInformation.Channels.TryGetValue(ElectrodePosition.Af8, out af8Channel))
            {
                throw new NotSupportedException(
                    "Cognitive workload estimation requires EEG channels named AF7 and AF8.");
            }

            this.board = board;
            this.boardInformation = boardInformation;
            samplingRate = boardInformation.SamplingRate;
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

            int requiredRow = Math.Max(af7Channel, af8Channel);
            if (data.GetLength(0) <= requiredRow)
            {
                throw new InvalidOperationException("AF7 or AF8 channel is missing from board data.");
            }

            double[] af7 = new double[sampleCount];
            double[] af8 = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                af7[i] = data[af7Channel, i];
                af8[i] = data[af8Channel, i];
            }

            int nfft = 1;

            while (nfft * 2 <= sampleCount)
            {
                nfft *= 2;
            }
            
            int overlap = nfft / 2;
            
            var psdAf7 = DataFilter.get_psd_welch(af7, nfft, overlap, samplingRate, (int)WindowOperations.HANNING);
            var psdAf8 = DataFilter.get_psd_welch(af8, nfft, overlap, samplingRate, (int)WindowOperations.HANNING);
            
            double thetaAf7 = PowerSpectrumCalculator.CalculateBandPower(psdAf7.ToValueTuple(), 4.0, 8.0);
            double thetaAf8 = PowerSpectrumCalculator.CalculateBandPower(psdAf8.ToValueTuple(), 4.0, 8.0);
            
            if (thetaAf7 <= 0 || thetaAf8 <= 0)
            {
                throw new InvalidOperationException("Theta power must be positive.");
            }

            return (Math.Log(thetaAf7) + Math.Log(thetaAf8)) / 2.0;
        }
    }
}
