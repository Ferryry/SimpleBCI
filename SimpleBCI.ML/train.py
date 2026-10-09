from pathlib import Path
import json
import joblib
import numpy as np
import onnx
import pandas as pd

from brainflow.data_filter import DataFilter, DetrendOperations, FilterTypes, WindowOperations
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import accuracy_score, classification_report, confusion_matrix
from skl2onnx import to_onnx

SCRIPT_DIR = Path(__file__).resolve().parent
TRAIN_DIR = SCRIPT_DIR / "dataset" / "train"
TEST_DIR = SCRIPT_DIR / "dataset" / "test"
MODEL_DIR = SCRIPT_DIR / "model"
LIBRARY_MODEL_PATH = (
    SCRIPT_DIR.parent / "SimpleBCI.Library" / "Model" / "Model.onnx"
)
MODEL_DIR.mkdir(exist_ok=True)

FS = 256
CHANNELS = ["TP9", "AF7", "AF8", "TP10"]
LABELS = {"Left": 0, "Neutral": 1, "Right": 2}

WINDOW_SAMPLES = 512
STEP_SAMPLES = 256

WELCH_NFFT = 256
WELCH_OVERLAP = 128
WELCH_WINDOW = WindowOperations.HANNING.value

BANDS = {
    "theta": (4.0, 8.0),
    "alpha": (8.0, 13.0),
    "beta": (13.0, 30.0),
}

FEATURE_NAMES = [
    f"{ch}_{band}_relative_power"
    for ch in CHANNELS
    for band in BANDS
]
FEATURE_NAMES += [
    feature_name
    for band in BANDS
    for feature_name in (
        f"AF7_AF8_{band}_log_ratio",
        f"TP9_TP10_{band}_log_ratio",
    )
]


def preprocess_channel(signal):
    x = np.asarray(signal, dtype=np.float64).copy()

    DataFilter.detrend(
        x,
        DetrendOperations.CONSTANT.value,
    )

    DataFilter.perform_bandpass(
        x,
        FS,
        4.0,
        30.0,
        4,
        FilterTypes.BUTTERWORTH.value,
        0.0,
    )

    # No 48-52 Hz notch: the 4-30 Hz bandpass already removes 50 Hz.
    return x


def band_power(signal, low, high):
    psd = DataFilter.get_psd_welch(
        signal,
        WELCH_NFFT,
        WELCH_OVERLAP,
        FS,
        WELCH_WINDOW,
    )
    return float(DataFilter.get_band_power(psd, low, high))


def extract_features(eeg_window):
    if eeg_window.shape != (4, WINDOW_SAMPLES):
        raise ValueError(
            f"Expected (4, {WINDOW_SAMPLES}), got {eeg_window.shape}"
        )

    processed = np.empty_like(eeg_window, dtype=np.float64)

    for ch in range(4):
        processed[ch] = preprocess_channel(eeg_window[ch])

    relative = np.zeros((4, len(BANDS)), dtype=np.float64)
    band_items = list(BANDS.items())

    for ch in range(4):
        total = max(band_power(processed[ch], 4.0, 30.0), 1e-12)

        for b, (_, (low, high)) in enumerate(band_items):
            relative[ch, b] = band_power(
                processed[ch], low, high
            ) / total

    features = []

    # 12 relative-bandpower features
    for ch in range(4):
        for b in range(len(BANDS)):
            features.append(relative[ch, b])

    # 6 asymmetry features
    for b in range(len(BANDS)):
        features.append(
            np.log((relative[1, b] + 1e-12) /
                   (relative[2, b] + 1e-12))
        )
        features.append(
            np.log((relative[0, b] + 1e-12) /
                   (relative[3, b] + 1e-12))
        )

    return np.asarray(features, dtype=np.float32)


