from pathlib import Path
from datetime import datetime
import csv
import time

from brainflow.board_shim import BoardShim, BrainFlowInputParams, BoardIds, BrainFlowPresets

FS = 256
CHANNELS = ["TP9", "AF7", "AF8", "TP10"]
DATASET = Path("dataset") / "train"

LABEL = "Neutral"
TRIALS = 10
DURATION = 20
REST = 10

(DATASET / LABEL.lower()).mkdir(parents=True, exist_ok=True)


def record():
    params = BrainFlowInputParams()

    # If necessary:
    # params.mac_address = "YOUR_MAC_ADDRESS"
    # params.serial_number = "YOUR_SERIAL_NUMBER"

    # Athena-specific connection options used in the previous notebook.
    params.other_info = "preset=p21;low_latency=true"

    board_id = BoardIds.MUSE_S_ATHENA_BOARD.value
    board = BoardShim(board_id, params)

    eeg_channels = BoardShim.get_eeg_channels(
        board_id,
        BrainFlowPresets.DEFAULT_PRESET
    )

    print(f"Connecting to Muse S Athena...")
    print(f"EEG channels: {eeg_channels}")

    board.prepare_session()
    board.start_stream(45000)

    try:
        for trial in range(1, TRIALS + 1):
            print()
            print("=" * 60)
            print(f"{LABEL.upper()} - Trial {trial}/{TRIALS}")
            print(f"Think about: {LABEL}")
            print("=" * 60)

            chunks = []
            start = time.time()

            while time.time() - start < DURATION:
                data = board.get_board_data(
                    preset=BrainFlowPresets.DEFAULT_PRESET
                )

                if data.shape[1] > 0:
                    chunks.append(data[eeg_channels, :])

                time.sleep(0.05)

            if not chunks:
                print("WARNING: No EEG data received.")
                continue

            eeg = __import__("numpy").concatenate(chunks, axis=1).T

            timestamp = datetime.now().strftime("%Y-%m-%d_%H_%M_%S_%f")
            filename = DATASET / LABEL.lower() / f"{LABEL}.{timestamp}.csv"

            with open(filename, "w", newline="") as f:
                writer = csv.writer(f)
                writer.writerow(
                    ["timestamp"] + [f"RAW_{c}" for c in CHANNELS]
                )

                for i, row in enumerate(eeg):
                    writer.writerow([i] + row.tolist())

            expected = DURATION * FS

            print(f"Saved: {filename}")
            print(f"Samples: {len(eeg)} / expected ~{expected}")

            if trial < TRIALS:
                print(f"Resting for {REST} seconds...")
                time.sleep(REST)

    finally:
        try:
            board.stop_stream()
        except Exception:
            pass

        board.release_session()

    print("Recording finished.")


if __name__ == "__main__":
    record()
