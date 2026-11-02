"""Record five seconds of EEG and highlight blink-like peaks on AF7/AF8.

Install dependencies with:
    py -m pip install brainflow scipy numpy matplotlib

Run with:
    py -m pip install brainflow scipy numpy
    py tools/muse_blink_peak_monitor.py

Muse S Athena input is read through BrainFlow. Use --channels to change the
two 1-based EEG input numbers used for peak detection (defaults to AF7/AF8).
"""

from __future__ import annotations

import argparse
import csv
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

import numpy as np
import matplotlib.pyplot as plt
from brainflow.board_shim import BoardShim, BrainFlowInputParams, BoardIds
from scipy.ndimage import uniform_filter1d
from scipy.signal import find_peaks


@dataclass(frozen=True)
class Peak:
    channel: str
    timestamp: float
    polarity: str
    amplitude: float
    duration_ms: float
    prominence: float


def plot_signal_csv(path: Path, save_path: Path | None = None) -> None:
    """Plot the two saved EEG channels with peak markers."""
    series: dict[str, tuple[list[float], list[float], list[bool]]] = {}
    with path.open(newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        required = {"sample_no", "timestamp_s", "channel", "amplitude_uv", "is_peak"}
        if not reader.fieldnames or not required.issubset(reader.fieldnames):
            raise ValueError("CSV needs sample_no, timestamp_s, channel, amplitude_uv, and is_peak columns")
        for row in reader:
            channel = row["channel"]
            times, values, peaks = series.setdefault(channel, ([], [], []))
            times.append(float(row["timestamp_s"]))
            values.append(float(row["amplitude_uv"]))
            peaks.append(row["is_peak"].strip().lower() == "true")

    if not series:
        raise ValueError(f"No signal samples found in {path}")
    fig, axes = plt.subplots(len(series), 1, figsize=(11, 6), sharex=True, squeeze=False)
    for ax, (channel, (times, values, peaks)) in zip(axes[:, 0], sorted(series.items())):
        ax.plot(times, values, color="#245a81", linewidth=0.8, label=f"{channel} amplitude")
        peak_times = [t for t, mark in zip(times, peaks) if mark]
        peak_values = [v for v, mark in zip(values, peaks) if mark]
        if peak_times:
            ax.scatter(peak_times, peak_values, marker="x", s=45, color="#c43b3b",
                       label="detected peak", zorder=3)
        ax.set_ylabel("Amplitude (µV)")
        ax.set_title(channel)
        ax.grid(True, alpha=0.25)
        ax.legend(loc="upper right")
    axes[-1, 0].set_xlabel("Time from recording start (s)")
    fig.suptitle(f"EEG blink recording — {path.stem}")
    fig.tight_layout()
    if save_path is not None:
        fig.savefig(save_path, dpi=300, bbox_inches="tight")
        print(f"Plot saved to {save_path}")
    plt.show()


def find_channel_peaks(
    signal: np.ndarray,
    timestamps: np.ndarray,
    sampling_rate: int,
    channel_name: str,
    threshold: float,
    min_duration: float,
    max_duration: float,
    min_interval: float,
) -> list[Peak]:
    """Find positive and negative peaks; measure width at half prominence."""
    if signal.size < 3:
        return []

    # Remove slow baseline drift with a centered moving average. Edge samples
    # use the nearest available baseline value.
    window_size = max(3, round(sampling_rate * 0.5))
    if window_size % 2 == 0:
        window_size += 1
    # uniform_filter1d preserves the input length, even before the stream has
    # filled a complete baseline window.
    baseline = uniform_filter1d(signal, size=window_size, mode="nearest")
    corrected = signal - baseline

    min_distance = max(1, round(sampling_rate * min_interval))
    min_width = max(1, sampling_rate * min_duration)
    max_width = sampling_rate * max_duration
    found: list[Peak] = []

    for polarity, candidate in (("+", corrected), ("-", -corrected)):
        indices, properties = find_peaks(
            candidate,
            height=threshold,
            prominence=threshold * 0.5,
            distance=min_distance,
            width=(min_width, max_width),
            rel_height=0.5,
        )

        widths_samples = properties["widths"]
        prominences = properties["prominences"]
        for peak_position, (idx, width_samples, prominence) in enumerate(
            zip(indices, widths_samples, prominences)
        ):
            # Ignore candidates whose measured half-prominence width touches
            # the available data boundary (the event may be truncated).
            left = int(np.floor(properties["left_ips"][peak_position]))
            right = int(np.ceil(properties["right_ips"][peak_position]))
            if left <= 0 or right >= corrected.size - 1:
                continue

            found.append(
                Peak(
                    channel=channel_name,
                    timestamp=float(timestamps[idx]),
                    polarity=polarity,
                    amplitude=float(corrected[idx]),
                    duration_ms=float(width_samples * 1000.0 / sampling_rate),
                    prominence=float(prominence),
                )
            )

    return sorted(found, key=lambda item: item.timestamp)


def main(default_device: str = "muse") -> int:
    device_name = "Muse S Athena" if default_device == "muse" else "NeuroPawn Knight"
    parser = argparse.ArgumentParser(description=f"Record exactly five seconds of {device_name} EEG.")
    parser.add_argument("--device", choices=("muse", "neuropawn"), default=default_device)
    parser.add_argument("--plot-csv", metavar="FILE", help="Plot a recording CSV and exit")
    parser.add_argument("--save-plot", metavar="FILE", help="Also save the plot as PNG or PDF")
    parser.add_argument("--port", default="COM4", help="NeuroPawn serial port (default: COM4)")
    parser.add_argument("--channels", default=None, help="1-based AF7,AF8 inputs (Muse: 2,3; NeuroPawn: 7,8)")
    parser.add_argument("--gain", type=int, default=1, help="NeuroPawn Knight gain (default: 12)")
    parser.add_argument("--threshold", type=float, default=200.0, help="Peak magnitude threshold in µV")
    parser.add_argument("--min-duration", type=float, default=0.05, help="Minimum peak width in seconds")
    parser.add_argument("--max-duration", type=float, default=0.30, help="Maximum peak width in seconds")
    parser.add_argument("--min-interval", type=float, default=0.20, help="Minimum peak spacing in seconds")
    parser.add_argument("--csv", default=None, help="CSV output path (defaults to a device-specific filename)")
    args = parser.parse_args()

    if args.plot_csv:
        output = Path(args.save_plot).expanduser() if args.save_plot else None
        plot_signal_csv(Path(args.plot_csv).expanduser(), output)
        return 0
    if args.csv is None:
        prefix = "muse" if args.device == "muse" else "neuropawn"
        run_id = datetime.now().astimezone().strftime("%Y%m%d_%H%M%S")
        args.csv = f"{prefix}_5s_{run_id}.csv"
    if args.channels is None:
        args.channels = "2,3" if args.device == "muse" else "1,2"
    try:
        channels = [int(part.strip()) for part in args.channels.split(",")]
    except ValueError:
        parser.error("--channels must be two comma-separated channel numbers, e.g. 2,3")
    if len(channels) != 2 or any(channel < 1 or channel > 8 for channel in channels):
        parser.error("--channels must contain exactly two values in the range 1..8")
    if args.min_duration <= 0 or args.max_duration <= args.min_duration:
        parser.error("duration range must satisfy 0 < --min-duration < --max-duration")

    board_id = BoardIds.MUSE_S_ATHENA_BOARD if args.device == "muse" else BoardIds.NEUROPAWN_KNIGHT_BOARD
    params = BrainFlowInputParams()
    if args.device == "muse":
        params.other_info = "preset=p1041;low_latency=true"
    else:
        params.serial_port = args.port
    board = BoardShim(board_id, params)
    started = False
    try:
        BoardShim.enable_board_logger()
        board.prepare_session()
        started = True
        board.start_stream(45000)
        if args.device == "neuropawn":
            time.sleep(2.0)
            for channel in channels:
                board.config_board(f"chon_{channel}_{args.gain}")
                time.sleep(1.0)

        sampling_rate = BoardShim.get_sampling_rate(board.get_board_id())
        eeg_rows = BoardShim.get_eeg_channels(board.get_board_id())
        if max(channels) > len(eeg_rows):
            raise RuntimeError(f"Board exposes {len(eeg_rows)} EEG inputs; requested channel {max(channels)}.")
        channel_rows = [eeg_rows[channel - 1] for channel in channels]
        target_samples = round(5.0 * sampling_rate)

        # Drain setup/startup data so sample 0 is the beginning of this recording.
        board.get_board_data()
        print(f"{device_name} connected at {sampling_rate} Hz; recording {target_samples} samples per channel.")
        print("AF7 and AF8 values are in µV. Keep still, then blink as desired.\n")

        chunks: list[np.ndarray] = []
        collected = 0
        capture_started = time.monotonic()
        last_status = capture_started
        while collected < target_samples:
            if time.monotonic() - capture_started > 10.0:
                raise TimeoutError(f"Only received {collected}/{target_samples} samples within 10 seconds.")
            fresh = board.get_board_data()
            if fresh.shape[1] == 0:
                time.sleep(0.01)
                continue
            take = min(fresh.shape[1], target_samples - collected)
            chunk = fresh[:, :take]
            chunks.append(chunk)
            collected += take
            if time.monotonic() - last_status >= 1.0:
                print(f"[{collected / sampling_rate:.1f}s] AF7 {chunk[channel_rows[0], -1]:+.1f} µV | "
                      f"AF8 {chunk[channel_rows[1], -1]:+.1f} µV", flush=True)
                last_status = time.monotonic()

        data = np.concatenate(chunks, axis=1)
        sample_no = np.arange(target_samples, dtype=int)
        timestamps = sample_no / sampling_rate
        peaks_by_channel: dict[str, list[Peak]] = {}
        peak_flags: dict[str, np.ndarray] = {}
        peak_polarity: dict[str, list[str]] = {}
        for name, row in zip(("AF7", "AF8"), channel_rows):
            peaks = find_channel_peaks(
                data[row, :], timestamps, sampling_rate, name, args.threshold,
                args.min_duration, args.max_duration, args.min_interval,
            )
            flags = np.zeros(target_samples, dtype=bool)
            polarity = [""] * target_samples
            for peak in peaks:
                peak_idx = int(np.argmin(np.abs(timestamps - peak.timestamp)))
                flags[peak_idx] = True
                polarity[peak_idx] = "positive" if peak.polarity == "+" else "negative"
                print(f"[PEAK] t={peak.timestamp:.3f}s | {name} | {polarity[peak_idx]} | "
                      f"amplitude={peak.amplitude:+.1f} µV | duration={peak.duration_ms:.0f} ms | "
                      f"prominence={peak.prominence:.1f} µV")
            peaks_by_channel[name] = peaks
            peak_flags[name] = flags
            peak_polarity[name] = polarity

        csv_path = Path(args.csv).expanduser().resolve()
        with csv_path.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            writer.writerow(["sample_no", "timestamp_s", "channel", "amplitude_uv", "is_peak", "peak_polarity"])
            for idx in range(target_samples):
                for name, row in zip(("AF7", "AF8"), channel_rows):
                    writer.writerow([idx, f"{timestamps[idx]:.9f}", name, f"{data[row, idx]:.9f}",
                                     bool(peak_flags[name][idx]), peak_polarity[name][idx]])

        print(f"\nRecorded {target_samples} samples ({target_samples / sampling_rate:.3f} s). CSV: {csv_path}")
        return 0
    except KeyboardInterrupt:
        print("\nRecording stopped early.")
        return 0
    finally:
        if started:
            try:
                board.stop_stream()
            except Exception:
                pass
            try:
                board.release_session()
            except Exception:
                pass


if __name__ == "__main__":
    raise SystemExit(main())
