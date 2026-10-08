# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""NanosecondTickCount: a point on a clock that counts in nanoseconds, as the C++ and C# cores have it."""

from typing import ClassVar, Self, overload, override

from .nanosecond_time_span import NanosecondTimeSpan

_MODULUS = 2**64
_HALF = 2**63
_MIN_NANOSECONDS = -_HALF
_MAX_NANOSECONDS = _HALF - 1


class NanosecondTickCount:
    """A point on a clock that counts in nanoseconds, any epoch (the same clock for the whole run), kept as a platform gives it.

    The marker's and the data module's times are whole nanoseconds; a tick is 100 ns (what .NET counts in), and to_ticks() is the
    tick a point is in. The count is kept as an unsigned 64-bit number, as in C++ and C#, so adding and subtracting wrap around and
    nanoseconds is its signed view. Two counts compare and subtract correctly while they are less than 2^63 nanoseconds apart
    (about 292 years), across the wrap too, so the order is not a total one. A value outside the range raises OverflowError;
    adding and subtracting never do. The count is a whole number, an int, as in C++ and C#: a float (or a bool) raises TypeError.
    """

    __slots__: tuple[str, ...] = ("_unsigned",)

    NANOSECONDS_PER_TICK: ClassVar[int] = NanosecondTimeSpan.NANOSECONDS_PER_TICK
    NANOSECONDS_PER_MICROSECOND: ClassVar[int] = NanosecondTimeSpan.NANOSECONDS_PER_MICROSECOND
    NANOSECONDS_PER_MILLISECOND: ClassVar[int] = NanosecondTimeSpan.NANOSECONDS_PER_MILLISECOND
    NANOSECONDS_PER_SECOND: ClassVar[int] = NanosecondTimeSpan.NANOSECONDS_PER_SECOND

    _unsigned: int

    def __init__(self, nanoseconds: int = 0) -> None:
        """The point a signed count of nanoseconds after the clock's epoch."""
        if type(nanoseconds) is not int:
            raise TypeError("A NanosecondTickCount is a whole number of nanoseconds: an int")
        if not _MIN_NANOSECONDS <= nanoseconds <= _MAX_NANOSECONDS:
            raise OverflowError("The value is outside the range of a NanosecondTickCount")
        self._unsigned = nanoseconds % _MODULUS

    @classmethod
    def from_unsigned_nanoseconds(cls, nanoseconds: int) -> Self:
        """The count as it is kept (unsigned_nanoseconds): 0 to 2^64 - 1."""
        if type(nanoseconds) is not int:
            raise TypeError("A NanosecondTickCount is a whole number of nanoseconds: an int")
        if not 0 <= nanoseconds < _MODULUS:
            raise OverflowError("The value is outside the range of a NanosecondTickCount")
        return cls(nanoseconds - _MODULUS if nanoseconds >= _HALF else nanoseconds)

    @classmethod
    def from_microseconds(cls, microseconds: int) -> Self:
        return cls(microseconds * cls.NANOSECONDS_PER_MICROSECOND)

    @classmethod
    def from_milliseconds(cls, milliseconds: int) -> Self:
        return cls(milliseconds * cls.NANOSECONDS_PER_MILLISECOND)

    @classmethod
    def from_seconds(cls, seconds: int) -> Self:
        return cls(seconds * cls.NANOSECONDS_PER_SECOND)

    @classmethod
    def from_ticks(cls, ticks: int) -> Self:
        """A point in ticks of 100 ns, exactly."""
        return cls(ticks * cls.NANOSECONDS_PER_TICK)

    @property
    def nanoseconds(self) -> int:
        """The count as a signed number of nanoseconds."""
        return self._unsigned - _MODULUS if self._unsigned >= _HALF else self._unsigned

    @property
    def unsigned_nanoseconds(self) -> int:
        """The count as it is kept."""
        return self._unsigned

    def to_ticks(self) -> int:
        """The tick of 100 ns the point is in: rounded down, before the epoch too."""
        return self.nanoseconds // self.NANOSECONDS_PER_TICK

    def to_nanosecond_time_span(self) -> NanosecondTimeSpan:
        """The time since the clock's epoch."""
        return NanosecondTimeSpan(self.nanoseconds)

    @property
    def total_microseconds(self) -> float:
        return self.nanoseconds / self.NANOSECONDS_PER_MICROSECOND

    @property
    def total_milliseconds(self) -> float:
        return self.nanoseconds / self.NANOSECONDS_PER_MILLISECOND

    @property
    def total_seconds(self) -> float:
        return self.nanoseconds / self.NANOSECONDS_PER_SECOND

    def __add__(self, span: NanosecondTimeSpan) -> "NanosecondTickCount":
        """Wraps around."""
        return NanosecondTickCount.from_unsigned_nanoseconds((self._unsigned + span.nanoseconds) % _MODULUS)

    @overload
    def __sub__(self, other: NanosecondTimeSpan) -> "NanosecondTickCount": ...

    @overload
    def __sub__(self, other: "NanosecondTickCount") -> NanosecondTimeSpan: ...

    def __sub__(self, other: "NanosecondTimeSpan | NanosecondTickCount") -> "NanosecondTickCount | NanosecondTimeSpan":
        """Less a span: the point that much earlier (wraps around). Less a count: the time from it to this one, the shorter way round."""
        if isinstance(other, NanosecondTickCount):
            return NanosecondTimeSpan(self._distance(other))
        return NanosecondTickCount.from_unsigned_nanoseconds((self._unsigned - other.nanoseconds) % _MODULUS)

    @override
    def __eq__(self, other: object) -> bool:
        return isinstance(other, NanosecondTickCount) and self._unsigned == other._unsigned

    @override
    def __hash__(self) -> int:
        return hash(self._unsigned)

    def __lt__(self, other: "NanosecondTickCount") -> bool:
        """This count is before the other: the wrap-safe order, for counts less than 2^63 nanoseconds apart."""
        return self._distance(other) < 0

    def __le__(self, other: "NanosecondTickCount") -> bool:
        return self._distance(other) <= 0

    def __gt__(self, other: "NanosecondTickCount") -> bool:
        return self._distance(other) > 0

    def __ge__(self, other: "NanosecondTickCount") -> bool:
        return self._distance(other) >= 0

    @override
    def __repr__(self) -> str:
        return f"NanosecondTickCount(nanoseconds={self.nanoseconds})"

    @override
    def __str__(self) -> str:
        return f"{self.nanoseconds} ns"

    def _distance(self, other: "NanosecondTickCount") -> int:
        """The nanoseconds from the other count to this one as a signed 64-bit number: the shorter way round."""
        difference = (self._unsigned - other._unsigned) % _MODULUS
        return difference - _MODULUS if difference >= _HALF else difference
