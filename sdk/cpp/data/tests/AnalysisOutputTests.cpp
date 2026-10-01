// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// summary.json and the CSVs: the format version, columns by name, whole ticks.
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/AnalysisFiles.hpp>
#include <mb/framepacing/data/analysis/AnalysisSummary.hpp>
#include <mb/framepacing/data/analysis/CapturesCsv.hpp>
#include <mb/framepacing/data/analysis/FramesCsv.hpp>
#include <mb/framepacing/data/analysis/Milliseconds.hpp>
#include <gtest/gtest.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <string_view>

namespace FP = MB::FramePacing;
namespace FD = MB::FramePacing::Data;

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
  const auto path = std::filesystem::temp_directory_path() / "mb_framepacing_data_test-frames.csv";
  {
    std::ofstream out(path, std::ios::binary);
    out << "frameIndex,newColumn,segment,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,driftMs,flags,cpuBusyMs\r\n"
        << "7,x,0,116.6667,3,50,33.3333,2,1,-0.5,SkippedBefore|Late,\r\n";
  }
  const auto rows = FD::ReadFrames(path);
  std::filesystem::remove(path);
  ASSERT_EQ(rows.size(), 1u);
  EXPECT_EQ(rows[0].FrameIndex, 7u);
  EXPECT_EQ(rows[0].AnimationTime, FP::TimeSpan(1'166'667));
  EXPECT_EQ(rows[0].OnScreen, FP::TimeSpan(333'333));
  EXPECT_EQ(rows[0].Drift, FP::TimeSpan(-5'000));
  EXPECT_EQ(rows[0].Flags, (std::vector<std::string>{"SkippedBefore", "Late"}));
  EXPECT_FALSE(rows[0].CpuBusy.has_value()) << "an empty cell";
  EXPECT_FALSE(rows[0].LastSeenTime.has_value()) << "a column the file lacks";
}

TEST(AnalysisOutput, TheMarkersValuesMustFit32Bits)
{
  const auto path = std::filesystem::temp_directory_path() / "mb_framepacing_data_test-marker-values.csv";
  const auto read = [&path](const std::string_view cpuBusyMs)
  {
    {
      std::ofstream out(path, std::ios::binary);
      out << "segment,frameIndex,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,driftMs,flags,cpuBusyMs\r\n"
          << "0,1,0,0,0,16.6667,1,0,0,," << cpuBusyMs << "\r\n";
    }
    auto rows = FD::ReadFrames(path);
    std::filesystem::remove(path);
    return rows;
  };
  EXPECT_EQ(read("429496.7295").at(0).CpuBusy, FP::TimeSpan32::MaxValue()) << "the largest";
  EXPECT_EQ(read("8").at(0).CpuBusy, FP::TimeSpan32(80'000u));
  EXPECT_THROW((void)read("429496.7296"), FD::DataFormatError);
  EXPECT_THROW((void)read("-0.0001"), FD::DataFormatError);
}

TEST(AnalysisOutput, CapturesCarrySourceDropsMissedRefreshesAndTheSyncMarker)
{
  const auto path = std::filesystem::temp_directory_path() / "mb_framepacing_data_test-captures.csv";
  {
    std::ofstream out(path, std::ios::binary);
    out << "captureIndex,captureMs,status,kind,runId,frameIndex,animationMs,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,hostMs,deviceMs,"
           "payloadHex\r\n"
        << "4,66.6667,Torn,Frame,7,12,200,3,1,7,11,70.1,66.6667,4D46\r\n"
        << "5,,NotRecorded,,,,,0,0,,,,,\r\n";
  }
  const auto rows = FD::ReadCaptures(path);
  std::filesystem::remove(path);
  ASSERT_EQ(rows.size(), 2u);
  EXPECT_EQ(rows[0].Status, "Torn");
  EXPECT_EQ(rows[0].FrameIndex, 12u);
  EXPECT_EQ(rows[0].SyncRunId, 7u);
  EXPECT_EQ(rows[0].SyncFrameIndex, 11u);
  EXPECT_EQ(rows[0].SourceDropsBefore, 3);
  EXPECT_EQ(rows[0].MissedBefore, 1);
  EXPECT_EQ(rows[0].Payload, (std::vector<uint8_t>{0x4D, 0x46}));
  EXPECT_FALSE(rows[1].CaptureTime.has_value()) << "a capture the recorder dropped";
  EXPECT_FALSE(rows[1].SyncFrameIndex.has_value());
}

TEST(AnalysisOutput, MillisecondsAreWholeTicks)
{
  EXPECT_EQ(FD::ParseMilliseconds("16.6667"), FP::TimeSpan(166'667));
  EXPECT_EQ(FD::ParseMilliseconds("-0.0003"), FP::TimeSpan(-3));
  EXPECT_EQ(FD::ParseMilliseconds("12345678.9012"), FP::TimeSpan(123'456'789'012));
  EXPECT_EQ(FD::ParseMilliseconds("0"), FP::TimeSpan());
  EXPECT_THROW((void)FD::ParseMilliseconds("1e-5"), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseMilliseconds(""), FD::DataFormatError);
}
