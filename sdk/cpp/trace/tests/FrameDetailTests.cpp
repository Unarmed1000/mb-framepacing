// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The frame record's second part (optional, not checked against the capture): the names of its slices and moments against the
// golden data both languages read (test-data/trace/frame-details.csv), and its strings as the document has them.
#include <mb/framepacing/trace/FrameDetail.hpp>
#include <mb/framepacing/trace/FrameDetailName.hpp>
#include <mb/framepacing/trace/FrameRecordText.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <limits>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace TR = MB::FramePacing::Trace;
namespace Text = MB::FramePacing::Trace::FrameRecordText;

namespace
{
  // The names are there at compile time, and none allocates
  static_assert(TR::FrameDetailName(TR::FrameDetail::Present, 1, 388).View() == "mbfp present 1 388");
  static_assert(TR::FrameDetailName().View().empty());
  static_assert(TR::FrameDetailName::PrefixOf(static_cast<TR::FrameDetail>(0)).empty());
  // A few of the strings, as sdk/doc/trace-frame-record.md has them
  static_assert(Text::PresentTrackName == "mbfp presents");
  static_assert(Text::EnvironmentName == "mbfp environment");
  static_assert(Text::FramesInFlight == "framesInFlight");
  static_assert(Text::PresentModeQueueLateAtOnce == "queue, late at once");
  static_assert(Text::PlatformFeatures == "platformFeatures");

  std::optional<std::filesystem::path> FindGoldenFile()
  {
    for (auto folder = std::filesystem::path(MB_FRAMEPACING_TRACE_SOURCE_DIR); !folder.empty(); folder = folder.parent_path())
    {
      auto candidate = folder / "test-data" / "trace" / "frame-details.csv";
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

  std::optional<TR::FrameDetail> DetailOf(const std::string_view text)
  {
    for (const TR::FrameDetail detail :
         {TR::FrameDetail::Wait, TR::FrameDetail::Present, TR::FrameDetail::Gpu, TR::FrameDetail::Display, TR::FrameDetail::NotPresented})
    {
      constexpr std::array<std::string_view, 6> Names = {"", "Wait", "Present", "Gpu", "Display", "NotPresented"};
      if (Names[static_cast<uint32_t>(detail)] == text)
      {
        return detail;
      }
    }
    return std::nullopt;
  }
}

TEST(FrameDetailName, EveryDetailIsNamedAsTheGoldenDataHasIt)
{
  const auto path = FindGoldenFile();
  if (!path)
  {
    GTEST_SKIP() << "test-data/trace is not above this folder";
  }
  std::ifstream file(*path);
  std::string line;
  ASSERT_TRUE(std::getline(file, line));
  ASSERT_EQ(line.substr(0, line.find_last_not_of('\r') + 1), "detail,runId,frameIndex,name");
  uint32_t rows = 0;
  while (std::getline(file, line))
  {
    if (!line.empty() && line.back() == '\r')
    {
      line.pop_back();
    }
    if (line.empty())
    {
      continue;
    }
    std::vector<std::string> columns;
    std::size_t begin = 0;
    for (std::size_t end = line.find(','); end != std::string::npos && columns.size() < 3; end = line.find(',', begin))
    {
      columns.emplace_back(line.substr(begin, end - begin));
      begin = end + 1;
    }
    columns.emplace_back(line.substr(begin));
    ASSERT_EQ(columns.size(), 4u) << line;
    const auto detail = DetailOf(columns[0]);
    if (!detail.has_value())
    {
      ADD_FAILURE() << "no detail: " << line;
      continue;
    }
    const TR::FrameDetailName name(*detail, static_cast<uint32_t>(std::stoul(columns[1])), std::stoull(columns[2]));
    EXPECT_EQ(name.View(), columns[3]) << line;
    EXPECT_EQ(std::strlen(name.CStr()), name.View().size()) << line;
    EXPECT_LE(name.View().size(), TR::FrameDetailName::MaxLength) << line;
    EXPECT_TRUE(name.View().starts_with(TR::FrameDetailName::PrefixOf(*detail))) << line;
    ++rows;
  }
  // A row per detail
  EXPECT_EQ(rows, 5u);
}

TEST(FrameDetailName, TheLongestNameFitsAndTwoNamesCompareByText)
{
  const TR::FrameDetailName longest(TR::FrameDetail::NotPresented, std::numeric_limits<uint32_t>::max(), std::numeric_limits<uint64_t>::max());
  EXPECT_EQ(longest.View().size(), TR::FrameDetailName::MaxLength);
  EXPECT_EQ(longest, TR::FrameDetailName(TR::FrameDetail::NotPresented, std::numeric_limits<uint32_t>::max(), std::numeric_limits<uint64_t>::max()));
  EXPECT_FALSE(TR::FrameDetailName(TR::FrameDetail::Wait, 1, 2) == TR::FrameDetailName(TR::FrameDetail::Gpu, 1, 2));
  // A value that is no detail has no name
  EXPECT_TRUE(TR::FrameDetailName(static_cast<TR::FrameDetail>(9), 1, 2).View().empty());
  EXPECT_STREQ(TR::FrameDetailName(static_cast<TR::FrameDetail>(9), 1, 2).CStr(), "");
}

TEST(FrameRecordText, ThePartTwoNamesAreTheirOwnAndNoneIsTheRecords)
{
  // No name of part 2 begins as the record's does: a reader of the record never takes one for it
  for (const std::string_view prefix :
       {Text::WaitNamePrefix, Text::PresentNamePrefix, Text::GpuNamePrefix, Text::DisplayNamePrefix, Text::NotPresentedNamePrefix,
        Text::EnvironmentName, Text::SwapChainRecreatedName, Text::SwapIntervalChangeName})
  {
    EXPECT_FALSE(prefix.starts_with(Text::NamePrefix)) << prefix;
    EXPECT_TRUE(prefix.starts_with("mbfp ")) << prefix;
  }
  for (const std::string_view track : {Text::WaitTrackName, Text::PresentTrackName, Text::GpuTrackName, Text::DisplayTrackName, Text::RunTrackName})
  {
    EXPECT_NE(track, Text::TrackName);
    EXPECT_TRUE(track.starts_with("mbfp ")) << track;
  }
}
