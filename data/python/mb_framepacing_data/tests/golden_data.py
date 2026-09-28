# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""Finds the golden data (test-data/data: a test clip imported and analysed by the tools, and digest.json) above this file, or from the
MB_FRAMEPACING_DATA_TEST_DATA environment variable."""

import os
from pathlib import Path

CLIP = "60-busy-full-rate"
ENVIRONMENT_VARIABLE = "MB_FRAMEPACING_DATA_TEST_DATA"


def clip_directory() -> Path | None:
    """test-data/data/<clip>; None when not found (a copy of the library outside mb-framepacing)."""
    configured = os.environ.get(ENVIRONMENT_VARIABLE)
    if configured:
        return Path(configured) / CLIP
    for folder in Path(__file__).resolve().parents:
        candidate = folder / "test-data" / "data" / CLIP
        if (candidate / "digest.json").is_file():
            return candidate
    return None
