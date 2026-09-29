// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// summary.json and the CSVs: the format version, columns by name, whole ticks.
#include <mb/framepacingdata/FramePacingData.hpp>
#include <mb/framepacingdata/Version.hpp>
#include <gtest/gtest.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <string_view>

namespace FD = MB::FramePacingData;

namespace
{
  constexpr std::string_view MinimalSummary = R"({
  "scanout": "SingleScanout",
  "analysedUtc": "2026-01-01T00:00:00Z",
  "capturePeriodMs": 16.6667,
  "measurementResolutionMs": 16.6667,
  "errorThresholdMs": 1,
  "runs": []
})";
}

TEST(AnalysisOutput, TheVersionIsTheDataLibraries)
{
  EXPECT_EQ(FD::VersionString, MB_FRAMEPACINGDATA_EXPECTED_VERSION);
  const FD::LibraryVersion version = FD::GetLibraryVersion();
  EXPECT_EQ(version.Text, FD::VersionString);
  EXPECT_EQ(version.Major, FD::VersionMajor);
  EXPECT_EQ(version.Minor, FD::VersionMinor);
  EXPECT_EQ(version.Patch, FD::VersionPatch);
}

TEST(AnalysisOutput, ASummaryWithoutAFormatVersionIsFormatOne)
{
  const auto summary = FD::ParseSummary(MinimalSummary);
  EXPECT_EQ(summary.FormatVersion, 1);
  EXPECT_TRUE(summary.Runs.empty());
  EXPECT_TRUE(summary.Markers.empty());
  EXPECT_EQ(summary.CapturePeriodMs, 16.6667);
  EXPECT_EQ(summary.Scanout, "SingleScanout");
}

TEST(AnalysisOutput, ANewerFormatIsRefused)
{
  const std::string newer = "{ \"formatVersion\": 2," + std::string(MinimalSummary.substr(1));
  try
  {
    (void)FD::ParseSummary(newer);
    FAIL() << "a newer format was read";
  }
  catch (const FD::DataFormatError& error)
  {
    EXPECT_NE(std::string(error.what()).find("update"), std::string::npos);
  }
}

TEST(AnalysisOutput, FileNamesFollowTheRunIds)
{
  EXPECT_EQ(FD::FramesFileName(3), "run-3-frames.csv");
  EXPECT_EQ(FD::FramesFileName(3, 1), "run-3-2-frames.csv");
}

TEST(AnalysisOutput, FramesAreReadByColumnNameWhateverTheOrder)
{
  const auto path = std::filesystem::temp_directory_path() / "mb_framepacingdata_test-frames.csv";
  {
    std::ofstream out(path, std::ios::binary);
    out << "frameIndex,newColumn,segment,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,driftMs,flags,cpuBusyMs\r\n"
        << "7,x,0,116.6667,3,50,33.3333,2,1,-0.5,SkippedBefore|Late,\r\n";
  }
  const auto rows = FD::ReadFrames(path);
  std::filesystem::remove(path);
  ASSERT_EQ(rows.size(), 1u);
  EXPECT_EQ(rows[0].FrameIndex, 7u);
  EXPECT_EQ(rows[0].AnimationTicks, 1'166'667);
  EXPECT_EQ(rows[0].OnScreenTicks, 333'333);
  EXPECT_EQ(rows[0].DriftTicks, -5'000);
  EXPECT_EQ(rows[0].Flags, (std::vector<std::string>{"SkippedBefore", "Late"}));
  EXPECT_FALSE(rows[0].CpuBusyTicks.has_value()) << "an empty cell";
  EXPECT_FALSE(rows[0].LastSeenTicks.has_value()) << "a column the file lacks";
}

TEST(AnalysisOutput, MillisecondsAreWholeTicks)
{
  EXPECT_EQ(FD::ParseTicks("16.6667"), 166'667);
  EXPECT_EQ(FD::ParseTicks("-0.0003"), -3);
  EXPECT_EQ(FD::ParseTicks("12345678.9012"), 123'456'789'012);
  EXPECT_EQ(FD::ParseTicks("0"), 0);
  EXPECT_THROW((void)FD::ParseTicks("1e-5"), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseTicks(""), FD::DataFormatError);
}
