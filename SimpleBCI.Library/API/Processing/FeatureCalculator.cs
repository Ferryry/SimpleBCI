using brainflow;
using SimpleBCI.Library.API.Processing;
using SimpleBCI.Library.API.Processing.Helper;

namespace SimpleBCI.API.Processing
{
    internal class FeatureCalculator
    {
        private readonly BoardShim board;
        private readonly int samplingRate;
        private readonly int[] eegChannels;

        public FeatureCalculator(BoardShim board)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            this.board = board;

            int boardId = board.get_board_id();
            int preset = (int)BrainFlowPresets.DEFAULT_PRESET;
            samplingRate = BoardShim.get_sampling_rate(boardId, preset);
            eegChannels = BoardShim.get_eeg_channels(boardId, preset);

            if (eegChannels.Length < 3)
            {
                throw new InvalidOperationException(
                    "The board must provide EEG channels for AF7 and AF8.");
            }
        }

        private (double[] Af7, double[] Af8) GetAfSignals()
        {
            int preset = (int)BrainFlowPresets.DEFAULT_PRESET;
            double[,] data = board.get_current_board_data(
                MotorImageryFeatureExtractor.windowSize,
                preset);
            int sampleCount = data.GetLength(1);

            if (sampleCount == 0)
            {
                throw new InvalidOperationException(
                    "Not enough EEG samples are available to calculate the index.");
            }

            int af7Row = eegChannels[1];
            int af8Row = eegChannels[2];
            double[] af7 = new double[sampleCount];
            double[] af8 = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                af7[i] = data[af7Row, i];
                af8[i] = data[af8Row, i];
            }

            return (af7, af8);
        }

        public double[] GetFocusIndex()
        {
            (double[] af7, double[] af8) = GetAfSignals();

            double[] alphaAf7 = WaveExtractor.ExtractAlpha(af7, samplingRate);
            double[] betaAf7 = WaveExtractor.ExtractBeta(af7, samplingRate);
            double[] alphaAf8 = WaveExtractor.ExtractAlpha(af8, samplingRate);
            double[] betaAf8 = WaveExtractor.ExtractBeta(af8, samplingRate);

            double alphaPowerAf7 = WaveExtractor.CalculateMeanSquare(alphaAf7);
            double betaPowerAf7 = WaveExtractor.CalculateMeanSquare(betaAf7);
            double alphaPowerAf8 = WaveExtractor.CalculateMeanSquare(alphaAf8);
            double betaPowerAf8 = WaveExtractor.CalculateMeanSquare(betaAf8);

            return new double[]
            {
                FrequencyCalculator.FocusIndex(alphaPowerAf7, betaPowerAf7),
                FrequencyCalculator.FocusIndex(alphaPowerAf8, betaPowerAf8)
            };
        }

        public double[] GetRelaxationIndex()
        {
            (double[] af7, double[] af8) = GetAfSignals();

            double alphaPowerAf7 = WaveExtractor.CalculateMeanSquare(
                WaveExtractor.ExtractAlpha(af7, samplingRate));
            double betaPowerAf7 = WaveExtractor.CalculateMeanSquare(
                WaveExtractor.ExtractBeta(af7, samplingRate));
            double alphaPowerAf8 = WaveExtractor.CalculateMeanSquare(
                WaveExtractor.ExtractAlpha(af8, samplingRate));
            double betaPowerAf8 = WaveExtractor.CalculateMeanSquare(
                WaveExtractor.ExtractBeta(af8, samplingRate));

            return new double[]
            {
                FrequencyCalculator.RelaxationIndex(alphaPowerAf7, betaPowerAf7),
                FrequencyCalculator.RelaxationIndex(alphaPowerAf8, betaPowerAf8)
            };
        }

        public double[] GetArousalIndex()
        {
            (double[] af7, double[] af8) = GetAfSignals();

            double arousalAf7 = CalculateArousalIndex(af7);
            double arousalAf8 = CalculateArousalIndex(af8);

            return new double[] { arousalAf7, arousalAf8 };
        }

        private double CalculateArousalIndex(double[] signal)
        {
            double alphaPower = WaveExtractor.CalculateMeanSquare(
                WaveExtractor.ExtractAlpha(signal, samplingRate));
            double betaPower = WaveExtractor.CalculateMeanSquare(
                WaveExtractor.ExtractBeta(signal, samplingRate));
            double gammaPower = WaveExtractor.CalculateMeanSquare(
                WaveExtractor.ExtractGamma(signal, samplingRate));
            double thetaPower = WaveExtractor.CalculateMeanSquare(
                WaveExtractor.ExtractTheta(signal, samplingRate));

            return FrequencyCalculator.ArousalIndex(
                alphaPower,
                betaPower,
                gammaPower,
                thetaPower);
        }
    }
}
