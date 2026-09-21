using brainflow;

namespace SimpleBCI.API
{
    public class EEGClassifier
    {
        private readonly BoardShim board;
        private readonly MLModel model;
        private static EEGClassifier? instance;

        /// <summary>
        /// Returns the singleton instance of the <see cref="EEGClassifier"/>.
        /// Creates the instance if it does not exist yet.
        /// </summary>
        public static EEGClassifier GetSingletonInstance(BoardShim board)
        {
            ArgumentNullException.ThrowIfNull(board);

            if (instance == null)
            {
                instance = new EEGClassifier(board);
            }

            return instance;
        }

        /// <summary>
        /// Private class constructor.
        /// </summary>
        /// <param name="board">Board reference instance.</param>
        private EEGClassifier(BoardShim board)
        {
            BrainFlowModelParams modelParams = new BrainFlowModelParams(
                (int)BrainFlowMetrics.USER_DEFINED,
                (int)BrainFlowClassifiers.DEFAULT_CLASSIFIER
            );

            this.model = new MLModel(modelParams);
            this.board = board;
        }

        /// <summary>
        /// Performs interference from prepared EEG samples to given model.
        /// </summary>
        /// <param name="featureVector">A given feature vector.</param>
        /// <returns>Prediction vector.</returns>
        private double[] Predict(double[] featureVector)
        {
            model.prepare();

            try
            {
                return model.predict(featureVector);
            }
            finally
            {
                model.release();
            }
        }

        /// <summary>
        /// Avaialble brainstates.
        /// </summary>
        public enum BrainState
        {
            Imagined_Left_Hand,
            Imagined_Right_Right,
            None
        }

        private BrainState Classify(double[,] eegRawData)
        {
            /*
             * 
             * Placeholder
             * 
             */

            return BrainState.None;
        }

        /// <summary>
        /// Receives the current state of the brain.
        /// </summary>
        /// <returns>Predicted state.</returns>
        public BrainState GetCurrentBrainState()
        {
            return BrainState.None;
        }

        /// <summary>
        /// Extacts features from matrix.
        /// </summary>
        /// <param name="eegRawData"></param>
        /// <returns></returns>
        private double[] ExtractFeatures(double[,] eegRawData)
        {
            return eegRawData
                .Cast<double>()
                .Take(10)
                .ToArray();
        }
    }
}
