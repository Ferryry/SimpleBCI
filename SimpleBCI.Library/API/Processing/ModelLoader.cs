using brainflow;

namespace SimpleBCI.Library.API.Processing
{
    public static class ModelLoader
    {
        /// <summary>
        /// Creates an instance of MLModel and loads the onnx model.
        /// </summary>
        /// <returns>Loaded model for infer purpose.</returns>
        public static MLModel GetModel()
        {
            BrainFlowModelParams modelParams = new BrainFlowModelParams(
                (int)BrainFlowMetrics.USER_DEFINED,
                (int)BrainFlowClassifiers.ONNX_CLASSIFIER
            );

            modelParams.file = Path.Combine(
                AppContext.BaseDirectory,
                "Model",
                "Model.onnx"
            );

            modelParams.output_name = "probabilities";
            modelParams.max_array_size = 3;

            return new MLModel(modelParams);
        }
    }
}
