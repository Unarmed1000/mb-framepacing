// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Which of the tracks of one name a slice goes on (TrackLanes), held to the golden data every language reads
// (test-data/trace/track-lanes.csv: sequences of slices, each sequence on lanes of its own, with the lane, the lanes taken and the
// overflows after each slice), and the record's lane from FrameRecordUtil::TryCreate.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/trace/FrameRecord.hpp>
#include <mb/framepacing/trace/FrameRecordUtil.hpp>
#include <mb/framepacing/trace/TrackLanes.hpp>
#include <gtest/gtest.h>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;
namespace TR = MB::FramePacing::Trace;

namespace
{
  constexpr std::string_view FileName = "track-lanes.csv";
  constexpr std::string_view Header = "sequence,startNs,endNs,lane,laneCount,overflows";

  std::optional<std::filesystem::path> FindGoldenFile()
  {
    for (auto folder = std::filesystem::path(MB_FRAMEPACING_TRACE_SOURCE_DIR); !folder.empty(); folder = folder.parent_path())
    {
      auto candidate = folder / "test-data" / "trace" / FileName;
      if (std::filesystem::exists(candidate))
      {
        return candidate;
      }
      if (folder == folder.parent_path())
      {
        break;
      }
    }
    return std::nullopt;
  }

  std::vector<std::string> Split(const std::string_view text)
  {
    std::vector<std::string> parts;
    std::size_t begin = 0;
    for (std::size_t end = text.find(','); end != std::string_view::npos; end = text.find(',', begin))
    {
      parts.emplace_back(text.substr(begin, end - begin));
      begin = end + 1;
    }
    parts.emplace_back(text.substr(begin));
    return parts;
  }

  std::string_view WithoutCarriageReturn(const std::string_view line)
  {
    return !line.empty() && line.back() == '\r' ? line.substr(0, line.size() - 1) : line;
  }

  FP::NanosecondTickCount At(const int64_t nanoseconds)
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  //! A frame marker's payload whose record is the slice from cpuStart for cpuBusy
  FM::Payload Frame(const uint64_t frameIndex, const int64_t cpuStart, const int64_t cpuBusy)
  {
    return {FM::MarkerKind::Frame,
            7u,
            frameIndex,
            FM::MarkerFlags::NoFlags,
            FP::NanosecondTimeSpan(0),
            {},
            {},
            {},
            {},
            At(cpuStart),
            FP::NanosecondTimeDuration::FromNanoseconds(cpuBusy)};
  }
}

TEST(TrackLanes, EverySequenceGetsTheLanesOfItsRows)
{
  const std::optional<std::filesystem::path> file = FindGoldenFile();
  if (!file.has_value())
  {
    GTEST_SKIP() << "test-data/trace not found (a copy of the library outside mb-framepacing)";
  }
  std::ifstream stream(*file, std::ios::binary);
  ASSERT_TRUE(stream.is_open()) << file->string();
  std::string line;
  ASSERT_TRUE(static_cast<bool>(std::getline(stream, line)));
  ASSERT_EQ(WithoutCarriageReturn(line), Header);

  TR::TrackLanes lanes;
  std::string sequence;
  uint32_t rows = 0;
  uint32_t overflows = 0;
  while (std::getline(stream, line))
  {
    const std::string_view text = WithoutCarriageReturn(line);
    if (text.empty())
    {
      continue;
    }
    const std::vector<std::string> cells = Split(text);
    ASSERT_EQ(cells.size(), 6u) << text;
    if (cells[0] != sequence)
    {
      // Every sequence on lanes of its own
      lanes.Clear();
      sequence = cells[0];
    }
    ++rows;
    EXPECT_EQ(lanes.Assign(At(std::stoll(cells[1])), At(std::stoll(cells[2]))), std::stoul(cells[3])) << text;
    EXPECT_EQ(lanes.LaneCount(), std::stoul(cells[4])) << text;
    EXPECT_EQ(lanes.Overflows(), std::stoul(cells[5])) << text;
    overflows += lanes.Overflows();
  }
  EXPECT_GE(rows, 50u);
  EXPECT_GT(overflows, 0u) << "the file has the overflows too";
}

