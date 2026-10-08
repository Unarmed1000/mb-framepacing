#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Regenerate the pacer module's golden data (sdk/test-data/pacer).

1. <scenario>-frames.csv: a busy test clip of measure/test-data/videos (made by mb-framepacing-explained's simulations) frame by frame:
   each frame's render time in nanoseconds (the manifest's cpuBusyNs) and, as the reference, the swap interval it was paced at (60 fps: 1, 30 fps:
   2) and the refresh it was shown on. 60-busy is the busy stretch of the 60-busy-adaptive clip (the full-window rule), 60-busy-full-rate the clip of that name (every
   refresh, no adapting).
2. <scenario>-<rule>.csv: pacer-sim --golden, every golden scenario paced with its rules (both, or -Fixed at a fixed swap interval; the
   C++ tool; a port's tests must produce the same bytes).
3. tier-loops.csv and tier-loop-<run>.csv: pacer-sim --golden too, the tier pacer in a simulated frame loop on a display model: every way
   of pacing that has a pacer, both aims and a list of cases. A line per run with the length and the CRC-32 of its frames as text, and
   for the runs with GPU work that nobody reports the frames themselves, to read a difference in.

    python tools/update_pacer_test_data.py [--pacer-sim <path to pacer-sim>]

Run it after a change to the pacer's rule or planning, then review the difference: the pacer's tests compare with these files.
"""

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import cast

ROOT = Path(__file__).resolve().parent.parent
VIDEOS = ROOT / "measure" / "test-data" / "videos"
# The golden scenarios given frame by frame: the scenario's name and the clip it comes from
CLIPS = {"60-busy": "60-busy-adaptive", "60-busy-full-rate": "60-busy-full-rate"}
TARGET = ROOT / "sdk" / "test-data" / "pacer"
REFRESH_HZ = 60


class Arguments(argparse.Namespace):
    """The parsed command line."""

    pacer_sim: str | None = None


def find_pacer_sim() -> Path | None:
    """pacer-sim in a configured build of sdk/cpp (any preset; Visual Studio puts it in Release/)."""
    candidates = [
        path
        for pattern in ("*/pacer/Release/pacer-sim.exe", "*/pacer/pacer-sim.exe", "*/pacer/pacer-sim")
        for path in (ROOT / "sdk" / "cpp" / "build").glob(pattern)
    ]
    return max(candidates, key=lambda path: path.stat().st_mtime) if candidates else None


def write_frames(scenario: str, clip: str) -> Path:
    manifest_path = VIDEOS / clip / "manifest.json"
    manifest = cast(dict[str, object], json.loads(manifest_path.read_text(encoding="utf-8")))
    videos = cast(list[dict[str, object]], manifest["videos"])
    box = cast(dict[str, object], videos[0]["box"])
    frames = cast(dict[str, list[int]], box["frames"])
    work, target_fps, shown = frames["cpuBusyNs"], frames["targetFps"], frames["refresh"]
    if not len(work) == len(target_fps) == len(shown):
        sys.exit(f"{manifest_path}: the frame lists differ in length")
    lines = ["workNs,referenceSwapInterval,referenceShownRefresh"]
    for busy, fps, refresh in zip(work, target_fps, shown, strict=True):
        if REFRESH_HZ % fps != 0:
            sys.exit(f"{manifest_path}: a target of {fps} fps is not a whole swap interval at {REFRESH_HZ} Hz")
        lines.append(f"{busy},{REFRESH_HZ // fps},{refresh}")
    TARGET.mkdir(parents=True, exist_ok=True)
    path = TARGET / f"{scenario}-frames.csv"
    _ = path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--pacer-sim", help="the pacer-sim executable (default: the newest in sdk/cpp/build)")
    args = parser.parse_args(namespace=Arguments())

    for scenario, clip in CLIPS.items():
        print(write_frames(scenario, clip))
    pacer_sim = Path(args.pacer_sim) if args.pacer_sim else find_pacer_sim()
    if pacer_sim is None or not pacer_sim.is_file():
        sys.exit(
            "pacer-sim not found: build sdk/cpp with the pacer and its tests (-DMB_FRAMEPACING_BUILD_PACER=ON; the windows and "
            + "linux-sanitize presets have it on) or pass --pacer-sim"
        )
    _ = subprocess.run([str(pacer_sim), "--golden", str(TARGET)], check=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
