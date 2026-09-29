#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Regenerate the data libraries' golden data (test-data/data): a test clip imported and analysed by the tools.

Imports test-data/videos/<clip>/video.mp4 with `mb-framepacing import --analyze` (needs ffmpeg), copies capture.json, captures.mbcd and the
analysis output into test-data/data/<clip>, and replaces the machine specific paths in the JSON files. Then it runs the C# data library's
golden tests with MB_FRAMEPACING_UPDATE_TEST_DATA=1, which write digest.json: the values every language's reader must read.

    python tools/update_test_data.py

Run it after a change to the capture data or analysis output format; the C#, Python and C++ data library tests then check the new files.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "measure" / "app" / "FramePacing" / "FramePacing.csproj"
TESTS = ROOT / "data" / "csharp" / "UnitTest" / "MB.FramePacing.Data.UnitTest.csproj"
CLIP = "60-busy-full-rate"
TARGET = ROOT / "test-data" / "data" / CLIP


def json_text(path: str) -> str:
    """How a path appears inside a JSON string."""
    return json.dumps(path)[1:-1]


def neutralise(path: Path, replacements: dict[str, str]) -> None:
    text = path.read_bytes().decode("utf-8")
    for old, new in replacements.items():
        text = text.replace(json_text(old), json_text(new))
    _ = path.write_bytes(text.encode("utf-8"))


def main() -> int:
    video = ROOT / "test-data" / "videos" / CLIP / "video.mp4"
    with tempfile.TemporaryDirectory(prefix="mb-framepacing-test-data-") as temporary:
        capture = Path(temporary) / CLIP
        command = ["dotnet", "run", "--project", str(PROJECT), "-c", "Release", "--", "import", str(video), "-o", str(capture), "--analyze"]
        print("> " + " ".join(command), flush=True)
        _ = subprocess.run(command, check=True, stdout=subprocess.DEVNULL)

        if TARGET.exists():
            shutil.rmtree(TARGET)
        (TARGET / "analysis").mkdir(parents=True)
        for name in ("capture.json", "captures.mbcd"):
            _ = shutil.copy2(capture / name, TARGET / name)
        for file in (capture / "analysis").iterdir():
            _ = shutil.copy2(file, TARGET / "analysis" / file.name)

    # The clip's path in ffmpeg's command line, and the capture folder in summary.json
    replacements = {str(video): f"test-data/videos/{CLIP}/video.mp4", str(capture): CLIP}
    neutralise(TARGET / "capture.json", replacements)
    neutralise(TARGET / "analysis" / "summary.json", replacements)

    environment = dict(os.environ, MB_FRAMEPACING_UPDATE_TEST_DATA="1")
    command = ["dotnet", "test", str(TESTS), "--filter", "FullyQualifiedName~GoldenDataTests"]
    print("> " + " ".join(command), flush=True)
    _ = subprocess.run(command, check=True, env=environment)
    print(f"Updated {TARGET.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