TEST(TrackLanes, ASequentialLoopUsesOneTrackAndFramesInFlightOneEach)
{
  TR::TrackLanes lanes;
  EXPECT_EQ(TR::TrackLanes::MaxLaneCount, 16u);
  EXPECT_EQ(lanes.LaneCount(), 0u);
  // Each slice ends when the next begins: one lane
  for (int64_t frame = 0; frame < 10; ++frame)
  {
    EXPECT_EQ(lanes.Assign(At(frame * 100), At((frame + 1) * 100)), 0u);
  }
  EXPECT_EQ(lanes.LaneCount(), 1u);

  // Two frames in flight: lanes in turn; then back to one
  lanes.Clear();
  EXPECT_EQ(lanes.Assign(At(0), At(150)), 0u);
  EXPECT_EQ(lanes.Assign(At(100), At(250)), 1u);
  EXPECT_EQ(lanes.Assign(At(200), At(350)), 0u);
  EXPECT_EQ(lanes.Assign(At(400), At(450)), 0u);
  EXPECT_EQ(lanes.LaneCount(), 2u);
  EXPECT_EQ(lanes.Overflows(), 0u);
}

TEST(TrackLanes, WithEveryLaneTakenTheOneFreeFirstIsCounted)
{
  TR::TrackLanes lanes;
  for (uint32_t lane = 0; lane < TR::TrackLanes::MaxLaneCount; ++lane)
  {
    // Lane 5 is free first
    const int64_t end = lane == 5u ? 500 : 1000;
    EXPECT_EQ(lanes.Assign(At(lane), At(end)), lane);
  }
  EXPECT_EQ(lanes.Assign(At(20), At(30)), 5u);
  EXPECT_EQ(lanes.Overflows(), 1u);
  // The shorter slice did not free the lane early: at 400 the lane's slice to 500 is still open
  EXPECT_EQ(lanes.Assign(At(400), At(410)), 5u);
  EXPECT_EQ(lanes.Overflows(), 2u);
  EXPECT_EQ(lanes.Assign(At(600), At(700)), 5u) << "free again";
  EXPECT_EQ(lanes.Overflows(), 2u);

  lanes.Clear();
  EXPECT_EQ(lanes.LaneCount(), 0u);
  EXPECT_EQ(lanes.Overflows(), 0u);
  EXPECT_EQ(lanes.Assign(At(0), At(1)), 0u) << "nothing left of before";
}

TEST(FrameRecordUtil, TheRecordsOfFramesInFlightGetTheirOwnLanes)
{
  TR::TrackLanes lanes;
  TR::FrameRecord record;
  // Frame 2 starts before frame 1 is presented, frame 3 after
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(Frame(1u, 1'000, 1'500), lanes, record));
  EXPECT_EQ(record.Lane, 0u);
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(Frame(2u, 2'000, 1'500), lanes, record));
  EXPECT_EQ(record.Lane, 1u);
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(Frame(3u, 2'600, 100), lanes, record));
  EXPECT_EQ(record.Lane, 0u);
  EXPECT_EQ(record.Name.View(), "mbfp frame 7 3");

  // A marker that gets no record takes no lane
  EXPECT_FALSE(TR::FrameRecordUtil::TryCreate(Frame(4u, 0, 100), lanes, record));
  EXPECT_EQ(record.Lane, 0u);
  EXPECT_FALSE(TR::FrameRecordUtil::TryCreate(Frame(4u, 2'700, 100).WithKind(FM::MarkerKind::Sync), lanes, record));
  EXPECT_EQ(lanes.LaneCount(), 2u);
  EXPECT_EQ(lanes.Overflows(), 0u);

  // Without lanes every record is on lane 0
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(Frame(2u, 2'000, 1'500), record));
  EXPECT_EQ(record.Lane, 0u);
}
