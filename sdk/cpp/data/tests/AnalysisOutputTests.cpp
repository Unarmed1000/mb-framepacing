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

  constexpr std::string_view Counts = R"("counts": { "captures": 10, "decoded": 9, "undecodable": 1, "torn": 0, "notRecorded": 0,
    "sourceDroppedFrames": 0, "missedCaptures": 0, "presentedFrames": 8, "skippedFrameIndices": 0, "droppedFrames": 0,
    "outOfOrderCaptures": 0, "segments": 1 })";

  constexpr std::string_view Statistics = R"("statistics": { "framesWithAnimationError": 2, "errorPerFrameMs": 0.5, "percentError": 25,
    "excludedStaticFrames": 0, "uncertainSteps": 0 })";

  constexpr std::string_view RunStart = R"("runId": 7, "hasStartMarker": true, "hasEndMarker": false, "framesFile": "run-7-frames.csv")";

  //! A summary whose one run has the given members.
  std::string SummaryWithRun(const std::string& members)
  {
    return R"({ "capturePeriodMs": 16.6667, "errorThresholdMs": 1, "runs": [ { )" + members + " } ] }";
  }

  std::string OneRun(const std::string_view start = RunStart, const std::string_view counts = Counts, const std::string_view statistics = Statistics,
                     const std::string_view more = {})
  {
    std::string members;
    for (const std::string_view part : {start, counts, statistics, more})
    {
      if (!part.empty())
      {
        members += (members.empty() ? "" : ", ") + std::string(part);
      }
    }
    return SummaryWithRun(members);
  }

  //! The minimal summary with one more member in front.
  std::string MinimalWith(const std::string& member)
  {
    return "{ " + member + "," + std::string(MinimalSummary.substr(1));
  }
}

TEST(AnalysisOutput, ARunIsReadWithItsCountsAndStatistics)
{
  const auto summary = FD::ParseSummary(OneRun());
  ASSERT_EQ(summary.Runs.size(), 1u);
  EXPECT_EQ(summary.Runs[0].RunId, 7u);
  EXPECT_EQ(summary.Runs[0].FramesFile, "run-7-frames.csv");
  EXPECT_EQ(summary.Runs[0].Counts.Captures, 10);
  EXPECT_EQ(summary.Runs[0].Counts.PresentedFrames, 8);
  EXPECT_EQ(summary.Runs[0].Statistics.FramesWithAnimationError, 2);
  EXPECT_EQ(summary.Runs[0].Statistics.PercentError, 25.0);
  EXPECT_EQ(summary.MeasurementResolutionMs, 16.6667) << "a file without it: the capture period";
}

TEST(AnalysisOutput, ARunWithoutItsCountsOrStatisticsIsNotASummary)
{
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, {}, Statistics)), FD::DataFormatError) << "no counts";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, {})), FD::DataFormatError) << "no statistics";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, R"("counts": 3)", Statistics)), FD::DataFormatError) << "counts that are a number";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, R"("statistics": [])")), FD::DataFormatError) << "statistics that are an array";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics, R"("histograms": {})")), FD::DataFormatError) << "empty histograms";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics, R"("histograms": { "animationErrorMs": 1, "displayDeltaMs": 2 })")),
               FD::DataFormatError)
    << "histograms that are numbers";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics, R"("pacing": 1)")), FD::DataFormatError) << "pacing that is a number";
}

TEST(AnalysisOutput, ListsMustBeLists)
{
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodMs": 16.6667, "errorThresholdMs": 1, "runs": { "a": 1 } })"), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodMs": 16.6667, "errorThresholdMs": 1, "runs": [ 5 ] })"), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("markers": { "bounds": "0,0,1,1", "moduleSizePx": 3 })")), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("warnings": "one text")")), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("warnings": [ 1 ])")), FD::DataFormatError);
  EXPECT_EQ(FD::ParseSummary(MinimalWith(R"("warnings": [ "one", "two" ])")).Warnings, (std::vector<std::string>{"one", "two"}));
}

TEST(AnalysisOutput, NumbersMustFitTheirFields)
{
  EXPECT_THROW((void)FD::ParseSummary(OneRun(R"("runId": -1, "hasStartMarker": true, "hasEndMarker": false, "framesFile": "f")")),
               FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(OneRun(R"("runId": 4294967296, "hasStartMarker": true, "hasEndMarker": false, "framesFile": "f")")),
               FD::DataFormatError);
  EXPECT_EQ(FD::ParseSummary(OneRun(R"("runId": 4294967295, "hasStartMarker": true, "hasEndMarker": false, "framesFile": "f")")).Runs[0].RunId,
            4294967295u);
  EXPECT_THROW((void)FD::ParseSummary(OneRun(R"("runId": 1.5, "hasStartMarker": true, "hasEndMarker": false, "framesFile": "f")")),
               FD::DataFormatError)
    << "a fraction for a whole number";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(R"("runId": 1, "hasStartMarker": 1, "hasEndMarker": false, "framesFile": "f")")), FD::DataFormatError)
    << "a number for a flag";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(R"("runId": 1, "hasStartMarker": true, "hasEndMarker": false, "framesFile": 5)")), FD::DataFormatError)
    << "a number for a text";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, R"("statistics": { "framesWithAnimationError": 1e300, "errorPerFrameMs": 0.5,
    "percentError": 25, "excludedStaticFrames": 0, "uncertainSteps": 0 })")),
               FD::DataFormatError)
    << "a count beyond 64 bits";
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("formatVersion": 4294967297)")), FD::DataFormatError) << "not format 1 by wrapping";
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodMs": "16", "errorThresholdMs": 1 })"), FD::DataFormatError) << "a text for a number";
  EXPECT_EQ(FD::ParseSummary(R"({ "capturePeriodMs": 16, "errorThresholdMs": 1 })").CapturePeriodMs, 16.0) << "a whole number for a real one";
}

TEST(AnalysisOutput, RequiredFieldsAreRequired)
{
  EXPECT_THROW((void)FD::ParseSummary(R"({ "errorThresholdMs": 1 })"), FD::DataFormatError) << "no capture period";
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodMs": 16.6667 })"), FD::DataFormatError) << "no error threshold";
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodMs": null, "errorThresholdMs": 1 })"), FD::DataFormatError) << "null is absent";
  EXPECT_THROW((void)FD::ParseSummary("[]"), FD::DataFormatError) << "not an object";
  EXPECT_THROW((void)FD::ParseSummary("{ \"capturePeriodMs\": "), FD::DataFormatError) << "not JSON";
  EXPECT_THROW((void)FD::ParseSummary(""), FD::DataFormatError) << "empty";
}

TEST(AnalysisOutput, FormatVersionZeroIsFormatOne)
{
  EXPECT_EQ(FD::ParseSummary(MinimalWith(R"("formatVersion": 0)")).FormatVersion, 1) << "as a file without it (C# reads both as 0)";
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("formatVersion": -1)")), FD::DataFormatError);
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
