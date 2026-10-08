// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. Where the display's refreshes are, from vertical blank readings, as a part of its own (sdk/doc/pacer-design.md,
// "How a pacer is put together"). The pacers that have vertical blank times are tested with it in their own files; these tests
// are of the part alone.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/timeline/VBlankTimeline.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns, of which an eighth is 1,250,000
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  //! A vertical blank at a time, read at that time or at another
  void Read(PC::VBlankTimeline& rTimeline, const int64_t blankNanoseconds, const int64_t readNanoseconds = 0)
  {
    PC::VBlankReading reading;
    reading.VBlankTime = At(blankNanoseconds);
    reading.ReadTime = At(readNanoseconds != 0 ? readNanoseconds : blankNanoseconds);
    rTimeline.AddVBlank(reading, g_hz100);
  }

  int64_t TimeOf(const PC::VBlankTimeline& timeline, const int64_t slot)
  {
    return timeline.TimeOfBlank(slot, g_hz100).Nanoseconds();
  }
}

TEST(VBlankTimeline, UntilAReadingATimeThePacerGivesStandsInForAVerticalBlank)
{
  PC::VBlankTimeline timeline;
  EXPECT_FALSE(timeline.HasReading());
  timeline.StartAt(At(Start));
  EXPECT_FALSE(timeline.HasReading());
  // The vertical blanks are a refresh period apart, before it and after it
  EXPECT_EQ(TimeOf(timeline, 0), Start);
  EXPECT_EQ(TimeOf(timeline, 3), Start + (3 * Period));
  EXPECT_EQ(TimeOf(timeline, -2), Start - (2 * Period));
  // The last one at or before a time
  EXPECT_EQ(timeline.BlankAtOrBefore(At(Start), g_hz100), 0);
  EXPECT_EQ(timeline.BlankAtOrBefore(At(Start + Period - 1), g_hz100), 0);
  EXPECT_EQ(timeline.BlankAtOrBefore(At(Start + Period), g_hz100), 1);
  EXPECT_EQ(timeline.BlankAtOrBefore(At(Start - 1), g_hz100), -1);
  EXPECT_EQ(timeline.BlankAtOrBefore(At(Start - Period), g_hz100), -1);
  EXPECT_EQ(timeline.BlankAtOrBefore(At(Start - Period - 1), g_hz100), -2);
  // It is given once: a later one changes nothing
  timeline.StartAt(At(Start + 4'000'000));
  EXPECT_EQ(TimeOf(timeline, 0), Start);
}

TEST(VBlankTimeline, TheFirstReadingIsTakenWholeAndIsTheVerticalBlankNearestToIt)
{
  PC::VBlankTimeline timeline;
  timeline.StartAt(At(Start));
  // 2 ms after vertical blank 3 as the timeline has it: that one, at the reading's time
  Read(timeline, Start + (3 * Period) + 2'000'000);
  EXPECT_TRUE(timeline.HasReading());
  EXPECT_EQ(TimeOf(timeline, 3), Start + (3 * Period) + 2'000'000);
  EXPECT_EQ(TimeOf(timeline, 0), Start + 2'000'000);
  EXPECT_EQ(timeline.Jumps(), 0u);

  // 6 ms after one is 4 ms before the next: the next
  PC::VBlankTimeline other;
  other.StartAt(At(Start));
  Read(other, Start + (3 * Period) + 6'000'000);
  EXPECT_EQ(TimeOf(other, 4), Start + (3 * Period) + 6'000'000);

  // Without a time before it the first reading is vertical blank 0
  PC::VBlankTimeline bare;
  Read(bare, Start + 123);
  EXPECT_TRUE(bare.HasReading());
  EXPECT_EQ(TimeOf(bare, 0), Start + 123);
}

