"""Record five seconds of NeuroPawn Knight EEG with peak highlighting.

Install dependencies with: py -m pip install brainflow scipy numpy matplotlib
Run with: py tools/neuropawn_blink_peak_monitor.py --port COM4
Plot with: py tools/neuropawn_blink_peak_monitor.py --plot-csv neuropawn_blink_5s_YYYYMMDD_HHMMSS.csv

AF7 and AF8 default to NeuroPawn inputs 7 and 8, matching the SimpleBCI
NeuroPawn blink test mapping. Close SimpleBCI and the NeuroPawn GUI first.
"""

from af7af8monitor import main


if __name__ == "__main__":
    raise SystemExit(main(default_device="neuropawn"))
