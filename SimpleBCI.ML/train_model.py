from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pandas as pd
from scipy.signal import welch
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import accuracy_score, classification_report
from sklearn.model_selection import GroupShuffleSplit


ROOT = Path(__file__).resolve().parent
DATA_DIR = ROOT / "motor_imagery_recordings"
MODEL_DIR = ROOT / "trained_model"

WINDOW_SECONDS = 2
CHANNELS = ("AF7", "AF8", "TP9", "TP10")

# These numeric IDs deliberately match the order of the C# BrainState enum:
# Neutral = 0, Left = 1, Right = 2.
CLASS_TO_ID = {"Neutral": 0, "Left": 1, "Right": 2}
ID_TO_CLASS = {value: key for key, value in CLASS_TO_ID.items()}
BANDS = ((4, 8), (8, 13), (13, 30), (30, 45))


def extract_features(window: np.ndarray, sampling_rate: int) -> np.ndarray:
    """Return 16 features: four log band-powers for each of four channels."""
    features = []

    for channel_index in range(window.shape[1]):
        signal = window[:, channel_index].astype(np.float64)
        signal -= np.mean(signal)

        frequencies, power = welch(
            signal,
            fs=sampling_rate,
            nperseg=min(256, len(signal)),
        )

        for low, high in BANDS:
            mask = (frequencies >= low) & (frequencies < high)
            band_power = (
                np.trapezoid(power[mask], frequencies[mask])
                if np.any(mask)
                else 0.0
            )
            features.append(np.log10(max(float(band_power), 1e-12)))

    return np.asarray(features, dtype=np.float32)


def load_dataset() -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    if not DATA_DIR.exists():
        raise FileNotFoundError(f"Recording directory not found: {DATA_DIR}")

    csv_files = sorted(DATA_DIR.glob("*.csv"))
    if not csv_files:
        raise RuntimeError(f"No recording CSV files found in {DATA_DIR}")

    required_columns = {
        "session_id", "trial_id", "sample_index", "timestamp", "sampling_rate", "label",
        *CHANNELS,
    }
    X: list[np.ndarray] = []
    y: list[int] = []
    groups: list[str] = []
    sample_rates: list[int] = []

    for csv_path in csv_files:
        # RecordEEG.py writes semicolon-delimited CSV files.
        frame = pd.read_csv(csv_path, sep=";")
        missing = sorted(required_columns - set(frame.columns))
        if missing:
            print(f"Skipping {csv_path.name}; missing columns: {missing}")
            continue

        frame["session_id"] = frame["session_id"].astype(str)
        session_ids = frame["session_id"].unique()
        if len(session_ids) != 1:
            raise ValueError(
                f"{csv_path.name} must contain exactly one session_id; found {session_ids}"
            )
        session_id = session_ids[0]

        for column in ("trial_id", "sample_index", "sampling_rate", *CHANNELS):
            frame[column] = pd.to_numeric(frame[column], errors="coerce")
        if frame[["trial_id", "sample_index", "sampling_rate", *CHANNELS]].isna().any().any():
            raise ValueError(f"{csv_path.name} contains missing or non-numeric values.")
        if not frame["label"].isin(CLASS_TO_ID).all():
            unknown = sorted(set(frame["label"]) - set(CLASS_TO_ID))
            raise ValueError(f"{csv_path.name} contains unknown labels: {unknown}")

        windows_in_file = 0
        for trial_id, trial in frame.groupby("trial_id", sort=True):
            labels = trial["label"].unique()
            if len(labels) != 1:
                raise ValueError(
                    f"Trial {trial_id} in {csv_path.name} has multiple labels: {labels}"
                )

            trial = trial.sort_values("sample_index")
            trial_sample_rates = trial["sampling_rate"].unique()
            if len(trial_sample_rates) != 1:
                raise ValueError(
                    f"Trial {trial_id} in {csv_path.name} has inconsistent sampling rates: "
                    f"{trial_sample_rates}"
                )
            sampling_rate = int(trial_sample_rates[0])
            if sampling_rate <= 0:
                raise ValueError(f"Invalid sampling rate in {csv_path.name}: {sampling_rate}")
            window_samples = sampling_rate * WINDOW_SECONDS
            sample_indices = trial["sample_index"].to_numpy(dtype=np.int64)
            if len(sample_indices) > 1 and not np.all(np.diff(sample_indices) == 1):
                raise ValueError(
                    f"Sample indices are not continuous in trial {trial_id} of {csv_path.name}."
                )

            eeg = trial[list(CHANNELS)].to_numpy(dtype=np.float64)
            label_id = CLASS_TO_ID[labels[0]]

            # Do not allow a feature window to cross a trial/class boundary.
            for start in range(0, len(eeg) - window_samples + 1, window_samples):
                X.append(
                    extract_features(eeg[start:start + window_samples], sampling_rate)
                )
                y.append(label_id)
                groups.append(session_id)
                sample_rates.append(sampling_rate)
                windows_in_file += 1

        print(f"Read {csv_path.name}: {windows_in_file} windows, session {session_id}")

    if not X:
        raise RuntimeError(
            "No windows were created. Each trial needs at least "
            f"at least {WINDOW_SECONDS} seconds of data and the CSV must use the expected schema."
        )

    return (
        np.asarray(X, dtype=np.float32),
        np.asarray(y, dtype=np.int64),
        np.asarray(groups),
        np.asarray(sample_rates, dtype=np.int64),
    )


