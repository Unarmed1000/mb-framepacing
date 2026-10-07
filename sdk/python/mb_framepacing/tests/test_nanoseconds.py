# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""Nanoseconds as plain integers: the conversions of the C++ and C# cores' NanosecondTimeSpan and NanosecondTickCount."""

import unittest

from .. import (
    NANOSECONDS_PER_MICROSECOND,
    NANOSECONDS_PER_MILLISECOND,
    NANOSECONDS_PER_SECOND,
    NANOSECONDS_PER_TICK,
    nanosecond_tick_count_to_ticks,
    nanosecond_time_span_to_ticks,
    ticks_to_nanoseconds,
)

MAX_INT64 = 2**63 - 1
MIN_INT64 = -(2**63)


class NanosecondsTests(unittest.TestCase):
    def test_the_units(self) -> None:
        self.assertEqual(
            (NANOSECONDS_PER_TICK, NANOSECONDS_PER_MICROSECOND, NANOSECONDS_PER_MILLISECOND, NANOSECONDS_PER_SECOND),
            (100, 1_000, 1_000_000, 1_000_000_000),
        )

    def test_ticks_are_exact_in_nanoseconds(self) -> None:
        self.assertEqual(ticks_to_nanoseconds(41_664), 4_166_400)
        self.assertEqual(ticks_to_nanoseconds(-1), -100)
        self.assertEqual(ticks_to_nanoseconds(0), 0)
        self.assertEqual(ticks_to_nanoseconds(MAX_INT64 // 100), (MAX_INT64 // 100) * 100)

    def test_an_interval_is_truncated_toward_zero_to_a_tick(self) -> None:
        # The refresh period of a 240.016 Hz mode: 4,166,389 ns is 41,663 ticks and 89 ns that ticks do not hold
        self.assertEqual(nanosecond_time_span_to_ticks(4_166_389), 41_663)
        self.assertEqual(nanosecond_time_span_to_ticks(99), 0)
        self.assertEqual(nanosecond_time_span_to_ticks(100), 1)
        self.assertEqual(nanosecond_time_span_to_ticks(-99), 0)
        self.assertEqual(nanosecond_time_span_to_ticks(-199), -1)
        self.assertEqual(nanosecond_time_span_to_ticks(MAX_INT64), 92_233_720_368_547_758)
        self.assertEqual(nanosecond_time_span_to_ticks(MIN_INT64), -92_233_720_368_547_758)

    def test_a_point_on_a_clock_is_the_tick_it_is_in(self) -> None:
        self.assertEqual(nanosecond_tick_count_to_ticks(4_166_389), 41_663)
        self.assertEqual(nanosecond_tick_count_to_ticks(99), 0)
        self.assertEqual(nanosecond_tick_count_to_ticks(100), 1)
        # Rounded down before the epoch too
        self.assertEqual(nanosecond_tick_count_to_ticks(-1), -1)
        self.assertEqual(nanosecond_tick_count_to_ticks(-100), -1)
        self.assertEqual(nanosecond_tick_count_to_ticks(-101), -2)
        # There and back again
        self.assertEqual(nanosecond_tick_count_to_ticks(ticks_to_nanoseconds(-41_664)), -41_664)
        self.assertEqual(nanosecond_time_span_to_ticks(ticks_to_nanoseconds(-41_664)), -41_664)