TEST(VBlankTimeline, AReadingNearAVerticalBlankMovesItAQuarterOfTheWay)
{
  PC::VBlankTimeline timeline;
  Read(timeline, Start);
  // 0.8 ms after vertical blank 5: within an eighth of a period, so it is taken, and the blank moves 0.2 ms
  Read(timeline, Start + (5 * Period) + 800'000);
  EXPECT_EQ(TimeOf(timeline, 5), Start + (5 * Period) + 200'000);
  // The next one, the same 0.8 ms after the first: 0.6 ms off now, and a quarter of that further
  Read(timeline, Start + (6 * Period) + 800'000);
  EXPECT_EQ(TimeOf(timeline, 6), Start + (6 * Period) + 350'000);
  // Before a vertical blank too
  Read(timeline, Start + (7 * Period) - 450'000);
  EXPECT_EQ(TimeOf(timeline, 7), Start + (7 * Period) + 150'000);
  // An eighth of a period off is still near
  Read(timeline, Start + (8 * Period) + 150'000 + (Period / 8));
  EXPECT_EQ(timeline.Jumps(), 0u);
  static_assert(PC::VBlankTimeline::FollowDivisor == 4);
  static_assert(PC::VBlankTimeline::JumpDivisor == 8);
}

TEST(VBlankTimeline, AReadingThatIsOffIsCountedAndEightInARowOnOneGridMoveTheVerticalBlanks)
{
  PC::VBlankTimeline timeline;
  Read(timeline, Start);
  // 3 ms off: more than an eighth of a period. Counted, and the vertical blanks stay
  Read(timeline, Start + (2 * Period) + 3'000'000);
  EXPECT_EQ(timeline.Jumps(), 1u);
  EXPECT_EQ(TimeOf(timeline, 2), Start + (2 * Period));
  // Six more, each a whole number of periods after the one before it: still not taken
  for (int64_t number = 3; number <= 8; ++number)
  {
    Read(timeline, Start + (number * Period) + 3'000'000);
    ASSERT_EQ(TimeOf(timeline, number), Start + (number * Period)) << number;
  }
  EXPECT_EQ(timeline.Jumps(), 7u);
  // The eighth in a row on that grid: it is the display's, and the vertical blanks are there now, whole
  Read(timeline, Start + (10 * Period) + 3'000'000);
  EXPECT_EQ(timeline.Jumps(), 8u);
  EXPECT_EQ(TimeOf(timeline, 10), Start + (10 * Period) + 3'000'000);
  EXPECT_EQ(TimeOf(timeline, 0), Start + 3'000'000);
  static_assert(PC::VBlankTimeline::ReadingsToMoveGrid == 8u);

  // Readings that are off and on no grid of their own never move them: each is counted, and the count in a row starts again
  for (int64_t number = 11; number <= 40; ++number)
  {
    Read(timeline, Start + (number * Period) + 3'000'000 + ((number % 2) == 0 ? 2'500'000 : 5'000'000));
    ASSERT_EQ(TimeOf(timeline, 0), Start + 3'000'000) << number;
  }
  EXPECT_EQ(timeline.Jumps(), 38u);
  // Two readings of the same vertical blank are not two that agree: they are less than half a period apart
  PC::VBlankTimeline same;
  Read(same, Start);
  for (int64_t count = 0; count < 20; ++count)
  {
    Read(same, Start + (2 * Period) + 3'000'000 + count, Start + (2 * Period) + 3'000'000 + count);
  }
  EXPECT_EQ(same.Jumps(), 20u);
  EXPECT_EQ(TimeOf(same, 0), Start);
  // A reading that is taken in between ends a row
  PC::VBlankTimeline row;
  Read(row, Start);
  for (int64_t number = 1; number <= 30; ++number)
  {
    Read(row, Start + (number * Period) + ((number % 5) == 0 ? 0 : 3'000'000));
  }
  EXPECT_EQ(row.Jumps(), 24u);
  EXPECT_EQ(TimeOf(row, 0), Start);
}

TEST(VBlankTimeline, AReadingReadBeforeTheNewestIsNotTaken)
{
  PC::VBlankTimeline timeline;
  Read(timeline, Start, Start + 500'000);
  // Read earlier: not looked at, near or off
  Read(timeline, Start + Period + 800'000, Start + 400'000);
  Read(timeline, Start + Period + 3'000'000, Start + 400'000);
  EXPECT_EQ(TimeOf(timeline, 1), Start + Period);
  EXPECT_EQ(timeline.Jumps(), 0u);
  // Read at the same time as the newest: taken
  Read(timeline, Start + Period + 800'000, Start + 500'000);
  EXPECT_EQ(TimeOf(timeline, 1), Start + Period + 200'000);
}

TEST(VBlankTimeline, ClearedTheNextReadingIsTheFirst)
{
  PC::VBlankTimeline timeline;
  Read(timeline, Start);
  Read(timeline, Start + Period + 3'000'000);
  EXPECT_EQ(timeline.Jumps(), 1u);
  timeline.Clear();
  EXPECT_FALSE(timeline.HasReading());
  // Far off where the vertical blanks were: taken whole, as vertical blank 0, and what was counted stays counted
  Read(timeline, Start + (5 * Period) + 4'000'000);
  EXPECT_TRUE(timeline.HasReading());
  EXPECT_EQ(TimeOf(timeline, 0), Start + (5 * Period) + 4'000'000);
  EXPECT_EQ(timeline.Jumps(), 1u);
  // And a time the pacer gives is the first after a clear without a reading
  timeline.Clear();
  timeline.StartAt(At(Start + (9 * Period)));
  EXPECT_EQ(TimeOf(timeline, 0), Start + (9 * Period));
}
