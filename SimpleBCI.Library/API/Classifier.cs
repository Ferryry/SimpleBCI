using brainflow;

namespace SimpleBCI.API
{
    public sealed class Classifier
    {
        private readonly BoardShim board;
        private readonly MLModel model;

        /// <summary>
        /// Class constructor.
        /// </summary>
        /// <param name="board">Board reference instance.</param>
        public Classifier(BoardShim board)
        {
            this.model = ModelLoader.GetModel();
            this.board = board;
        }

        /// <summary>
        /// Performs interference from prepared EEG samples to given model.
        /// </summary>
        /// <param name="featureVector">A given feature vector.</param>
        /// <returns>Prediction vector.</returns>
        private double[] Predict(double[] modelInput)
        {
            model.prepare();

            try
            {
                return model.predict(modelInput);
            }
            finally
            {
                model.release();
            }
        }

        public BrainState ClassifyWindow(double[,] eegWindow)
        {
            double[] features = featureExtractor.ExtractFeatures(eegWindow);

            double[] prediction = modelLoader.Predict(features);

            return InterpretPrediction(prediction);
        }

        private BrainState InterpretPrediction(double[] prediction)
        {
            /*if (prediction.Length != 2)
                return BrainState.Neutral;

            if (prediction[0] > prediction[1])
                return BrainState.ImaginedLeftHand;

            return BrainState.ImaginedRightHand;*/

            return BrainState.Neutral;
        }

        /// <summary>
        /// Receives the current state of the brain.
        /// </summary>
        /// <returns>Predicted state.</returns>
        public BrainState GetCurrentBrainState()
        {
            double[,] data = board.get_current_board_data(256);

            Task.Delay(TimeSpan.FromMilliseconds(1000));

            if (data.GetLength(1) < 256)
            {
                return BrainState.Neutral;
            }

            return ClassifyWindow(data);
        }

        /// <summary>
        /// Prepares model information from matrix.
        /// </summary>
        /// <param name="eegRawData"></param>
        /// <returns></returns>
        private double[] PrepareModelInput(double[,] eegRawData)
        {
            double[] input = new double[4 * 256];

            int index = 0;

            for (int channel = 0; channel < 4; channel++)
            {
                for (int sample = 0; sample < 256; sample++)
                {
                    input[index++] = eegRawData[channel, sample];
                }
            }

            return input;
        }
    }
}
