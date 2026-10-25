using brainflow;
using MathNet.Numerics.LinearAlgebra;
using System.Numerics;

namespace SimpleBCI.API.Processing
{
    /// <summary>
    /// The left/right motor-imagery model (Model/MotorImagery.onnx) as a black box.
    /// Input: 8 EEG channels in <see cref="ChannelNames"/> order, microvolts, 125 Hz.
    /// Output: [P(left hand), P(right hand)].
    /// </summary>
    public sealed class MotorImageryModel : IDisposable
    {
        /// <summary>Electrodes the model was trained on, in input order.</summary>
        public static readonly string[] ChannelNames = { "FC3", "FC4", "C5", "C3", "C4", "C6", "CP3", "CP4" };

        public const int SamplingRate = 125;
        public const int EpochStart = 62;                            // 0.5 s after the cue
        public const int EpochEnd = 500;                             // 4.0 s after the cue
        public const int EpochLength = EpochEnd - EpochStart;        // 438 samples = model input
        public const int WarmupSamples = 250;                        // 2 s before the epoch for the filter
        public const int WindowLength = WarmupSamples + EpochLength; // 688 samples to read from the board

        private readonly MLModel model;
        private readonly List<Matrix<double>> calibrationCovariances = new();
        private Matrix<double>? alignment;

        public MotorImageryModel(string onnxPath)
        {
            var parameters = new BrainFlowModelParams(
                (int)BrainFlowMetrics.USER_DEFINED,
                (int)BrainFlowClassifiers.ONNX_CLASSIFIER);
            parameters.file = onnxPath;
            parameters.output_name = "probabilities";
            MLModel.disable_ml_logger();
            model = new MLModel(parameters);
            model.prepare(); // once; BrainFlow allows one prepared model per file
        }

        public bool IsCalibrated => alignment is not null;
        public int CalibrationCount => calibrationCovariances.Count;

        /// <summary>
        /// Step 1: preprocessing. <paramref name="raw"/> holds 8 channels x at least
        /// <see cref="WindowLength"/> samples (µV) ending 4.0 s after the cue. Returns the
        /// 8 x 438 epoch: common-average reference, causal 8-30 Hz Butterworth band-pass,
        /// last 438 samples.
        /// </summary>
        public static double[,] Preprocess(double[,] raw)
        {
            int channels = raw.GetLength(0);
            int n = raw.GetLength(1);
            if (channels != ChannelNames.Length || n < WindowLength)
            {
                throw new ArgumentException(
                    $"Expected {ChannelNames.Length} channels x at least {WindowLength} samples.", nameof(raw));
            }

            var rows = new double[channels][];
            for (int c = 0; c < channels; c++)
            {
                rows[c] = new double[n];
            }

            for (int t = 0; t < n; t++)
            {
                double mean = 0;
                for (int c = 0; c < channels; c++)
                {
                    mean += raw[c, t];
                }

                mean /= channels;
                for (int c = 0; c < channels; c++)
                {
                    rows[c][t] = raw[c, t] - mean; // common-average reference
                }
            }

            var epoch = new double[channels, EpochLength];
            for (int c = 0; c < channels; c++)
            {
                // Causal (forward-only) Butterworth, exactly as in training. Do not use the zero-phase variant.
                double[] filtered = DataFilter.perform_bandpass(
                    rows[c], SamplingRate, 8.0, 30.0, 4, (int)FilterTypes.BUTTERWORTH, 0.0);
                for (int i = 0; i < EpochLength; i++)
                {
                    epoch[c, i] = filtered[n - EpochLength + i];
                }
            }

            return epoch;
        }

        /// <summary>Step 2a: add one preprocessed epoch of the calibration phase (labels are not needed).</summary>
        public void AddCalibrationEpoch(double[,] epoch)
        {
            calibrationCovariances.Add(OasCovariance(epoch));
        }

        /// <summary>Step 2b: compute the user's alignment matrix W = mean(covariance)^(-1/2).</summary>
        public void FinishCalibration()
        {
            if (calibrationCovariances.Count < 6)
            {
                throw new InvalidOperationException("Record at least 6 (better 10-20) calibration epochs first.");
            }

            Matrix<double> mean = calibrationCovariances.Aggregate((a, b) => a + b) / calibrationCovariances.Count;
            mean = (mean + mean.Transpose()) / 2.0;
            var evd = mean.Evd(Symmetricity.Symmetric);
            double[] values = evd.EigenValues.Select(v => v.Real).ToArray();
            double floor = 1e-10 * values.Max();
            var inverseSqrt = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(values.Length, i => 1.0 / Math.Sqrt(Math.Max(values[i], floor)));
            alignment = evd.EigenVectors * Matrix<double>.Build.DiagonalOfDiagonalVector(inverseSqrt)
                        * evd.EigenVectors.Transpose();
        }

        /// <summary>Step 3: [P(left hand), P(right hand)] for one preprocessed epoch.</summary>
        public double[] Predict(double[,] epoch)
        {
            if (alignment is null)
            {
                throw new InvalidOperationException("Call FinishCalibration() before Predict().");
            }

            Matrix<double> aligned = alignment * Matrix<double>.Build.DenseOfArray(epoch);
            var input = new double[ChannelNames.Length * EpochLength];
            for (int c = 0; c < ChannelNames.Length; c++)
            {
                for (int i = 0; i < EpochLength; i++)
                {
                    input[c * EpochLength + i] = aligned[c, i]; // channel by channel
                }
            }

            double[] output = model.predict(input);
            return new[] { output[0], output[1] };
        }

        /// <summary>Oracle-approximating shrinkage covariance of one epoch (as in training).</summary>
        private static Matrix<double> OasCovariance(double[,] epoch)
        {
            Matrix<double> x = Matrix<double>.Build.DenseOfArray(epoch);
            int p = x.RowCount;
            int n = x.ColumnCount;
            for (int c = 0; c < p; c++)
            {
                double rowMean = x.Row(c).Average();
                x.SetRow(c, x.Row(c) - rowMean);
            }

            Matrix<double> s = x * x.Transpose() / n;
            double mu = s.Trace() / p;
            double alpha = s.PointwisePower(2).Enumerate().Average();
            double num = alpha + mu * mu;
            double den = (n + 1) * (alpha - mu * mu / p);
            double shrinkage = den == 0 ? 1.0 : Math.Min(num / den, 1.0);
            return (1.0 - shrinkage) * s + shrinkage * mu * Matrix<double>.Build.DenseIdentity(p);
        }

        public void Dispose()
        {
            model.release();
        }
    }
}
