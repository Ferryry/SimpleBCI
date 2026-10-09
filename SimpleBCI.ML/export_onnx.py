from pathlib import Path

import numpy as np
import pandas as pd
import joblib
import onnx

from skl2onnx import to_onnx


MODEL_DIR = Path("model")

JOBLIB_MODEL = MODEL_DIR / "Model.joblib"
FEATURES_FILE = MODEL_DIR / "features.csv"
ONNX_MODEL = MODEL_DIR / "Model.onnx"


model = joblib.load(JOBLIB_MODEL)

features = pd.read_csv(FEATURES_FILE)

n_features = features.shape[1]

# Dummy input only establishes the ONNX input shape/type.
dummy_input = np.zeros(
    (1, n_features),
    dtype=np.float32
)

onnx_model = to_onnx(
    model,
    dummy_input,
    target_opset=16,
    options={id(model): {"zipmap": False}}
)

# BrainFlow's ONNX classifier builds the input tensor from the declared shape
# and cannot create it when the batch dimension is dynamic (-1).
batch_dimension = onnx_model.graph.input[0].type.tensor_type.shape.dim[0]
batch_dimension.ClearField("dim_param")
batch_dimension.dim_value = 1
for output in onnx_model.graph.output:
    output_shape = output.type.tensor_type.shape
    if output_shape.dim:
        output_batch_dimension = output_shape.dim[0]
        output_batch_dimension.ClearField("dim_param")
        output_batch_dimension.dim_value = 1
onnx.checker.check_model(onnx_model)

onnx_bytes = onnx_model.SerializeToString()
ONNX_MODEL.write_bytes(onnx_bytes)

library_model = Path(__file__).resolve().parent.parent / "SimpleBCI.Library" / "Model" / "Model.onnx"
library_model.parent.mkdir(parents=True, exist_ok=True)
library_model.write_bytes(onnx_bytes)

print("ONNX export successful:")
print(ONNX_MODEL)
print(library_model)
