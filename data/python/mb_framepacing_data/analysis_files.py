# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""The file names of an analysis output folder (doc/analysis-output-format.md), finding it next to a capture, and its time format:
milliseconds with at most four decimals, which is exactly a whole number of 100 ns ticks."""

from pathlib import Path

DIRECTORY_NAME = "analysis"
SUMMARY_FILE_NAME = "summary.json"
CAPTURES_FILE_NAME = "captures.csv"
TICKS_PER_MILLISECOND = 10_000


def run_file_prefix(run_id: int, ordinal: int = 0) -> str:
    """'run-<id>', and 'run-<id>-<n>' for the n-th run with the same id (ordinal counts from 0 among the runs with that id)."""
    return f"run-{run_id}" if ordinal == 0 else f"run-{run_id}-{ordinal + 1}"


def frames_file_name(run_id: int, ordinal: int = 0) -> str:
    """A run's frames CSV. summary.json names it (SummaryRun.frames_file): prefer that name."""
    return run_file_prefix(run_id, ordinal) + "-frames.csv"


def find_analysis(folder: str | Path) -> Path | None:
    """The analysis output folder of a folder: the folder itself, or its analysis folder; None when neither holds summary.json."""
    folder = Path(folder)
    if (folder / SUMMARY_FILE_NAME).is_file():
        return folder
    analysis = folder / DIRECTORY_NAME
    return analysis if (analysis / SUMMARY_FILE_NAME).is_file() else None


def ms_to_ticks(milliseconds: float) -> int:
    """The 100 ns ticks of a milliseconds value (rounded to the nearest tick)."""
    return round(milliseconds * TICKS_PER_MILLISECOND)


def parse_ticks(text: str) -> int:
    """The ticks of a milliseconds text from the CSV files."""
    return ms_to_ticks(float(text))
