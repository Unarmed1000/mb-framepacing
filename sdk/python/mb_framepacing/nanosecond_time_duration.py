# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""NanosecondTimeDuration: a length of time in nanoseconds that is never negative, as the C++ and C# cores have it."""

from dataclasses import dataclass
from typing import ClassVar, Self, overload, override

from .nanosecond_time_span import NanosecondTimeSpan

_MAX_NANOSECONDS = 2**63 - 1


@dataclass(frozen=True, slots=True, order=True)
class NanosecondTimeDuration:
    """A length of time that is never negative, in nanoseconds: how long something took, a period, the span from a begin to an end
    that can not be before it.

    Always valid: a negative count given to it becomes zero, as in C++ and C#. A NanosecondTimeSpan is what can be negative (a
    difference between two times that can go either way), and that is what taking one duration from another gives, and what a
    duration and a NanosecondTimeSpan added or subtracted give. The range is 0 to the largest signed 64-bit count (about 292
    years); a value above it raises OverflowError. The count is a whole number, an int, as in C++ and C#: a float (or a bool)
    raises TypeError, so no fraction of a nanosecond and no rounding of a large value gets in.
    """

    NANOSECONDS_PER_TICK: ClassVar[int] = NanosecondTimeSpan.NANOSECONDS_PER_TICK

    nanoseconds: int = 0

    def __post_init__(self) -> None:
        if type(self.nanoseconds) is not int:
            raise TypeError("A NanosecondTimeDuration is a whole number of nanoseconds: an int")
        if self.nanoseconds > _MAX_NANOSECONDS:
            raise OverflowError("The value is outside the range of a NanosecondTimeDuration")
        if self.nanoseconds < 0:
            # The class is frozen for its users: this is the one place a value is put in
            object.__setattr__(self, "nanoseconds", 0)

    @classmethod
    def zero(cls) -> Self:
        return cls(0)

    @classmethod
    def max_value(cls) -> Self:
        return cls(_MAX_NANOSECONDS)

    @classmethod
    def from_nanosecond_time_span(cls, span: NanosecondTimeSpan) -> Self:
        """The span as a duration. A negative span becomes zero."""
        return cls(span.nanoseconds)

    @classmethod
    def from_ticks(cls, ticks: int) -> Self:
        """A duration in ticks of 100 ns, exactly. A negative count becomes zero."""
        return cls(ticks * cls.NANOSECONDS_PER_TICK)

    def to_ticks(self) -> int:
        """The duration in ticks of 100 ns, truncated to the tick."""
        return self.nanoseconds // self.NANOSECONDS_PER_TICK

    def to_nanosecond_time_span(self) -> NanosecondTimeSpan:
        """The duration as a NanosecondTimeSpan, which is never negative."""
        return NanosecondTimeSpan(self.nanoseconds)

    @overload
    def __add__(self, other: "NanosecondTimeDuration") -> "NanosecondTimeDuration": ...

    @overload
    def __add__(self, other: NanosecondTimeSpan) -> NanosecondTimeSpan: ...

    def __add__(self, other: "NanosecondTimeDuration | NanosecondTimeSpan") -> "NanosecondTimeDuration | NanosecondTimeSpan":
        """Two durations added are a duration; a duration and a span that can be negative give a span. Outside the range: OverflowError."""
        if isinstance(other, NanosecondTimeDuration):
            return NanosecondTimeDuration(self.nanoseconds + other.nanoseconds)
        return NanosecondTimeSpan(self.nanoseconds + other.nanoseconds)

    def __radd__(self, other: NanosecondTimeSpan) -> NanosecondTimeSpan:
        """A span that can be negative and a duration give a span. Outside the range: OverflowError."""
        return NanosecondTimeSpan(other.nanoseconds + self.nanoseconds)

    def __sub__(self, other: "NanosecondTimeDuration | NanosecondTimeSpan") -> NanosecondTimeSpan:
        """The difference can be negative, so it is a NanosecondTimeSpan. Less a span outside the range: OverflowError."""
        return NanosecondTimeSpan(self.nanoseconds - other.nanoseconds)

    def __rsub__(self, other: NanosecondTimeSpan) -> NanosecondTimeSpan:
        """A span that can be negative less a duration is a span. Outside the range: OverflowError."""
        return NanosecondTimeSpan(other.nanoseconds - self.nanoseconds)

    @override
    def __str__(self) -> str:
        return f"{self.nanoseconds} ns"
