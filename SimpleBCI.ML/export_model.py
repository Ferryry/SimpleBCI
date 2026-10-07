from __future__ import annotations

from pathlib import Path

import joblib
import numpy as np
import onnx
import onnxruntime as ort
from brainflow.ml_model import BrainFlowClassifiers, BrainFlowMetrics, BrainFlowModelParams, MLModel
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType


ROOT = Path(__file__).resolve().parent
MODEL_DIR = ROOT / "trained_model"
MODEL_PATH = MODEL_DIR / "model.joblib"
EXAMPLE_PATH = MODEL_DIR / "feature_example.npy"
OUTPUT_PATH = ROOT.parent / "SimpleBCI.Library" / "Model" / "Model.onnx"

NUMBER_OF_FEATURES = 16
NUMBER_OF_CLASSES = 3
PROBABILITY_OUTPUT_NAME = "probabilities"


def main() -> None:
    if not MODEL_PATH.is_file():
        raise FileNotFoundError(
            f"Trained model not found: {MODEL_PATH}\n"
            "Run train_model.py first."
        )
    if not EXAMPLE_PATH.is_file():
        raise FileNotFoundError(
            f"Training example not found: {EXAMPLE_PATH}\n"
            "Run train_model.py again to save the example input."
        )

    model = joblib.load(MODEL_PATH)
    if list(model.classes_) != [0, 1, 2]:
        raise ValueError(
            f"Expected numeric classes [0, 1, 2], got {list(model.classes_)}. "
            "These correspond to Neutral, Left, Right."
        )

    # BrainFlow's ONNX classifier supports the probability tensor when ZipMap
    # is disabled. Opset 11 follows BrainFlow's documented compatibility guide.
    initial_types = [
        ("float_input", FloatTensorType([1, NUMBER_OF_FEATURES]))
    ]
    onnx_model = convert_sklearn(
        model,
        initial_types=initial_types,
        target_opset=11,
        options={id(model): {"zipmap": False}},
    )

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    onnx.save_model(onnx_model, str(OUTPUT_PATH))

    checked_model = onnx.load(str(OUTPUT_PATH))
    onnx.checker.check_model(checked_model)

    example = np.load(EXAMPLE_PATH).astype(np.float32)
    if example.shape != (1, NUMBER_OF_FEATURES):
        raise ValueError(
            f"Expected example shape (1, {NUMBER_OF_FEATURES}), got {example.shape}."
        )

    # First validate the exported model with ONNX Runtime.
    session = ort.InferenceSession(
        str(OUTPUT_PATH), providers=["CPUExecutionProvider"]
    )
    input_name = session.get_inputs()[0].name
    output_names = [output.name for output in session.get_outputs()]
    if PROBABILITY_OUTPUT_NAME not in output_names:
        raise RuntimeError(
            f"Expected an ONNX output named '{PROBABILITY_OUTPUT_NAME}', "
            f"but found {output_names}. The C# API is configured to read "
            "the 'probabilities' output."
        )
    onnx_probabilities = session.run(
        [PROBABILITY_OUTPUT_NAME], {input_name: example}
    )[0]
    if onnx_probabilities.shape != (1, NUMBER_OF_CLASSES):
        raise RuntimeError(
            f"Expected probability output shape (1, {NUMBER_OF_CLASSES}), "
            f"got {onnx_probabilities.shape}."
        )

    # Also exercise BrainFlow's ONNX wrapper, the same kind of loader the
    # SimpleBCI C# API uses. BrainFlow takes a flattened feature vector.
    params = BrainFlowModelParams(
        BrainFlowMetrics.USER_DEFINED,
        BrainFlowClassifiers.ONNX_CLASSIFIER,
    )
    params.file = str(OUTPUT_PATH)
    params.output_name = PROBABILITY_OUTPUT_NAME
    brainflow_model = MLModel(params)
    brainflow_model.prepare()
    try:
        brainflow_probabilities = np.asarray(
            brainflow_model.predict(example.reshape(-1)), dtype=np.float32
        )
    finally:
        brainflow_model.release()

    expected = onnx_probabilities.reshape(-1)
    if brainflow_probabilities.shape != (NUMBER_OF_CLASSES,):
        raise RuntimeError(
            "BrainFlow returned an unexpected prediction shape: "
            f"{brainflow_probabilities.shape}."
        )
    if not np.allclose(brainflow_probabilities, expected, rtol=1e-4, atol=1e-6):
        raise RuntimeError(
            "BrainFlow and ONNX Runtime returned different class probabilities. "
            f"ONNX Runtime: {expected}; BrainFlow: {brainflow_probabilities}"
        )

    print("ONNX export and BrainFlow inference check succeeded.")
    print(f"Model: {OUTPUT_PATH}")
    print(f"Input: {session.get_inputs()[0].shape} ({input_name})")
    print(f"Probability output: {PROBABILITY_OUTPUT_NAME}")
    print("Class order: 0=Neutral, 1=Left, 2=Right")
    print(f"Example probabilities: {brainflow_probabilities.tolist()}")


if __name__ == "__main__":
    main()
