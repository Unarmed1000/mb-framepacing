# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""NanosecondTimeSpan32: an interval in nanoseconds that fits a 32-bit field, as the C++ and C# cores have it."""

from dataclasses import dataclass
from typing import Self, override

from .nanosecond_time_span import NanosecondTimeSpan

_MAX_NANOSECONDS = 2**32 - 1


@dataclass(frozen=True, slots=True, order=True)
class NanosecondTimeSpan32:
    """A time interval of 0 to 4.294967295 s in nanoseconds: a 32-bit field that holds an interval to the nanosecond.

    It says what a value becomes when it is put into four bytes: a negative or a longer span does not fit and raises
    OverflowError, never a silent cut. The count is a whole number, an int: a float (or a bool) raises TypeError. It has no
    arithmetic: compute with NanosecondTimeSpan and convert the result.
    """

    nanoseconds: int = 0

    def __post_init__(self) -> None:
        if type(self.nanoseconds) is not int:
            raise TypeError("A NanosecondTimeSpan32 is a whole number of nanoseconds: an int")
        if not 0 <= self.nanoseconds <= _MAX_NANOSECONDS:
            raise OverflowError("A NanosecondTimeSpan32 is 0 to 4.294967295 s")

    @classmethod
    def max_value(cls) -> Self:
        return cls(_MAX_NANOSECONDS)

    @classmethod
    def from_nanosecond_time_span(cls, span: NanosecondTimeSpan) -> Self:
        """The span exactly."""
        return cls(span.nanoseconds)

    @classmethod
    def from_ticks(cls, ticks: int) -> Self:
        """A span in ticks of 100 ns, exactly (42,949,672 ticks is the longest that fits)."""
        return cls(ticks * NanosecondTimeSpan.NANOSECONDS_PER_TICK)

    def to_nanosecond_time_span(self) -> NanosecondTimeSpan:
        return NanosecondTimeSpan(self.nanoseconds)

    def to_ticks(self) -> int:
        """The span in ticks of 100 ns, truncated to a tick."""
        return self.nanoseconds // NanosecondTimeSpan.NANOSECONDS_PER_TICK

    @override
    def __str__(self) -> str:
        return f"{self.nanoseconds} ns"
