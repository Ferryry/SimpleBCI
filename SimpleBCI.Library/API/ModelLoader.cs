using brainflow;

namespace SimpleBCI.API
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

            return new MLModel(modelParams);
        }
    }
}
