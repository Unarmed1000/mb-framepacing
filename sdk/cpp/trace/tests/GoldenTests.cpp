// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The golden data (test-data/trace/frame-records.csv; every language's trace module reads the same file): each row is what a
// payload's constructor is given, before any cap, and then the record that payload gives: the slice's name, start and length and
// its annotations in order (name=value, joined by ';'). A row whose name is empty is a payload that gets no record.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/trace/FrameRecord.hpp>
#include <mb/framepacing/trace/FrameRecordAnnotation.hpp>
#include <mb/framepacing/trace/FrameRecordUtil.hpp>
#include <gtest/gtest.h>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <limits>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;
namespace TR = MB::FramePacing::Trace;

namespace
{
  constexpr std::string_view FileName = "frame-records.csv";
  //! The file's columns: the payload constructor's arguments in the order of the wire format (without the flags), then the record
  constexpr std::string_view Header =
    "kind,runId,frameIndex,animationNs,preferredNs,targetNs,intendedDisplayNs,cpuStartNs,cpuBusyNs,name,startNs,durationNs,annotations";
  constexpr std::size_t ColumnCount = 13;
  //! The rows the file has at the least: a row for each rule of the record
  constexpr uint32_t MinRowCount = 9;

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

  //! The parts of a text between a separator: one more than the separators it has, empty ones included
  std::vector<std::string> Split(const std::string_view text, const char separator)
  {
    std::vector<std::string> parts;
    std::size_t begin = 0;
    for (std::size_t end = text.find(separator); end != std::string_view::npos; end = text.find(separator, begin))
    {
      parts.emplace_back(text.substr(begin, end - begin));
      begin = end + 1;
    }
    parts.emplace_back(text.substr(begin));
    return parts;
  }

  //! A line as the file has it on every system: a checkout may end its lines with a carriage return too
  std::string_view WithoutCarriageReturn(const std::string_view line)
  {
    return !line.empty() && line.back() == '\r' ? line.substr(0, line.size() - 1) : line;
  }

  std::optional<FM::MarkerKind> KindOf(const std::string_view text)
  {
    if (text == "Frame")
    {
      return FM::MarkerKind::Frame;
    }
    if (text == "SequenceStart")
    {
      return FM::MarkerKind::SequenceStart;
    }
    if (text == "SequenceEnd")
    {
      return FM::MarkerKind::SequenceEnd;
    }
    if (text == "Sync")
    {
      return FM::MarkerKind::Sync;
    }
    return std::nullopt;
  }

  //! The payload of a row: its first nine cells, given to the constructor as they are
  FM::Payload PayloadOf(const FM::MarkerKind kind, const std::vector<std::string>& cells)
  {
    const unsigned long long runId = std::stoull(cells[1]);
    EXPECT_LE(runId, std::numeric_limits<uint32_t>::max());
    return {kind,
            static_cast<uint32_t>(runId),
            std::stoull(cells[2]),
            FM::MarkerFlags::NoFlags,
            FP::NanosecondTimeSpan(std::stoll(cells[3])),
            FP::NanosecondTimeDuration::FromNanoseconds(std::stoll(cells[4])),
            FP::NanosecondTimeDuration::FromNanoseconds(std::stoll(cells[5])),
            FP::NanosecondTickCount(std::stoll(cells[6])),
            FP::NanosecondTickCount(std::stoll(cells[7])),
            FP::NanosecondTimeDuration::FromNanoseconds(std::stoll(cells[8]))};
  }
}

TEST(Golden, EveryPayloadGivesTheRecordOfItsRow)
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

  uint32_t rowCount = 0;
  uint32_t recordCount = 0;
  while (std::getline(stream, line))
  {
    const std::string_view row = WithoutCarriageReturn(line);
    if (row.empty())
    {
      continue;
    }
    SCOPED_TRACE(std::string(row));
    ++rowCount;
    const std::vector<std::string> cells = Split(row, ',');
    ASSERT_EQ(cells.size(), ColumnCount);
    const std::optional<FM::MarkerKind> kind = KindOf(cells[0]);
    if (!kind.has_value())
    {
      ADD_FAILURE() << "a kind that is not Frame, SequenceStart, SequenceEnd or Sync";
      return;
    }

    // A record that held values, so that a row without a record also shows that the call empties it
    TR::FrameRecord record;
    record.StartTime = FP::NanosecondTickCount(1);
    record.AnnotationCount = 1;
    const bool created = TR::FrameRecordUtil::TryCreate(PayloadOf(kind.value(), cells), record);

    const std::string& expectedName = cells[9];
    if (expectedName.empty())
    {
      // No record: the row has nothing else of one either
      EXPECT_TRUE(cells[10].empty() && cells[11].empty() && cells[12].empty());
      EXPECT_FALSE(created);
      EXPECT_TRUE(record.Name.View().empty());
      EXPECT_EQ(record.StartTime, FP::NanosecondTickCount());
      EXPECT_EQ(record.Duration, FP::NanosecondTimeDuration());
      EXPECT_TRUE(record.Annotations().empty());
      continue;
    }

    ++recordCount;
    ASSERT_TRUE(created);
    EXPECT_EQ(record.Name.View(), expectedName);
    EXPECT_STREQ(record.Name.CStr(), expectedName.c_str());
    EXPECT_EQ(record.StartTime.Nanoseconds(), std::stoll(cells[10]));
    EXPECT_EQ(record.Duration.Nanoseconds(), std::stoll(cells[11]));
    const std::vector<std::string> expected = cells[12].empty() ? std::vector<std::string>() : Split(cells[12], ';');
    const std::span<const TR::FrameRecordAnnotation> annotations = record.Annotations();
    ASSERT_EQ(annotations.size(), expected.size());
    for (std::size_t index = 0; index < expected.size(); ++index)
    {
      const std::size_t equals = expected[index].find('=');
      ASSERT_NE(equals, std::string::npos) << expected[index];
      EXPECT_EQ(annotations[index].Name, expected[index].substr(0, equals)) << index;
      EXPECT_EQ(annotations[index].Value, std::stoll(expected[index].substr(equals + 1))) << index;
    }
  }
  EXPECT_GE(rowCount, MinRowCount) << "the file has fewer rows than the record has rules";
  // Both kinds of row are there: payloads that get a record and payloads that get none
  EXPECT_GT(recordCount, 0u);
  EXPECT_LT(recordCount, rowCount);
}