def load_dataset(dataset_dir):
    X, y = [], []
    recording_count = 0

    for label_name, label_value in LABELS.items():
        directory = dataset_dir / label_name.lower()

        if not directory.exists():
            print(f"Warning: missing directory: {directory}")
            continue

        for csv_file in sorted(directory.glob("*.csv")):
            df = pd.read_csv(csv_file)

            columns = ["RAW_TP9", "RAW_AF7", "RAW_AF8", "RAW_TP10"]
            missing = [c for c in columns if c not in df.columns]

            if missing:
                raise ValueError(
                    f"{csv_file}: missing columns {missing}"
                )

            data = df[columns].to_numpy(dtype=np.float64).T

            if data.shape[1] < WINDOW_SAMPLES:
                print(f"Skipping {csv_file}: too short")
                continue

            recording_count += 1
            for start in range(
                0,
                data.shape[1] - WINDOW_SAMPLES + 1,
                STEP_SAMPLES,
            ):
                window = data[:, start:start + WINDOW_SAMPLES]
                X.append(extract_features(window))
                y.append(label_value)

    if not X:
        raise RuntimeError(f"No recordings found under {dataset_dir}.")

    return (
        np.asarray(X, dtype=np.float32),
        np.asarray(y, dtype=np.int64),
        recording_count,
    )


def main():
    X_train, y_train, train_recording_count = load_dataset(TRAIN_DIR)
    X_test, y_test, test_recording_count = load_dataset(TEST_DIR)

    print(f"Training: {X_train.shape}")
    print(f"Testing:  {X_test.shape}")
    print(f"Training recordings: {train_recording_count}")
    print(f"Testing recordings:  {test_recording_count}")

    missing_train_classes = set(LABELS.values()) - set(y_train.tolist())
    missing_test_classes = set(LABELS.values()) - set(y_test.tolist())
    if missing_train_classes:
        raise RuntimeError(
            f"Training data is missing classes: {sorted(missing_train_classes)}"
        )
    if missing_test_classes:
        raise RuntimeError(
            f"Test data is missing classes: {sorted(missing_test_classes)}"
        )

    model = RandomForestClassifier(
        n_estimators=300,
        min_samples_leaf=2,
        class_weight="balanced",
        random_state=42,
        n_jobs=-1,
    )

    print("Training Random Forest...")
    model.fit(X_train, y_train)

    pred = model.predict(X_test)

    print(f"Accuracy: {accuracy_score(y_test, pred):.4f}")
    print()
    print(classification_report(
        y_test,
        pred,
        target_names=["Left", "Neutral", "Right"],
    ))
    print("Confusion matrix:")
    print(confusion_matrix(y_test, pred))

    joblib.dump(model, MODEL_DIR / "Model.joblib")

    metadata = {
        "feature_count": len(FEATURE_NAMES),
        "features": FEATURE_NAMES,
        "sampling_rate": FS,
        "window_samples": WINDOW_SAMPLES,
        "step_samples": STEP_SAMPLES,
        "welch_nfft": WELCH_NFFT,
        "welch_overlap": WELCH_OVERLAP,
        "welch_window": "HANNING",
        "bands": BANDS,
        "labels": LABELS,
    }

    (MODEL_DIR / "features.json").write_text(
        json.dumps(metadata, indent=2),
        encoding="utf-8",
    )

    onnx_model = to_onnx(
        model,
        X_train[:1].astype(np.float32),
        target_opset=16,
        options={id(model): {"zipmap": False}},
    )
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
    (MODEL_DIR / "Model.onnx").write_bytes(onnx_bytes)
    LIBRARY_MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
    LIBRARY_MODEL_PATH.write_bytes(onnx_bytes)

    pd.DataFrame(X_train, columns=FEATURE_NAMES).to_csv(
        MODEL_DIR / "features.csv",
        index=False,
    )

    pd.DataFrame({
        "label": y_train,
        "label_name": [
            next(name for name, value in LABELS.items() if value == v)
            for v in y_train
        ],
    }).to_csv(MODEL_DIR / "labels.csv", index=False)

    print("\nSaved:")
    print("  Model/Model.onnx")
    print("  Model/Model.joblib")
    print("  Model/features.json")
    print("  Model/features.csv")
    print("  Model/labels.csv")
    print(f"  {LIBRARY_MODEL_PATH}")


if __name__ == "__main__":
    main()
