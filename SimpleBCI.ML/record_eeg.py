from __future__ import annotations

import argparse
import csv
import time
from datetime import datetime
from pathlib import Path

import numpy as np
from brainflow.board_shim import BoardIds, BoardShim, BrainFlowInputParams
from playsound3 import playsound


ROOT = Path(__file__).resolve().parent
AUDIO_DIR = ROOT / "audio"
OUTPUT_DIR = ROOT / "motor_imagery_recordings"

# This order is also the order used by train_model.py.
CLASSES = ("Neutral", "Left", "Right")
CHANNEL_NAMES = ("AF7", "AF8", "TP9", "TP10")
BOARD_OPTIONS = ("synthetic", "muse_s_athena")
CUE_FILES = {
    "Left": AUDIO_DIR / "Left.wav",
    "Right": AUDIO_DIR / "Right.wav",
    "Neutral": AUDIO_DIR / "Neutral.wav",
    "Break": AUDIO_DIR / "Break.wav",
}
MUSE_S_ATHENA_SAMPLE_RATE = 256


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Record labeled EEG samples from a BrainFlow board."
    )
    parser.add_argument(
        "--board",
        choices=BOARD_OPTIONS,
        default="synthetic",
        help="BrainFlow board to use (default: synthetic).",
    )
    parser.add_argument(
        "--mac-address",
        default="",
        help="Optional Muse S Athena Bluetooth MAC address.",
    )
    parser.add_argument(
        "--serial-number",
        default="",
        help="Optional Muse device name/serial number for discovery.",
    )
    parser.add_argument(
        "--seconds",
        type=float,
        default=10.0,
        help="Recording duration after each cue, in seconds (default: 10).",
    )
    parser.add_argument(
        "--rounds",
        type=int,
        default=5,
        help="Record each class once per round (default: 5).",
    )
    parser.add_argument(
        "--gap",
        type=float,
        default=2.0,
        help="Pause between trials, in seconds (default: 2).",
    )
    parser.add_argument("--seed", type=int, default=None)
    return parser.parse_args()


def load_cues() -> dict[str, Path]:
    cues = {}
    for label, path in CUE_FILES.items():
        if not path.is_file():
            raise FileNotFoundError(f"Missing cue WAV: {path}")
        cues[label] = path
    return cues


def play_cue(cue: Path) -> None:
    playsound(cue, block=True)


