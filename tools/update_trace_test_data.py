#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""Regenerate measure/test-data/trace: what keeps this repository's two ways of joining
a capture with a Perfetto trace equal, the script (tools/trace_augment.py) and the tools (measure/libs/MB.FramePacing.TraceJoin).

Everything in the folder is made here, from the SDK's golden capture (sdk/test-data/data/60-busy-full-rate) and a trace the
tests make to fit it (tools/tests/synthetic_trace.py: made up in every value, no device and no recording):

    synthetic.pftrace          the made-up trace
    synthetic-pieces.pftrace   the same with the marks of a recording that was written to its file in pieces
    synthetic-app.pftrace      a made-up trace of the application's own, with its record of its frames (only the tools read it)
    frames.csv                 what the script says the trace says of each frame (--frames)
    digest.json                the length and SHA-256 of the traces the script writes (the capture alone, and each made-up trace
                               with the capture's tracks), and the numbers they were placed by

    uv run --group trace tools/update_trace_test_data.py            # write the folder
    uv run --group trace tools/update_trace_test_data.py --check    # fail when the folder is not what the script makes now

It needs Perfetto's Python package and its trace processor, as the script does. Run it after a change to the script's parts,
then review the difference: the tools' tests compare with these files."""

import argparse
import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOOL = ROOT / "tools" / "trace_augment.py"
CAPTURE = ROOT / "sdk" / "test-data" / "data" / "60-busy-full-rate"
TARGET = ROOT / "measure" / "test-data" / "trace"

SYNTHETIC = "synthetic.pftrace"
PIECES = "synthetic-pieces.pftrace"
APP = "synthetic-app.pftrace"
FRAMES = "frames.csv"
DIGEST = "digest.json"


class Arguments(argparse.Namespace):
    check: bool = False


def run_tool(*arguments: str | Path | int) -> None:
    done = subprocess.run([sys.executable, str(TOOL), *[str(a) for a in arguments]], capture_output=True, text=True, check=False)
    if done.returncode != 0:
        raise RuntimeError(f"trace_augment.py {' '.join(str(a) for a in arguments)} failed:\n{done.stdout}{done.stderr}")


def digest_of(data: bytes) -> dict[str, int | str]:
    return {"length": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def make() -> dict[str, bytes]:
    """The folder's files, by name."""
    from tests import synthetic_trace  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport] (the folder next to this script)
    from tests.golden_capture import CAPTURE_MINUS_PACER_NS, PERIOD_NS, frames  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport]

    # The made-up application's clock counts from an epoch of its own: the frame record in the trace says how far that is
    # from the record's clock, and which process is the application. Nothing here tells the script either
    offset = synthetic_trace.APP_CLOCK_OFFSET_NS
    with tempfile.TemporaryDirectory() as folder:
        work = Path(folder)
        synthetic_trace.write(work / SYNTHETIC)
        synthetic_trace.write(work / PIECES, with_service_events=True)
        synthetic_trace.write_app(work / APP)
        run_tool(CAPTURE, work / SYNTHETIC, "--frames", work / FRAMES)
        run_tool(CAPTURE, "-o", work / "capture-alone.pftrace")
        run_tool(CAPTURE, work / SYNTHETIC, "-o", work / "augmented.pftrace")
        run_tool(CAPTURE, work / PIECES, "-o", work / "augmented-pieces.pftrace")

        # The clocks of the made-up trace, as its one snapshot has them: what a reader without a trace processor places by
        golden = frames()
        start = synthetic_trace.trace_ns(min(f.cpu_start_ns for f in golden if f.cpu_start_ns is not None and f.cpu_busy_ns is not None)) - 1_000_000_000
        digest = {
            "capture": "sdk/test-data/data/60-busy-full-rate",
            "process": synthetic_trace.APP_NAME,
            "layer": synthetic_trace.APP_LAYER,
            "periodNs": PERIOD_NS,
            "appClock": "monotonic",
            "appClockOffsetNs": offset,
            "captureMinusAppNs": CAPTURE_MINUS_PACER_NS,
            "vblankShiftNs": synthetic_trace.VBLANK_BEFORE_INTENDED_NS,
            "vblanks": "vblank-0",
            "snapshotTraceNs": start,
            "snapshotClockNs": start - synthetic_trace.BOOT_MINUS_MONOTONIC_NS,
            "captureAlone": digest_of((work / "capture-alone.pftrace").read_bytes()),
            "augmented": digest_of((work / "augmented.pftrace").read_bytes()),
            "augmentedPieces": digest_of((work / "augmented-pieces.pftrace").read_bytes()),
        }
        return {
            SYNTHETIC: (work / SYNTHETIC).read_bytes(),
            PIECES: (work / PIECES).read_bytes(),
            APP: (work / APP).read_bytes(),
            FRAMES: (work / FRAMES).read_bytes(),
            DIGEST: (json.dumps(digest, indent=2) + "\n").encode(),
        }


def differences() -> list[str]:
    """The names of the files that are not what the script makes now. The folder's text files have line feeds in every checkout
    (.gitattributes), as they are written here."""
    return [name for name, data in make().items() if not (TARGET / name).is_file() or (TARGET / name).read_bytes() != data]


def main() -> int:
    parser = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    _ = parser.add_argument("--check", action="store_true", help="write nothing: fail when the folder is not what the script makes now")
    arguments = parser.parse_args(namespace=Arguments())
    if arguments.check:
        changed = differences()
        for name in changed:
            print(f"{TARGET / name} is not what tools/trace_augment.py makes now: run tools/update_trace_test_data.py", file=sys.stderr)
        return 1 if changed else 0
    TARGET.mkdir(parents=True, exist_ok=True)
    for name, data in make().items():
        _ = (TARGET / name).write_bytes(data)
        print(f"{TARGET / name}: {len(data)} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
