# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""Times in nanoseconds: plain integers, as every time of this package is.

The C++ and C# cores have two types for what a platform reports in nanoseconds, NanosecondTimeSpan (a signed interval) and
NanosecondTickCount (a point on a clock). Both are a whole count of nanoseconds, so here they are an int whose name says the
unit (period_nanoseconds, display_nanoseconds), next to the package's ticks of 100 ns (period_ticks). These functions convert
between the two as those types do.
"""

NANOSECONDS_PER_TICK = 100
NANOSECONDS_PER_MICROSECOND = 1_000
NANOSECONDS_PER_MILLISECOND = 1_000_000
NANOSECONDS_PER_SECOND = 1_000_000_000


def ticks_to_nanoseconds(ticks: int) -> int:
    """Ticks of 100 ns in nanoseconds, exactly: an interval or a point on a clock."""
    return ticks * NANOSECONDS_PER_TICK


def nanosecond_time_span_to_ticks(nanoseconds: int) -> int:
    """An interval in nanoseconds in ticks of 100 ns, truncated toward zero to a tick (NanosecondTimeSpan's ToTimeSpan)."""
    ticks = abs(nanoseconds) // NANOSECONDS_PER_TICK
    return -ticks if nanoseconds < 0 else ticks


def nanosecond_tick_count_to_ticks(nanoseconds: int) -> int:
    """A point on a clock in nanoseconds as the tick of 100 ns it is in: rounded down, before the epoch too
    (NanosecondTickCount's ToTickCount64)."""
    return nanoseconds // NANOSECONDS_PER_TICK