def main() -> int:
    args = parse_args()
    if args.seconds <= 0:
        raise ValueError("--seconds must be greater than 0.")
    if args.rounds <= 0:
        raise ValueError("--rounds must be greater than 0.")
    if args.gap < 0:
        raise ValueError("--gap cannot be negative.")

    cues = load_cues()
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    session_id = datetime.now().strftime("session_%Y%m%d_%H%M%S")
    output_path = OUTPUT_DIR / f"{session_id}.csv"
    metadata_path = OUTPUT_DIR / f"{session_id}_metadata.txt"

    params = BrainFlowInputParams()
    if args.board == "muse_s_athena":
        params.other_info = "preset=p1041;low_latency=true"
        if args.mac_address:
            params.mac_address = args.mac_address
        if args.serial_number:
            params.serial_number = args.serial_number
        board_id = BoardIds.MUSE_S_ATHENA_BOARD.value
    else:
        board_id = BoardIds.SYNTHETIC_BOARD.value

    board = BoardShim(board_id, params)
    rows_written = 0
    trial_id = 0

    try:
        board.prepare_session()
        board.start_stream(45000, "")
        time.sleep(1.0)

        eeg_rows = BoardShim.get_eeg_channels(board_id)
        if args.board == "muse_s_athena":
            # BrainFlow reports Muse channels by name. Reorder them to match
            # the training schema, regardless of BrainFlow's native order.
            eeg_names = BoardShim.get_eeg_names(board_id)
            row_by_name = {
                name.strip().upper(): row
                for name, row in zip(eeg_names, eeg_rows)
            }
            missing_channels = [
                name for name in CHANNEL_NAMES if name.upper() not in row_by_name
            ]
            if missing_channels:
                raise RuntimeError(
                    "Muse S Athena did not expose the expected EEG channels "
                    f"{CHANNEL_NAMES}. BrainFlow reported: {eeg_names}"
                )
            eeg_rows = [row_by_name[name.upper()] for name in CHANNEL_NAMES]
        else:
            eeg_rows = eeg_rows[:len(CHANNEL_NAMES)]
        timestamp_row = BoardShim.get_timestamp_channel(board_id)
        sampling_rate = BoardShim.get_sampling_rate(board_id)

        if len(eeg_rows) != len(CHANNEL_NAMES):
            raise RuntimeError("The selected board must provide four EEG channels.")
        if (
            args.board == "muse_s_athena"
            and sampling_rate != MUSE_S_ATHENA_SAMPLE_RATE
        ):
            raise RuntimeError(
                "Expected Muse S Athena at "
                f"{MUSE_S_ATHENA_SAMPLE_RATE} Hz, but the board reports {sampling_rate} Hz."
            )

        # Schema: one row per sample; label and trial_id identify the trial.
        fieldnames = [
            "session_id", "trial_id", "sample_index", "timestamp", "sampling_rate", "label",
            *CHANNEL_NAMES,
        ]
        rng = np.random.default_rng(args.seed)

        with output_path.open("w", newline="", encoding="utf-8") as csv_file:
            writer = csv.DictWriter(
                csv_file, fieldnames=fieldnames, delimiter=";"
            )
            writer.writeheader()

            for round_number in range(1, args.rounds + 1):
                order = list(CLASSES)
                rng.shuffle(order)

                for label in order:
                    trial_id += 1
                    print(
                        f"\nRound {round_number}/{args.rounds} — "
                        f"Trial {trial_id}: {label}"
                    )
                    play_cue(cues[label])

                    # Empty the buffer so cue playback is not labeled as EEG
                    # from the following trial.
                    board.get_board_data()

                    started = time.monotonic()
                    sample_index = 0
                    while time.monotonic() - started < args.seconds:
                        data = board.get_board_data()
                        if data.size == 0:
                            time.sleep(0.01)
                            continue

                        timestamps = (
                            data[timestamp_row, :]
                            if timestamp_row >= 0
                            else np.full(data.shape[1], np.nan)
                        )

                        for column in range(data.shape[1]):
                            row = {
                                "session_id": session_id,
                                "trial_id": trial_id,
                                "sample_index": sample_index,
                                "timestamp": float(timestamps[column]),
                                "sampling_rate": sampling_rate,
                                "label": label,
                            }
                            for channel_name, brainflow_row in zip(
                                CHANNEL_NAMES, eeg_rows
                            ):
                                row[channel_name] = float(data[brainflow_row, column])
                            writer.writerow(row)
                            sample_index += 1
                            rows_written += 1

                        csv_file.flush()

                    print(f"Saved {sample_index} samples for {label}.")
                    if args.gap:
                        time.sleep(args.gap)

            play_cue(cues["Break"])

        metadata_path.write_text(
            "\n".join(
                [
                    f"created_at={datetime.now().isoformat(timespec='seconds')}",
                    f"session_id={session_id}",
                    f"board={args.board}",
                    f"sampling_rate={sampling_rate}",
                    f"channel_names={','.join(CHANNEL_NAMES)}",
                    f"brainflow_eeg_rows={','.join(map(str, eeg_rows))}",
                    f"seconds_per_trial={args.seconds}",
                    f"rounds={args.rounds}",
                    f"classes={','.join(CLASSES)}",
                    f"rows_written={rows_written}",
                    *(
                        [
                            "note=For the synthetic board, AF7/AF8/TP9/TP10 are column placeholders, not physical electrode measurements.",
                            "note=Synthetic data tests the software pipeline; it is not meaningful motor-imagery training data.",
                        ]
                        if args.board == "synthetic"
                        else ["brainflow_other_info=preset=p1041;low_latency=true"]
                    ),
                ]
            ) + "\n",
            encoding="utf-8",
        )

        print(f"\nRecording saved to: {output_path}")
        print(f"Metadata saved to: {metadata_path}")
        return 0
    finally:
        if board.is_prepared():
            board.stop_stream()
            board.release_session()


if __name__ == "__main__":
    raise SystemExit(main())
