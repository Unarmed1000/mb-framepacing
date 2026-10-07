# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""NanosecondTimeSpan and NanosecondTickCount: the same cases as the C++ and C# cores' tests."""

import unittest
from typing import cast

from .. import NanosecondTickCount, NanosecondTimeSpan, NanosecondTimeSpan32

MAX_INT64 = 2**63 - 1
MIN_INT64 = -(2**63)
MAX_UINT64 = 2**64 - 1

# What a caller that ignores the type hints could pass where a whole count is asked for
A_FLOAT = cast("int", 4_166_389.0)
A_BOOL = cast("int", True)


class NanosecondTimeSpanTests(unittest.TestCase):
    def test_holds_a_signed_count_of_nanoseconds(self) -> None:
        self.assertEqual(NanosecondTimeSpan().nanoseconds, 0)
        self.assertEqual(NanosecondTimeSpan(4_166_389).nanoseconds, 4_166_389)
        self.assertEqual(NanosecondTimeSpan(-1).nanoseconds, -1)
        self.assertEqual(NanosecondTimeSpan.min_value().nanoseconds, MIN_INT64)
        self.assertEqual(NanosecondTimeSpan.max_value().nanoseconds, MAX_INT64)
        self.assertEqual(
            (
                NanosecondTimeSpan.NANOSECONDS_PER_TICK,
                NanosecondTimeSpan.NANOSECONDS_PER_MICROSECOND,
                NanosecondTimeSpan.NANOSECONDS_PER_MILLISECOND,
                NanosecondTimeSpan.NANOSECONDS_PER_SECOND,
            ),
            (100, 1_000, 1_000_000, 1_000_000_000),
        )
        # The range of a signed 64-bit count, as in C++ and C#
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan(MAX_INT64 + 1)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan(MIN_INT64 - 1)

    def test_is_made_from_whole_units_and_raises_outside_its_range(self) -> None:
        self.assertEqual(NanosecondTimeSpan.from_microseconds(4_166).nanoseconds, 4_166_000)
        self.assertEqual(NanosecondTimeSpan.from_milliseconds(-16).nanoseconds, -16_000_000)
        self.assertEqual(NanosecondTimeSpan.from_seconds(2).nanoseconds, 2_000_000_000)
        self.assertEqual(NanosecondTimeSpan.from_seconds(9_223_372_036).nanoseconds, 9_223_372_036_000_000_000)
        self.assertEqual(NanosecondTimeSpan.from_seconds(-9_223_372_036).nanoseconds, -9_223_372_036_000_000_000)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan.from_seconds(9_223_372_037)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan.from_seconds(-9_223_372_037)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan.from_milliseconds(MAX_INT64)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan.from_microseconds(MIN_INT64)

    def test_ticks_are_exact_in_nanoseconds_and_the_way_back_truncates_to_a_tick(self) -> None:
        self.assertEqual(NanosecondTimeSpan.from_ticks(41_664).nanoseconds, 4_166_400)
        self.assertEqual(NanosecondTimeSpan.from_ticks(-1).nanoseconds, -100)
        self.assertEqual(NanosecondTimeSpan.from_ticks(MAX_INT64 // 100).nanoseconds, (MAX_INT64 // 100) * 100)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan.from_ticks((MAX_INT64 // 100) + 1)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan.from_ticks(-(MAX_INT64 // 100) - 2)

        # The refresh period of a 240.016 Hz mode: 4,166,389 ns is 41,663 ticks and 89 ns that ticks do not hold
        self.assertEqual(NanosecondTimeSpan(4_166_389).to_ticks(), 41_663)
        self.assertEqual(NanosecondTimeSpan(99).to_ticks(), 0)
        self.assertEqual(NanosecondTimeSpan(100).to_ticks(), 1)
        # Toward zero
        self.assertEqual(NanosecondTimeSpan(-99).to_ticks(), 0)
        self.assertEqual(NanosecondTimeSpan(-199).to_ticks(), -1)
        self.assertEqual(NanosecondTimeSpan.max_value().to_ticks(), 92_233_720_368_547_758)
        self.assertEqual(NanosecondTimeSpan.min_value().to_ticks(), -92_233_720_368_547_758)

    def test_gives_its_total_in_larger_units(self) -> None:
        span = NanosecondTimeSpan(4_166_389)
        self.assertAlmostEqual(span.total_microseconds, 4_166.389, places=9)
        self.assertAlmostEqual(span.total_milliseconds, 4.166389, places=12)
        self.assertAlmostEqual(span.total_seconds, 0.004166389, places=15)
        self.assertEqual(NanosecondTimeSpan(-1_500_000_000).total_seconds, -1.5)

    def test_adds_subtracts_and_negates_and_raises_outside_its_range(self) -> None:
        a = NanosecondTimeSpan(4_166_389)
        b = NanosecondTimeSpan(-389)
        self.assertEqual((a + b).nanoseconds, 4_166_000)
        self.assertEqual((a - b).nanoseconds, 4_166_778)
        self.assertEqual((b - a).nanoseconds, -4_166_778)
        self.assertEqual(+a, a)
        self.assertEqual((-a).nanoseconds, -4_166_389)
        self.assertEqual(abs(b).nanoseconds, 389)
        self.assertEqual(abs(a), a)
        self.assertEqual(NanosecondTimeSpan.max_value() + NanosecondTimeSpan(-1), NanosecondTimeSpan(MAX_INT64 - 1))
        self.assertEqual(NanosecondTimeSpan.min_value() + NanosecondTimeSpan.max_value(), NanosecondTimeSpan(-1))
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan.max_value() + NanosecondTimeSpan(1)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan.min_value() - NanosecondTimeSpan(1)
        # The lowest value has no positive counterpart
        with self.assertRaises(OverflowError):
            _ = -NanosecondTimeSpan.min_value()
        with self.assertRaises(OverflowError):
            _ = abs(NanosecondTimeSpan.min_value())

    def test_compares_by_its_count_and_is_written_with_its_unit(self) -> None:
        shorter = NanosecondTimeSpan(-100)
        longer = NanosecondTimeSpan(100)
        self.assertTrue(shorter < longer and shorter <= longer and longer > shorter and longer >= shorter)
        self.assertFalse(longer < shorter or shorter > longer)
        self.assertEqual(shorter, NanosecondTimeSpan(-100))
        self.assertNotEqual(shorter, longer)
        self.assertEqual(hash(shorter), hash(NanosecondTimeSpan(-100)))
        self.assertEqual(sorted([longer, shorter]), [shorter, longer])
        self.assertEqual(str(NanosecondTimeSpan(4_166_389)), "4166389 ns")
        self.assertEqual(repr(NanosecondTimeSpan(-389)), "NanosecondTimeSpan(nanoseconds=-389)")

    def test_holds_whole_nanoseconds_only(self) -> None:
        # An int and nothing else: no float gets in, by the constructor or by a factory
        self.assertIs(type(NanosecondTimeSpan(4_166_389).nanoseconds), int)
        self.assertIs(type(NanosecondTimeSpan.from_ticks(41_664).nanoseconds), int)
        self.assertIs(type((NanosecondTimeSpan(5) - NanosecondTimeSpan(7)).nanoseconds), int)
        self.assertIs(type(NanosecondTimeSpan(4_166_389).to_ticks()), int)
        for make in (NanosecondTimeSpan, NanosecondTimeSpan.from_microseconds, NanosecondTimeSpan.from_seconds, NanosecondTimeSpan.from_ticks):
            with self.assertRaises(TypeError):
                _ = make(A_FLOAT)
        with self.assertRaises(TypeError):
            _ = NanosecondTimeSpan(A_BOOL)


class NanosecondTickCountTests(unittest.TestCase):
    def test_holds_a_count_of_nanoseconds_kept_unsigned(self) -> None:
        self.assertEqual(NanosecondTickCount().nanoseconds, 0)
        self.assertEqual(NanosecondTickCount(4_166_389).nanoseconds, 4_166_389)
        self.assertEqual(NanosecondTickCount(4_166_389).unsigned_nanoseconds, 4_166_389)
        self.assertEqual(NanosecondTickCount(-1).nanoseconds, -1)
        self.assertEqual(NanosecondTickCount(-1).unsigned_nanoseconds, MAX_UINT64)
        self.assertEqual(NanosecondTickCount.from_unsigned_nanoseconds(MAX_UINT64).nanoseconds, -1)
        self.assertEqual(NanosecondTickCount.from_unsigned_nanoseconds(12), NanosecondTickCount(12))
        self.assertEqual(NanosecondTickCount(4_166_389).to_nanosecond_time_span(), NanosecondTimeSpan(4_166_389))
        self.assertEqual(NanosecondTickCount.NANOSECONDS_PER_TICK, 100)
        self.assertEqual(NanosecondTickCount.NANOSECONDS_PER_SECOND, 1_000_000_000)
        with self.assertRaises(OverflowError):
            _ = NanosecondTickCount(MAX_INT64 + 1)
        with self.assertRaises(OverflowError):
            _ = NanosecondTickCount(MIN_INT64 - 1)
        with self.assertRaises(OverflowError):
            _ = NanosecondTickCount.from_unsigned_nanoseconds(-1)
        with self.assertRaises(OverflowError):
            _ = NanosecondTickCount.from_unsigned_nanoseconds(MAX_UINT64 + 1)

    def test_is_made_from_whole_units_and_raises_outside_its_range(self) -> None:
        self.assertEqual(NanosecondTickCount.from_seconds(2).nanoseconds, 2_000_000_000)
        self.assertEqual(NanosecondTickCount.from_milliseconds(-16).nanoseconds, -16_000_000)
        self.assertEqual(NanosecondTickCount.from_microseconds(4_166).nanoseconds, 4_166_000)
        self.assertEqual(NanosecondTickCount.from_seconds(9_223_372_036).nanoseconds, 9_223_372_036_000_000_000)
        with self.assertRaises(OverflowError):
            _ = NanosecondTickCount.from_seconds(9_223_372_037)
        with self.assertRaises(OverflowError):
            _ = NanosecondTickCount.from_milliseconds(MIN_INT64)
        with self.assertRaises(OverflowError):
            _ = NanosecondTickCount.from_microseconds(MAX_INT64)

    def test_ticks_are_exact_in_nanoseconds_and_the_way_back_is_the_tick_the_point_is_in(self) -> None:
        self.assertEqual(NanosecondTickCount.from_ticks(41_664).nanoseconds, 4_166_400)
        self.assertEqual(NanosecondTickCount.from_ticks(-1).nanoseconds, -100)
        with self.assertRaises(OverflowError):
            _ = NanosecondTickCount.from_ticks((MAX_INT64 // 100) + 1)
        # Rounded down to the tick the point is in, before the epoch too
        self.assertEqual(NanosecondTickCount(4_166_389).to_ticks(), 41_663)
        self.assertEqual(NanosecondTickCount(99).to_ticks(), 0)
        self.assertEqual(NanosecondTickCount(100).to_ticks(), 1)
        self.assertEqual(NanosecondTickCount(-1).to_ticks(), -1)
        self.assertEqual(NanosecondTickCount(-100).to_ticks(), -1)
        self.assertEqual(NanosecondTickCount(-101).to_ticks(), -2)

    def test_gives_its_total_in_larger_units(self) -> None:
        count = NanosecondTickCount(4_166_389)
        self.assertAlmostEqual(count.total_microseconds, 4_166.389, places=9)
        self.assertAlmostEqual(count.total_milliseconds, 4.166389, places=12)
        self.assertAlmostEqual(count.total_seconds, 0.004166389, places=15)

    def test_a_span_moves_it_and_two_counts_are_a_span_apart(self) -> None:
        count = NanosecondTickCount(1_000_000_000)
        self.assertEqual((count + NanosecondTimeSpan(4_166_389)).nanoseconds, 1_004_166_389)
        self.assertEqual((count - NanosecondTimeSpan(4_166_389)).nanoseconds, 995_833_611)
        moved = count + NanosecondTimeSpan(500) - NanosecondTimeSpan(200)
        self.assertEqual(moved.nanoseconds, 1_000_000_300)
        self.assertEqual(moved - count, NanosecondTimeSpan(300))
        self.assertEqual(count - moved, NanosecondTimeSpan(-300))

    def test_wraps_around_and_compares_across_the_wrap(self) -> None:
        # One nanosecond past the highest signed value is the lowest: the count wraps
        last = NanosecondTickCount(MAX_INT64)
        following = last + NanosecondTimeSpan(1)
        self.assertEqual(following.nanoseconds, MIN_INT64)
        self.assertEqual(following - last, NanosecondTimeSpan(1))
        self.assertEqual(last - following, NanosecondTimeSpan(-1))
        self.assertTrue(last < following and last <= following and following > last and following >= last)
        self.assertFalse(following < last or last > following)
        # And across the unsigned wrap
        before = NanosecondTickCount.from_unsigned_nanoseconds(MAX_UINT64)
        after = before + NanosecondTimeSpan(2)
        self.assertEqual(after.unsigned_nanoseconds, 1)
        self.assertTrue(before < after)
        self.assertEqual(after - before, NanosecondTimeSpan(2))
        self.assertEqual(after - NanosecondTimeSpan(2), before)
        # The plain order of counts close together
        earlier = NanosecondTickCount(100)
        later = NanosecondTickCount(200)
        self.assertTrue(earlier < later and earlier <= NanosecondTickCount(100) and later >= NanosecondTickCount(200))
        self.assertFalse(earlier > later or later <= earlier)

    def test_equals_by_its_count_and_is_written_with_its_unit(self) -> None:
        count = NanosecondTickCount(4_166_389)
        self.assertEqual(count, NanosecondTickCount(4_166_389))
        self.assertNotEqual(count, NanosecondTickCount(1))
        self.assertNotEqual(count, NanosecondTimeSpan(4_166_389))
        self.assertEqual(hash(count), hash(NanosecondTickCount(4_166_389)))
        self.assertEqual(str(count), "4166389 ns")
        self.assertEqual(repr(NanosecondTickCount(-389)), "NanosecondTickCount(nanoseconds=-389)")

    def test_holds_whole_nanoseconds_only(self) -> None:
        count = NanosecondTickCount(4_166_389) + NanosecondTimeSpan(11)
        self.assertIs(type(count.nanoseconds), int)
        self.assertIs(type(count.unsigned_nanoseconds), int)
        self.assertIs(type(count.to_ticks()), int)
        self.assertIs(type((count - NanosecondTickCount(1)).nanoseconds), int)
        for make in (
            NanosecondTickCount,
            NanosecondTickCount.from_unsigned_nanoseconds,
            NanosecondTickCount.from_milliseconds,
            NanosecondTickCount.from_ticks,
        ):
            with self.assertRaises(TypeError):
                _ = make(A_FLOAT)
        with self.assertRaises(TypeError):
            _ = NanosecondTickCount(A_BOOL)


class NanosecondTimeSpan32Tests(unittest.TestCase):
    def test_holds_a_count_of_nanoseconds_that_fits_32_bits(self) -> None:
        self.assertEqual(NanosecondTimeSpan32().nanoseconds, 0)
        self.assertEqual(NanosecondTimeSpan32(4_166_389).nanoseconds, 4_166_389)
        self.assertEqual(NanosecondTimeSpan32.max_value().nanoseconds, 4_294_967_295)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan32(4_294_967_296)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan32(-1)
        # An int and nothing else
        self.assertIs(type(NanosecondTimeSpan32.from_ticks(41_664).nanoseconds), int)
        with self.assertRaises(TypeError):
            _ = NanosecondTimeSpan32(A_FLOAT)
        with self.assertRaises(TypeError):
            _ = NanosecondTimeSpan32.from_ticks(A_FLOAT)
        with self.assertRaises(TypeError):
            _ = NanosecondTimeSpan32(A_BOOL)

    def test_is_made_from_a_span_that_fits_and_raises_for_one_that_does_not(self) -> None:
        self.assertEqual(NanosecondTimeSpan32.from_nanosecond_time_span(NanosecondTimeSpan(4_166_389)).nanoseconds, 4_166_389)
        self.assertEqual(NanosecondTimeSpan32.from_nanosecond_time_span(NanosecondTimeSpan(4_294_967_295)), NanosecondTimeSpan32.max_value())
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan32.from_nanosecond_time_span(NanosecondTimeSpan(4_294_967_296))
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan32.from_nanosecond_time_span(NanosecondTimeSpan(-1))
        # From ticks of 100 ns, exactly: 42,949,672 ticks is the longest that fits
        self.assertEqual(NanosecondTimeSpan32.from_ticks(41_664).nanoseconds, 4_166_400)
        self.assertEqual(NanosecondTimeSpan32.from_ticks(42_949_672).nanoseconds, 4_294_967_200)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan32.from_ticks(42_949_673)
        with self.assertRaises(OverflowError):
            _ = NanosecondTimeSpan32.from_ticks(-1)

    def test_widens_to_a_span_exactly_and_to_ticks_truncated(self) -> None:
        self.assertEqual(NanosecondTimeSpan32(4_166_389).to_nanosecond_time_span(), NanosecondTimeSpan(4_166_389))
        self.assertEqual(NanosecondTimeSpan32(4_166_389).to_ticks(), 41_663)
        self.assertEqual(NanosecondTimeSpan32(99).to_ticks(), 0)
        self.assertEqual(NanosecondTimeSpan32.max_value().to_ticks(), 42_949_672)

    def test_compares_by_its_count_and_is_written_with_its_unit(self) -> None:
        shorter = NanosecondTimeSpan32(4_166_389)
        longer = NanosecondTimeSpan32(16_666_667)
        self.assertTrue(shorter < longer and shorter <= longer and longer > shorter and longer >= shorter)
        self.assertEqual(shorter, NanosecondTimeSpan32(4_166_389))
        self.assertNotEqual(shorter, longer)
        self.assertEqual(hash(shorter), hash(NanosecondTimeSpan32(4_166_389)))
        self.assertEqual(str(shorter), "4166389 ns")
        self.assertEqual(repr(shorter), "NanosecondTimeSpan32(nanoseconds=4166389)")
