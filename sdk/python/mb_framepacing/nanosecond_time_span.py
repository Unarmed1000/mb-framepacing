# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""NanosecondTimeSpan: a signed time interval in nanoseconds, as the C++ and C# cores have it."""

from dataclasses import dataclass
from typing import ClassVar, Self, override

_MIN_NANOSECONDS = -(2**63)
_MAX_NANOSECONDS = 2**63 - 1


@dataclass(frozen=True, slots=True, order=True)
class NanosecondTimeSpan:
    """A signed time interval in nanoseconds: what a platform that counts in nanoseconds reports, kept as it is given.

    The SDK's other times are whole ticks of 100 ns, so a value that goes through ticks loses up to 99 ns: a refresh period of
    4,166,389 ns is 41,663 ticks, 21 parts in a million short. The range is that of a signed 64-bit count (about 292 years either
    way), as in C++ and C#; a value outside it raises OverflowError. The count is a whole number, an int, as in C++ and C#: a
    float (or a bool) raises TypeError, so no fraction of a nanosecond and no rounding of a large value gets in.
    """

    NANOSECONDS_PER_TICK: ClassVar[int] = 100
    NANOSECONDS_PER_MICROSECOND: ClassVar[int] = 1_000
    NANOSECONDS_PER_MILLISECOND: ClassVar[int] = 1_000_000
    NANOSECONDS_PER_SECOND: ClassVar[int] = 1_000_000_000

    nanoseconds: int = 0

    def __post_init__(self) -> None:
        if type(self.nanoseconds) is not int:
            raise TypeError("A NanosecondTimeSpan is a whole number of nanoseconds: an int")
        if not _MIN_NANOSECONDS <= self.nanoseconds <= _MAX_NANOSECONDS:
            raise OverflowError("The value is outside the range of a NanosecondTimeSpan")

    @classmethod
    def min_value(cls) -> Self:
        return cls(_MIN_NANOSECONDS)

    @classmethod
    def max_value(cls) -> Self:
        return cls(_MAX_NANOSECONDS)

    @classmethod
    def from_microseconds(cls, microseconds: int) -> Self:
        """A whole number of microseconds."""
        return cls(microseconds * cls.NANOSECONDS_PER_MICROSECOND)

    @classmethod
    def from_milliseconds(cls, milliseconds: int) -> Self:
        """A whole number of milliseconds."""
        return cls(milliseconds * cls.NANOSECONDS_PER_MILLISECOND)

    @classmethod
    def from_seconds(cls, seconds: int) -> Self:
        """A whole number of seconds."""
        return cls(seconds * cls.NANOSECONDS_PER_SECOND)

    @classmethod
    def from_ticks(cls, ticks: int) -> Self:
        """A span in ticks of 100 ns, exactly."""
        return cls(ticks * cls.NANOSECONDS_PER_TICK)

    def to_ticks(self) -> int:
        """The span in ticks of 100 ns, truncated toward zero to a tick, as every conversion to ticks is."""
        ticks = abs(self.nanoseconds) // self.NANOSECONDS_PER_TICK
        return -ticks if self.nanoseconds < 0 else ticks

    @property
    def total_microseconds(self) -> float:
        return self.nanoseconds / self.NANOSECONDS_PER_MICROSECOND

    @property
    def total_milliseconds(self) -> float:
        return self.nanoseconds / self.NANOSECONDS_PER_MILLISECOND

    @property
    def total_seconds(self) -> float:
        return self.nanoseconds / self.NANOSECONDS_PER_SECOND

    def __add__(self, other: "NanosecondTimeSpan") -> "NanosecondTimeSpan":
        return NanosecondTimeSpan(self.nanoseconds + other.nanoseconds)

    def __sub__(self, other: "NanosecondTimeSpan") -> "NanosecondTimeSpan":
        return NanosecondTimeSpan(self.nanoseconds - other.nanoseconds)

    def __neg__(self) -> "NanosecondTimeSpan":
        """The span with the other sign. The lowest value has no positive counterpart: OverflowError."""
        return NanosecondTimeSpan(-self.nanoseconds)

    def __pos__(self) -> "NanosecondTimeSpan":
        return self

    def __abs__(self) -> "NanosecondTimeSpan":
        """The absolute value. The lowest value has no positive counterpart: OverflowError."""
        return -self if self.nanoseconds < 0 else self

    @override
    def __str__(self) -> str:
        return f"{self.nanoseconds} ns"
