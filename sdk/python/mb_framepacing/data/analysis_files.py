# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The file names of an analysis output folder (doc/analysis-output-format.md) and finding it next to a capture."""

from pathlib import Path

DIRECTORY_NAME = "analysis"
SUMMARY_FILE_NAME = "summary.json"
CAPTURES_FILE_NAME = "captures.csv"
TICKS_PER_MILLISECOND = 10_000
"""The files hold every time as 100 ns ticks; this many are a millisecond, for showing one."""


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