def main() -> None:
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    X, y, groups, sample_rates = load_dataset()

    print(f"\nWindows: {len(X)}")
    print(f"Features per window: {X.shape[1]}")
    print(f"Classes: {[ID_TO_CLASS[i] for i in np.unique(y)]}")

    session_count = len(np.unique(groups))
    if session_count < 2:
        raise RuntimeError(
            "At least two separate recording sessions are needed for a session-level "
            "train/test split. Record another session before training."
        )
    if set(np.unique(y)) != set(ID_TO_CLASS):
        raise RuntimeError(
            "The dataset must contain at least one complete window for each class: "
            "Neutral, Left and Right."
        )

    splitter = GroupShuffleSplit(n_splits=1, test_size=0.25, random_state=42)
    train_indices, test_indices = next(splitter.split(X, y, groups=groups))
    X_train, X_test = X[train_indices], X[test_indices]
    y_train, y_test = y[train_indices], y[test_indices]

    if set(np.unique(y_train)) != set(ID_TO_CLASS):
        raise RuntimeError(
            "The training sessions do not contain all three classes. "
            "Record more balanced sessions."
        )
    if set(np.unique(y_test)) != set(ID_TO_CLASS):
        raise RuntimeError(
            "The held-out test sessions do not contain all three classes. "
            "Record more balanced sessions or collect more sessions."
        )

    model = RandomForestClassifier(
        n_estimators=300,
        class_weight="balanced",
        random_state=42,
        n_jobs=-1,
    )
    model.fit(X_train, y_train)

    predictions = model.predict(X_test)
    labels = list(ID_TO_CLASS)
    print("\nSession-held-out test accuracy:", f"{accuracy_score(y_test, predictions):.3f}")
    print(
        classification_report(
            y_test,
            predictions,
            labels=labels,
            target_names=[ID_TO_CLASS[i] for i in labels],
            zero_division=0,
        )
    )

    model_path = MODEL_DIR / "model.joblib"
    example_path = MODEL_DIR / "feature_example.npy"
    metadata_path = MODEL_DIR / "model_metadata.json"
    import joblib

    joblib.dump(model, model_path)
    np.save(example_path, X_train[:1])
    metadata_path.write_text(
        json.dumps(
            {
                "classes": CLASS_TO_ID,
                "channels": list(CHANNELS),
                "sample_rates_hz": sorted(np.unique(sample_rates).tolist()),
                "window_seconds": WINDOW_SECONDS,
                "window_samples_by_rate": {
                    str(rate): rate * WINDOW_SECONDS
                    for rate in sorted(np.unique(sample_rates).tolist())
                },
                "bands_hz": [list(band) for band in BANDS],
                "feature_count": X.shape[1],
            },
            indent=2,
        ) + "\n",
        encoding="utf-8",
    )

    print(f"\nTrained model: {model_path}")
    print(f"Example input: {example_path}")
    print(f"Model metadata: {metadata_path}")


if __name__ == "__main__":
    main()
