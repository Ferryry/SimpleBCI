using brainflow;
using SimpleBCI.API;
using SimpleBCI.API.Processing;
using SimpleBCI.API.Processing.Helper;

namespace SimpleBCI.API.Processing
{
    internal class FeatureCalculator
    {
        private readonly BoardShim board;
        private readonly ImmutableBoardInformation boardInformation;
        private readonly int samplingRate;
        private readonly int af7Channel;
        private readonly int af8Channel;

        public FeatureCalculator(BoardShim board, ImmutableBoardInformation boardInformation)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            ArgumentNullException.ThrowIfNull(boardInformation);

            this.board = board;
            this.boardInformation = boardInformation;
            samplingRate = boardInformation.SamplingRate;

            if (!boardInformation.Channels.TryGetValue(ElectrodePosition.Af7, out af7Channel) ||
                !boardInformation.Channels.TryGetValue(ElectrodePosition.Af8, out af8Channel))
            {
                throw new NotSupportedException(
                    "Feature calculation requires EEG channels named AF7 and AF8.");
            }
        }

        private (double[] Af7, double[] Af8) GetAfSignals()
        {
            double[,] data = board.get_current_board_data(
                512,
                boardInformation.Preset);
            int sampleCount = data.GetLength(1);

            if (sampleCount == 0)
            {
                throw new InvalidOperationException(
                    "Not enough EEG samples are available to calculate the index.");
            }

            double[] af7 = new double[sampleCount];
            double[] af8 = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                af7[i] = data[af7Channel, i];
                af8[i] = data[af8Channel, i];
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

