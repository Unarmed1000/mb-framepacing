# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""mb_framepacing: the mb-framepacing SDK in Python.

- mb_framepacing.marker draws the frame marker (a QR code with the frame index, the animation time and the frame pacing values) into
  every frame of an application, exactly the pixels of the C++ and C# libraries (doc/marker-format.md).
- mb_framepacing.data reads what the mb-framepacing tools write: the capture data (captures.mbcd) and the analysis output (summary.json
  and the CSVs; doc/capture-data-format.md, doc/analysis-output-format.md).

Standard library only, Python 3.12 or later.
"""

from .nanoseconds import (
    NANOSECONDS_PER_MICROSECOND,
    NANOSECONDS_PER_MILLISECOND,
    NANOSECONDS_PER_SECOND,
    NANOSECONDS_PER_TICK,
    nanosecond_tick_count_to_ticks,
    nanosecond_time_span_to_ticks,
    ticks_to_nanoseconds,
)
from .point import Point
from .rectangle import Rectangle

# The SDK's version, sdk/VERSION in mb-framepacing as PEP 440 spells it (0.2.0-beta.1 is 0.2.0b1; tests/test_version.py checks it)
__version__ = "0.1.0"

__all__ = [
    "NANOSECONDS_PER_MICROSECOND",
    "NANOSECONDS_PER_MILLISECOND",
    "NANOSECONDS_PER_SECOND",
    "NANOSECONDS_PER_TICK",
    "Point",
    "Rectangle",
    "__version__",
    "nanosecond_tick_count_to_ticks",
    "nanosecond_time_span_to_ticks",
    "ticks_to_nanoseconds",
]
