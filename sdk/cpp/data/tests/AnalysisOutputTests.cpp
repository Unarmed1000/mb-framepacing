// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// summary.json and the CSVs: the format version, columns by name, times as whole nanoseconds, and content that is refused (files from
// before the nanoseconds, with their times in ticks of 100 ns under "...Ticks" names, among it).
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/AnalysisFiles.hpp>
#include <mb/framepacing/data/analysis/AnalysisSummary.hpp>
#include <mb/framepacing/data/analysis/CapturesCsv.hpp>
#include <mb/framepacing/data/analysis/FramesCsv.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace FP = MB::FramePacing;
namespace FD = MB::FramePacing::Data;
namespace FM = MB::FramePacing::Marker;

namespace
{
  constexpr std::string_view MinimalSummary = R"({
  "scanout": "SingleScanout",
  "analysedUtc": "2026-01-01T00:00:00Z",
  "capturePeriodNs": 16666667,
  "measurementResolutionNs": 16666667,
  "errorThresholdNs": 1000000,
  "runs": []
})";

  //! The minimal summary as it was before the nanoseconds: its times in ticks of 100 ns, under names that end in Ticks.
  constexpr std::string_view TickSummary = R"({
  "scanout": "SingleScanout",
  "analysedUtc": "2026-01-01T00:00:00Z",
  "capturePeriodTicks": 166667,
  "measurementResolutionTicks": 166667,
  "errorThresholdTicks": 10000,
  "runs": []
})";

  constexpr std::string_view Counts = R"("counts": { "captures": 10, "decoded": 9, "undecodable": 1, "torn": 0, "notRecorded": 0,
    "sourceDroppedFrames": 0, "missedCaptures": 0, "presentedFrames": 8, "skippedFrameIndices": 0, "droppedFrames": 0,
    "outOfOrderCaptures": 0, "segments": 1 })";

  constexpr std::string_view Values = R"({ "count": 3, "min": -1.5, "mean": 0.25, "stdDev": 1.25, "p50": 0, "p95": 2, "p99": 2.5, "max": 3 })";

  //! The statistics every run has; one of the six value statistics can be left out, and all of them given other values.
  std::string Statistics(const std::string_view without = {}, const std::string_view values = Values)
  {
    std::string text = "\"statistics\": { ";
    for (const std::string_view name :
         {"displayDeltaMs", "animationDeltaMs", "animationErrorMs", "absoluteAnimationErrorMs", "driftMs", "onScreenMs"})
    {
      if (name != without)
      {
        text += "\"" + std::string(name) + "\": " + std::string(values) + ", ";
      }
    }
    return text + R"("framesWithAnimationError": 2, "errorPerFrameMs": 0.5, "percentError": 25, "excludedStaticFrames": 0, "uncertainSteps": 0 })";
  }

  constexpr std::string_view RunStart = R"("runId": 7, "hasStartMarker": true, "hasEndMarker": false, "framesFile": "run-7-frames.csv")";

  //! A summary whose one run has the given members.
  std::string SummaryWithRun(const std::string& members)
  {
    return R"({ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000, "runs": [ { )" + members + " } ] }";
  }

  std::string OneRun(const std::string_view start, const std::string_view counts, const std::string_view statistics, const std::string_view more = {})
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

  std::string OneRun(const std::string_view start = RunStart)
  {
    return OneRun(start, Counts, Statistics());
  }

  //! The minimal summary with one more member in front.
  std::string MinimalWith(const std::string& member)
  {
    return "{ " + member + "," + std::string(MinimalSummary.substr(1));
  }

  //! text with the first old replaced by replacement (which must be there).
  std::string Replaced(const std::string_view text, const std::string_view old, const std::string_view replacement)
  {
    std::string result(text);
    const std::size_t at = result.find(old);
    EXPECT_NE(at, std::string::npos) << old;
    return at == std::string::npos ? result : result.replace(at, old.size(), replacement);
  }

  constexpr std::string_view FrameColumns =
    "segment,frameIndex,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,driftNs,flags";

  //! The same columns as a frames CSV named them before the nanoseconds.
  constexpr std::string_view TickFrameColumns =
    "segment,frameIndex,animationTicks,firstCaptureIndex,firstSeenTicks,onScreenTicks,captures,skippedBefore,driftTicks,flags";

  constexpr std::string_view CaptureColumns =
    "captureIndex,captureNs,status,kind,runId,frameIndex,animationNs,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,hostNs,"
    "deviceNs,payloadHex";

  //! The same columns as captures.csv named them before the nanoseconds.
  constexpr std::string_view TickCaptureColumns =
    "captureIndex,captureTicks,status,kind,runId,frameIndex,animationTicks,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,hostTicks,"
    "deviceTicks,payloadHex";

  //! A file with the content given, in the temporary folder; removed again when it goes out of scope.
  class TemporaryFile
  {
    std::filesystem::path m_path;

  public:
    TemporaryFile(const std::string& name, const std::string& content)
      : m_path(std::filesystem::temp_directory_path() / ("mb_framepacing_data_test-" + name))
    {
      std::ofstream out(m_path, std::ios::binary);
      out << content;
    }

    TemporaryFile(const TemporaryFile&) = delete;
    TemporaryFile& operator=(const TemporaryFile&) = delete;

    ~TemporaryFile()
    {
      std::error_code ignored;
      std::filesystem::remove(m_path, ignored);
    }

    [[nodiscard]] const std::filesystem::path& Path() const noexcept
    {
      return m_path;
    }
  };

  std::vector<FD::FrameRow> ReadFrames(const std::string& content)
  {
    const TemporaryFile file("frames.csv", content);
    return FD::ReadFrames(file.Path());
  }

  std::vector<FD::CaptureCsvRow> ReadCaptures(const std::string& content)
  {
    const TemporaryFile file("captures.csv", content);
    return FD::ReadCaptures(file.Path());
  }
}

TEST(AnalysisOutput, ASummaryWithoutAFormatVersionIsFormatOne)
{
  const auto summary = FD::ParseSummary(MinimalSummary);
  EXPECT_EQ(summary.FormatVersion, 1);
  EXPECT_TRUE(summary.Runs.empty());
  EXPECT_TRUE(summary.Markers.empty());
  EXPECT_EQ(summary.Scanout, "SingleScanout");
}

TEST(AnalysisOutput, ANewerFormatIsRefused)
{
  try
  {
    (void)FD::ParseSummary(MinimalWith(R"("formatVersion": 2)"));
    FAIL() << "a newer format was read";
  }
  catch (const FD::DataFormatError& error)
  {
    EXPECT_NE(std::string(error.what()).find("update"), std::string::npos);
  }
}

TEST(AnalysisOutput, FormatVersionZeroIsFormatOne)
{
  EXPECT_EQ(FD::ParseSummary(MinimalWith(R"("formatVersion": 0)")).FormatVersion, 1) << "as a file without it (C# reads both as 0)";
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("formatVersion": -1)")), FD::DataFormatError);
}

TEST(AnalysisOutput, ASummarysTimesAreWholeNanoseconds)
{
  const auto summary = FD::ParseSummary(MinimalSummary);
  EXPECT_EQ(summary.CapturePeriod, FP::NanosecondTimeSpan(16'666'667));
  EXPECT_EQ(summary.MeasurementResolution, FP::NanosecondTimeSpan(16'666'667));
  EXPECT_EQ(summary.ErrorThreshold, FP::NanosecondTimeSpan(1'000'000));
  EXPECT_EQ(FD::ParseSummary(R"({ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000 })").MeasurementResolution,
            FP::NanosecondTimeSpan(16'666'667))
    << "a file without it: the capture period";
  constexpr std::string_view Resolution = R"("measurementResolutionNs": 16666667)";
  EXPECT_EQ(FD::ParseSummary(Replaced(MinimalSummary, Resolution, R"("measurementResolutionNs": 0)")).MeasurementResolution,
            FP::NanosecondTimeSpan(16'666'667))
    << "0, as C# reads a file without it";
  EXPECT_EQ(FD::ParseSummary(Replaced(MinimalSummary, Resolution, R"("measurementResolutionNs": 5)")).MeasurementResolution,
            FP::NanosecondTimeSpan(5));
  EXPECT_EQ(FD::ParseSummary(Replaced(MinimalSummary, "16666667,", "9223372036854775807,")).CapturePeriod, FP::NanosecondTimeSpan::MaxValue())
    << "the 64 bits a time has";

  // Never a fraction, a text or another unit's field
  constexpr std::string_view Threshold = R"("errorThresholdNs": 1000000)";
  EXPECT_THROW((void)FD::ParseSummary(Replaced(MinimalSummary, "16666667,", "16666667.5,")), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(Replaced(MinimalSummary, "16666667,", "16666667.0,")), FD::DataFormatError) << "a fraction of nothing";
  EXPECT_THROW((void)FD::ParseSummary(Replaced(MinimalSummary, "16666667,", "9223372036854775808,")), FD::DataFormatError) << "beyond 64 bits";
  EXPECT_THROW((void)FD::ParseSummary(Replaced(MinimalSummary, Threshold, R"("errorThresholdNs": "1000000")")), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(Replaced(MinimalSummary, Threshold, R"("errorThresholdNs": 1e30)")), FD::DataFormatError);
  try
  {
    (void)FD::ParseSummary(Replaced(MinimalSummary, "capturePeriodNs", "capturePeriodMs"));
    FAIL() << "a summary in milliseconds was read";
  }
  catch (const FD::DataFormatError& error)
  {
    EXPECT_NE(std::string(error.what()).find("capturePeriodNs"), std::string::npos) << error.what();
  }
}

TEST(AnalysisOutput, ASummaryFromBeforeTheNanosecondsIsRefused)
{
  // Its times are ticks of 100 ns under "...Ticks" names: read as nanoseconds they would be a hundred times too small, so the required
  // "...Ns" fields it lacks refuse it
  try
  {
    (void)FD::ParseSummary(TickSummary);
    FAIL() << "a summary in ticks was read";
  }
  catch (const FD::DataFormatError& error)
  {
    EXPECT_NE(std::string(error.what()).find("capturePeriodNs"), std::string::npos) << error.what();
  }
  // One name at a time: each required time is refused by its own name
  EXPECT_THROW((void)FD::ParseSummary(Replaced(MinimalSummary, "capturePeriodNs", "capturePeriodTicks")), FD::DataFormatError);
  try
  {
    (void)FD::ParseSummary(Replaced(MinimalSummary, "errorThresholdNs", "errorThresholdTicks"));
    FAIL() << "an error threshold in ticks was read";
  }
  catch (const FD::DataFormatError& error)
  {
    EXPECT_NE(std::string(error.what()).find("errorThresholdNs"), std::string::npos) << error.what();
  }
  // The optional one is not read under its old name: the capture period, as in a file without it
  constexpr std::string_view Resolution = R"("measurementResolutionNs": 16666667)";
  EXPECT_EQ(FD::ParseSummary(Replaced(MinimalSummary, Resolution, R"("measurementResolutionTicks": 5)")).MeasurementResolution,
            FP::NanosecondTimeSpan(16'666'667));

  // A run's pacing from then
  constexpr std::string_view Pacing = R"("pacing": { "refreshPeriodNs": 16666667, "refreshCalculated": false, "targetFrameNs": 33333334,
    "source": "TargetFrameTime", "lateFrames": 1, "lateShare": 0.125, "worstLateShare": 0.5, "errorFramesWithUnevenDisplay": 1,
    "errorFramesWithEvenDisplay": 1, "verdict": "Both" })";
  const auto parse = [](const std::string& pacing) { return FD::ParseSummary(OneRun(RunStart, Counts, Statistics(), pacing)); };
  EXPECT_EQ(parse(std::string(Pacing)).Runs.size(), 1u) << "the pacing these cases change one name of";
  EXPECT_THROW((void)parse(Replaced(Pacing, R"("refreshPeriodNs": 16666667)", R"("refreshPeriodTicks": 166667)")), FD::DataFormatError);
  EXPECT_THROW((void)parse(Replaced(Pacing, R"("targetFrameNs": 33333334)", R"("targetFrameTicks": 333334)")), FD::DataFormatError);
}

TEST(AnalysisOutput, ARunIsReadWithItsCountsStatisticsAndPacing)
{
  constexpr std::string_view Pacing = R"("pacing": { "refreshPeriodNs": 16666667, "refreshCalculated": false, "targetFrameNs": 33333334,
    "source": "TargetFrameTime", "lateFrames": 1, "lateShare": 0.125, "worstLateShare": 0.5, "errorFramesWithUnevenDisplay": 1,
    "errorFramesWithEvenDisplay": 1, "verdict": "Both", "refreshHz": 59.9999988 })";
  const auto summary = FD::ParseSummary(OneRun(RunStart, Counts, Statistics(), Pacing));
  ASSERT_EQ(summary.Runs.size(), 1u);
  const FD::SummaryRun& run = summary.Runs[0];
  EXPECT_EQ(run.RunId, 7u);
  EXPECT_EQ(run.FramesFile, "run-7-frames.csv");
  EXPECT_EQ(run.Counts.Captures, 10);
  EXPECT_EQ(run.Counts.PresentedFrames, 8);
  EXPECT_EQ(run.Statistics.FramesWithAnimationError, 2);
  EXPECT_EQ(run.Statistics.PercentError, 25.0);
  EXPECT_EQ(run.Statistics.DriftMs.Count, 3);
  EXPECT_EQ(run.Statistics.DriftMs.Min, -1.5);
  EXPECT_EQ(run.Statistics.DriftMs.Max, 3.0);
  EXPECT_EQ(run.Statistics.DriftMs.P999, 0.0) << "0 in a file without it";
  EXPECT_EQ(run.Statistics.CpuBusyMs.Count, 0) << "an optional one the file lacks is empty";
  // An if the optional-access check follows (it does not see through ASSERT_TRUE)
  if (!run.Pacing.has_value())
  {
    FAIL() << "no pacing";
  }
  EXPECT_EQ(run.Pacing->RefreshPeriod, FP::NanosecondTimeSpan(16'666'667));
  EXPECT_EQ(run.Pacing->TargetFrameTime, FP::NanosecondTimeSpan(33'333'334));
  EXPECT_EQ(run.Pacing->Source, "TargetFrameTime");
  EXPECT_FALSE(run.Histograms.has_value());

  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics(), Replaced(Pacing, "33333334", "33333334.5"))), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics(), Replaced(Pacing, "refreshPeriodNs", "refreshPeriodMs"))),
               FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics(), R"("pacing": 1)")), FD::DataFormatError) << "pacing that is a number";
}

TEST(AnalysisOutput, ARunWithoutWhatEveryRunHasIsNotASummary)
{
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, {}, Statistics())), FD::DataFormatError) << "no counts";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, {})), FD::DataFormatError) << "no statistics";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, R"("counts": 3)", Statistics())), FD::DataFormatError) << "counts that are a number";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, R"("statistics": [])")), FD::DataFormatError) << "statistics that are an array";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics("driftMs"))), FD::DataFormatError) << "no drift statistics";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics({}, "7"))), FD::DataFormatError) << "statistics that are a number";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Statistics({}, Replaced(Values, R"("p50": 0, )", "")))), FD::DataFormatError)
    << "statistics without their median";
}

TEST(AnalysisOutput, HistogramsAndTheCameraAreReadWhole)
{
  constexpr std::string_view Histogram = R"({ "binWidthMs": 0.1, "total": 2, "bins": [ { "centerMs": 0.1, "count": 2 } ] })";
  const auto histograms = [](const std::string_view error, const std::string_view display)
  { return R"("histograms": { "animationErrorMs": )" + std::string(error) + R"(, "displayDeltaMs": )" + std::string(display) + " }"; };
  const auto parse = [](const std::string& more) { return FD::ParseSummary(OneRun(RunStart, Counts, Statistics(), more)); };

  const auto run = parse(histograms(Histogram, Histogram)).Runs[0];
  if (!run.Histograms.has_value())
  {
    FAIL() << "no histograms";
  }
  EXPECT_EQ(run.Histograms->AnimationErrorMs.BinWidthMs, 0.1);
  EXPECT_EQ(run.Histograms->DisplayDeltaMs.Total, 2);
  ASSERT_EQ(run.Histograms->AnimationErrorMs.Bins.size(), 1u);
  EXPECT_EQ(run.Histograms->AnimationErrorMs.Bins[0].CenterMs, 0.1);
  EXPECT_EQ(run.Histograms->AnimationErrorMs.Bins[0].Count, 2);
  EXPECT_THROW((void)parse(R"("histograms": {})"), FD::DataFormatError) << "empty histograms";
  EXPECT_THROW((void)parse(R"("histograms": { "animationErrorMs": 1, "displayDeltaMs": 2 })"), FD::DataFormatError) << "histograms that are numbers";
  EXPECT_THROW((void)parse(histograms(Histogram, R"({ "binWidthMs": 0.1, "total": 2 })")), FD::DataFormatError) << "a histogram without bins";
  EXPECT_THROW((void)parse(histograms(Histogram, R"({ "binWidthMs": 0.1, "total": 2, "bins": 3 })")), FD::DataFormatError)
    << "bins that are a number";
  EXPECT_THROW((void)parse(histograms(Histogram, R"({ "binWidthMs": 0.1, "total": 2, "bins": [ 5 ] })")), FD::DataFormatError)
    << "bins that are numbers";

  constexpr std::string_view Delay = R"("scanoutDelay": { "count": 1, "min": 0, "mean": 0, "stdDev": 0, "p50": 0, "p95": 0, "p99": 0, "max": 0 }, )";
  constexpr std::string_view Zones = R"("framesSeenInBothZones": 4, "tornFrames": 1, "secondZoneOnlyFrames": 0 })";
  const auto camera = parse(R"("camera": { )" + std::string(Delay) + std::string(Zones)).Runs[0].Camera;
  if (!camera.has_value())
  {
    FAIL() << "no camera";
  }
  EXPECT_EQ(camera->FramesSeenInBothZones, 4);
  EXPECT_EQ(camera->TornFrames, 1);
  EXPECT_EQ(camera->ScanoutDelay.Count, 1);
  EXPECT_THROW((void)parse(R"("camera": { )" + std::string(Zones)), FD::DataFormatError) << "a camera without its scanout delay";
  EXPECT_THROW((void)parse(R"("camera": 1)"), FD::DataFormatError);
}

TEST(AnalysisOutput, ListsMustBeLists)
{
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000, "runs": { "a": 1 } })"), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000, "runs": [ 5 ] })"), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("markers": { "bounds": "0,0,1,1", "moduleSizePx": 3 })")), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("markers": [ 3 ])")), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("warnings": "one text")")), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("warnings": [ 1 ])")), FD::DataFormatError);
  EXPECT_EQ(FD::ParseSummary(MinimalWith(R"("warnings": [ "one", "two" ])")).Warnings, (std::vector<std::string>{"one", "two"}));
  const auto markers = FD::ParseSummary(MinimalWith(R"("markers": [ { "bounds": "0,0,1,1", "moduleSizePx": 3 } ])")).Markers;
  ASSERT_EQ(markers.size(), 1u);
  EXPECT_EQ(markers[0].Bounds, "0,0,1,1");
  EXPECT_EQ(markers[0].ModuleSizePx, 3.0);
  EXPECT_EQ(FD::ParseSummary(MinimalWith(R"("capture": { "width": 1280 })")).CaptureJson, R"({"width":1280})");
  EXPECT_EQ(FD::ParseSummary(OneRun(RunStart, Counts, Statistics(), R"("warnings": [ "a run's" ])")).Runs[0].Warnings,
            (std::vector<std::string>{"a run's"}));
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
  constexpr std::string_view Frames = R"("framesWithAnimationError": 2)";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Replaced(Statistics(), Frames, R"("framesWithAnimationError": 1e300)"))),
               FD::DataFormatError)
    << "a count beyond 64 bits";
  EXPECT_THROW(
    (void)FD::ParseSummary(OneRun(RunStart, Counts, Replaced(Statistics(), Frames, R"("framesWithAnimationError": -9223372036854775809)"))),
    FD::DataFormatError)
    << "a count below 64 bits";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(RunStart, Counts, Replaced(Statistics(), R"("errorPerFrameMs": 0.5)", R"("errorPerFrameMs": "0.5")"))),
               FD::DataFormatError)
    << "a text for a number";
  EXPECT_THROW((void)FD::ParseSummary(MinimalWith(R"("formatVersion": 4294967297)")), FD::DataFormatError) << "not format 1 by wrapping";
  const auto statistics =
    FD::ParseSummary(OneRun(RunStart, Counts, Replaced(Statistics(), R"("percentError": 25)", R"("percentError": 25.5)"))).Runs[0].Statistics;
  EXPECT_EQ(statistics.PercentError, 25.5);
}

TEST(AnalysisOutput, RequiredFieldsAreRequired)
{
  EXPECT_THROW((void)FD::ParseSummary(R"({ "errorThresholdNs": 1000000 })"), FD::DataFormatError) << "no capture period";
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodNs": 16666667 })"), FD::DataFormatError) << "no error threshold";
  EXPECT_THROW((void)FD::ParseSummary(R"({ "capturePeriodNs": null, "errorThresholdNs": 1000000 })"), FD::DataFormatError) << "null is absent";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(R"("hasStartMarker": true, "hasEndMarker": false, "framesFile": "f")")), FD::DataFormatError)
    << "no run id";
  EXPECT_THROW((void)FD::ParseSummary(OneRun(R"("runId": 7, "hasStartMarker": true, "hasEndMarker": false)")), FD::DataFormatError)
    << "no frames file";
  EXPECT_THROW((void)FD::ParseSummary("[]"), FD::DataFormatError) << "not an object";
  EXPECT_THROW((void)FD::ParseSummary("{ \"capturePeriodNs\": "), FD::DataFormatError) << "not JSON";
  EXPECT_THROW((void)FD::ParseSummary(""), FD::DataFormatError) << "empty";
  EXPECT_THROW((void)FD::ParseSummary("null"), FD::DataFormatError);
  EXPECT_THROW((void)FD::ParseSummary(Replaced(MinimalSummary, "capturePeriodNs", "CapturePeriodNs")), FD::DataFormatError)
    << "names are case sensitive";
}

TEST(AnalysisOutput, AFileIsReadOrSaidToBeMissing)
{
  const TemporaryFile file("summary.json", std::string(MinimalSummary));
  EXPECT_EQ(FD::ReadSummary(file.Path()).CapturePeriod, FP::NanosecondTimeSpan(16'666'667));
  const auto missing = std::filesystem::temp_directory_path() / "mb_framepacing_data_test-no-such-folder" / "summary.json";
  EXPECT_THROW((void)FD::ReadFrames(missing), std::runtime_error);
  EXPECT_THROW((void)FD::ReadCaptures(missing), std::runtime_error);
  try
  {
    (void)FD::ReadSummary(missing);
    FAIL() << "a missing file was read";
  }
  catch (const FD::DataFormatError&)
  {
    FAIL() << "a file that cannot be opened is not a format error";
  }
  catch (const std::runtime_error& error)
  {
    EXPECT_NE(std::string(error.what()).find("summary.json"), std::string::npos);
  }
}

TEST(AnalysisOutput, FileNamesFollowTheRunIds)
{
  EXPECT_EQ(FD::FramesFileName(3), "run-3-frames.csv");
  EXPECT_EQ(FD::FramesFileName(3, 1), "run-3-2-frames.csv");
  EXPECT_EQ(FD::RunFilePrefix(3), "run-3");
  EXPECT_EQ(FD::RunFilePrefix(4294967295u, 2), "run-4294967295-3");
}

TEST(AnalysisOutput, TheAnalysisIsFoundInTheFolderOrItsAnalysisFolder)
{
  const auto folder = std::filesystem::temp_directory_path() / "mb_framepacing_data_test-find";
  std::filesystem::remove_all(folder);
  std::filesystem::create_directories(folder / FD::AnalysisDirectoryName);
  EXPECT_FALSE(FD::FindAnalysis(folder).has_value()) << "no summary.json anywhere";
  EXPECT_FALSE(FD::FindAnalysis(folder / "no-such-folder").has_value());
  {
    std::ofstream out(folder / FD::AnalysisDirectoryName / FD::SummaryFileName);
    out << MinimalSummary;
  }
  EXPECT_EQ(FD::FindAnalysis(folder), folder / FD::AnalysisDirectoryName) << "a capture folder";
  EXPECT_EQ(FD::FindAnalysis(folder / FD::AnalysisDirectoryName), folder / FD::AnalysisDirectoryName) << "the analysis folder itself";
  {
    std::ofstream out(folder / FD::SummaryFileName);
    out << MinimalSummary;
  }
  EXPECT_EQ(FD::FindAnalysis(folder), folder) << "the folder's own summary wins";
  std::filesystem::remove_all(folder);
}

TEST(AnalysisOutput, FramesAreReadByColumnNameWhateverTheOrder)
{
  const auto rows = ReadFrames(
    "frameIndex,newColumn,segment,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,driftNs,flags,"
    "cpuBusyNs\r\n"
    "7,x,0,116666669,3,50000000,33333334,2,1,-500000,SkippedBefore|Late,\r\n"
    "\r\n"
    "8,x,1,0,4,0,1,1,0,0,,5\n");
  ASSERT_EQ(rows.size(), 2u);
  EXPECT_EQ(rows[0].FrameIndex, 7u);
  EXPECT_EQ(rows[0].Segment, 0);
  EXPECT_EQ(rows[0].AnimationTime, FP::NanosecondTimeSpan(116'666'669));
  EXPECT_EQ(rows[0].FirstSeenTime, FP::NanosecondTickCount(50'000'000));
  EXPECT_EQ(rows[0].OnScreen, FP::NanosecondTimeSpan(33'333'334));
  EXPECT_EQ(rows[0].Drift, FP::NanosecondTimeSpan(-500'000));
  EXPECT_EQ(rows[0].Flags, (std::vector<std::string>{"SkippedBefore", "Late"}));
  EXPECT_FALSE(rows[0].CpuBusy.has_value()) << "an empty cell";
  EXPECT_FALSE(rows[0].LastSeenTime.has_value()) << "a column the file lacks";
  EXPECT_TRUE(rows[0].OlderFrames.empty());
  EXPECT_EQ(rows[1].Segment, 1) << "an empty line is skipped, a line may end with \\n";
  EXPECT_TRUE(rows[1].Flags.empty());
  EXPECT_EQ(rows[1].CpuBusy, FP::NanosecondTimeDuration::FromNanoseconds(5));
}

TEST(AnalysisOutput, EveryTimeIsReadAsTheNanosecondsItIs)
{
  // The values a marker can carry at their limits: nothing between the file and the type converts them
  const auto rows = ReadFrames(
    "segment,frameIndex,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,displayDeltaNs,"
    "animationDeltaNs,animationErrorNs,driftNs,flags,intendedDisplayNs,markerTargetNs,targetNs,markerPreferredNs,"
    "preferredNs,pacingErrorNs,predictionErrorNs,latenessNs,lastSeenNs,cpuStartNs,cpuBusyNs,frameTimeNs,cpuWaitNs,"
    "olderFrames,mainMarkerFirstSeenNs,scanoutDelayNs\n"
    "2,18446744073709551615,-9223372036854775808,5,9223372036854775807,16666667,1,0,16666666,9223372036854775807,-1,-7,Late,"
    "-9223372036854775808,4294967295,33333334,1,16666667,3,-3,8333333,1234567890123456789,-1234567890123456789,12006000,16666800,4660800,"
    "41@1234567890123456790|40@-5,9007199254740993,-9007199254740993\n");
  ASSERT_EQ(rows.size(), 1u);
  const FD::FrameRow& row = rows[0];
  EXPECT_EQ(row.Segment, 2);
  EXPECT_EQ(row.FrameIndex, std::numeric_limits<uint64_t>::max());
  EXPECT_EQ(row.AnimationTime, FP::NanosecondTimeSpan::MinValue());
  EXPECT_EQ(row.FirstSeenTime, FP::NanosecondTickCount(std::numeric_limits<int64_t>::max()));
  EXPECT_EQ(row.OnScreen, FP::NanosecondTimeSpan(16'666'667));
  EXPECT_EQ(row.DisplayDelta, FP::NanosecondTimeSpan(16'666'666));
  EXPECT_EQ(row.AnimationDelta, FP::NanosecondTimeSpan::MaxValue());
  EXPECT_EQ(row.AnimationError, FP::NanosecondTimeSpan(-1));
  EXPECT_EQ(row.Drift, FP::NanosecondTimeSpan(-7));
  EXPECT_EQ(row.IntendedDisplayTime, FP::NanosecondTickCount(std::numeric_limits<int64_t>::min()));
  EXPECT_EQ(row.MarkerTargetFrameTime, FM::Payload::OnDemandFrameTime) << "on demand, as the marker library names it";
  EXPECT_EQ(row.TargetFrameTime, FP::NanosecondTimeSpan(33'333'334));
  EXPECT_EQ(row.MarkerPreferredFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(1));
  EXPECT_EQ(row.PreferredFrameTime, FP::NanosecondTimeSpan(16'666'667));
  EXPECT_EQ(row.PacingError, FP::NanosecondTimeSpan(3));
  EXPECT_EQ(row.PredictionError, FP::NanosecondTimeSpan(-3));
  EXPECT_EQ(row.Lateness, FP::NanosecondTimeSpan(8'333'333));
  EXPECT_EQ(row.LastSeenTime, FP::NanosecondTickCount(1'234'567'890'123'456'789));
  EXPECT_EQ(row.CpuStartTime, FP::NanosecondTickCount(-1'234'567'890'123'456'789));
  EXPECT_EQ(row.CpuBusy, FP::NanosecondTimeDuration::FromNanoseconds(12'006'000));
  EXPECT_EQ(row.FrameTime, FP::NanosecondTimeSpan(16'666'800));
  EXPECT_EQ(row.CpuWait, FP::NanosecondTimeSpan(4'660'800));
  ASSERT_EQ(row.OlderFrames.size(), 2u);
  EXPECT_EQ(row.OlderFrames[0].FrameIndex, 41u);
  EXPECT_EQ(row.OlderFrames[0].CaptureTime, FP::NanosecondTickCount(1'234'567'890'123'456'790)) << "beyond what a double holds exactly";
  EXPECT_EQ(row.OlderFrames[1].FrameIndex, 40u);
  EXPECT_EQ(row.OlderFrames[1].CaptureTime, FP::NanosecondTickCount(-5));
  EXPECT_EQ(row.MainMarkerFirstSeenTime, FP::NanosecondTickCount(9'007'199'254'740'993)) << "2^53 + 1";
  EXPECT_EQ(row.ScanoutDelay, FP::NanosecondTimeSpan(-9'007'199'254'740'993));
}

TEST(AnalysisOutput, ATimeThatIsNotWholeNanosecondsIsRefused)
{
  for (const std::string_view cell :
       {"16666666.7", "16666667.0", "1e3", " 5", "5 ", "+5", "1_000", "0x10", "NaN", "9223372036854775808", "-9223372036854775809", ""})
  {
    EXPECT_THROW((void)ReadFrames(std::string(FrameColumns) + "\n0,1," + std::string(cell) + ",0,0,16666667,1,0,0,\n"), FD::DataFormatError)
      << "'" << cell << "'";
  }
  EXPECT_EQ(ReadFrames(std::string(FrameColumns) + "\n0,1,-0,0,0,16666667,1,0,0,\n").at(0).AnimationTime, FP::NanosecondTimeSpan(0));
  EXPECT_EQ(ReadFrames(std::string(FrameColumns) + "\n0,1,007,0,0,16666667,1,0,0,\n").at(0).AnimationTime, FP::NanosecondTimeSpan(7));
}

TEST(AnalysisOutput, TheMarkersDurationsMustFit32Bits)
{
  // 32 bits unsigned in the marker and in the file, whatever the 64 bits of the type they are read into
  const auto read = [](const std::string_view column, const std::string_view value)
  { return ReadFrames(std::string(FrameColumns) + "," + std::string(column) + "\n0,1,0,0,0,16666667,1,0,0,," + std::string(value) + "\n").at(0); };
  EXPECT_EQ(read("cpuBusyNs", "4294967295").CpuBusy, FM::Payload::MaxCpuBusy) << "the largest: 4.29 s";
  EXPECT_EQ(read("cpuBusyNs", "8000000").CpuBusy, FP::NanosecondTimeDuration::FromNanoseconds(8'000'000));
  EXPECT_EQ(read("cpuBusyNs", "0").CpuBusy, FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(read("markerTargetNs", "4294967295").MarkerTargetFrameTime, FM::Payload::OnDemandFrameTime) << "on demand";
  EXPECT_EQ(read("markerTargetNs", "4294967294").MarkerTargetFrameTime, FM::Payload::MaxFrameTime) << "the longest frame time";
  EXPECT_EQ(read("markerPreferredNs", "4294967295").MarkerPreferredFrameTime, FM::Payload::OnDemandFrameTime) << "on demand";
  EXPECT_EQ(read("markerPreferredNs", "16666667").MarkerPreferredFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(16'666'667));
  EXPECT_FALSE(read("markerPreferredNs", "").MarkerPreferredFrameTime.has_value()) << "an empty cell";
  for (const std::string_view column : {"cpuBusyNs", "markerTargetNs", "markerPreferredNs"})
  {
    EXPECT_THROW((void)read(column, "4294967296"), FD::DataFormatError) << column << ": one beyond 32 bits";
    EXPECT_THROW((void)read(column, "9223372036854775807"), FD::DataFormatError) << column << ": what the type itself would hold";
    EXPECT_THROW((void)read(column, "-1"), FD::DataFormatError) << column << ": a duration is never negative";
    EXPECT_THROW((void)read(column, "1.5"), FD::DataFormatError) << column;
  }
}

TEST(AnalysisOutput, AFramesCsvFromBeforeTheNanosecondsIsRefused)
{
  // Its times are ticks of 100 ns in "...Ticks" columns: the required "...Ns" columns it lacks refuse its first row
  try
  {
    (void)ReadFrames(std::string(TickFrameColumns) + "\n0,1,166667,0,0,166667,1,0,0,\n");
    FAIL() << "a frames CSV in ticks was read";
  }
  catch (const FD::DataFormatError& error)
  {
    EXPECT_NE(std::string(error.what()).find("frames.csv' line 2"), std::string::npos) << error.what();
  }
  EXPECT_EQ(ReadFrames(std::string(FrameColumns) + "\n0,1,16666667,0,0,16666667,1,0,0,\n").size(), 1u) << "the same row in nanoseconds";
  // One required column at a time under its old name
  for (const std::string_view column : {"animationNs", "firstSeenNs", "onScreenNs", "driftNs"})
  {
    const std::string ticks = std::string(column.substr(0, column.size() - 2)) + "Ticks";
    EXPECT_THROW((void)ReadFrames(Replaced(FrameColumns, column, ticks) + "\n0,1,16666667,0,0,16666667,1,0,0,\n"), FD::DataFormatError) << column;
  }
  // An optional column under its old name is a column the reader does not know: empty
  const auto rows = ReadFrames(std::string(FrameColumns) + ",displayDeltaTicks,cpuBusyTicks\n0,1,16666667,0,0,16666667,1,0,0,,166667,80000\n");
  ASSERT_EQ(rows.size(), 1u);
  EXPECT_FALSE(rows[0].DisplayDelta.has_value());
  EXPECT_FALSE(rows[0].CpuBusy.has_value());
}

TEST(AnalysisOutput, OtherContentThatIsNotAFrameIsRefused)
{
  const auto read = [](const std::string_view header, const std::string& line) { return ReadFrames(std::string(header) + "\n" + line + "\n"); };
  EXPECT_THROW((void)ReadFrames(""), FD::DataFormatError) << "an empty file";
  EXPECT_TRUE(ReadFrames(std::string(FrameColumns) + "\n\n").empty()) << "a header and an empty line";
  EXPECT_THROW((void)read(FrameColumns, "x,1,0,0,0,16666667,1,0,0,"), FD::DataFormatError) << "a segment that is no number";
  EXPECT_THROW((void)read(FrameColumns, "0,-1,0,0,0,16666667,1,0,0,"), FD::DataFormatError) << "a negative frame index";
  EXPECT_THROW((void)read(FrameColumns, "0,1,0,0,0,16666667,2147483648,0,0,"), FD::DataFormatError) << "captures beyond 32 bits";
  EXPECT_THROW((void)read("frameIndex,animationNs", "1,0"), FD::DataFormatError) << "a required column the file lacks";
  const std::string withOlder = std::string(FrameColumns) + ",olderFrames";
  for (const std::string_view older : {"41", "@5", "41@", "x@5", "41@5.5", "41@5|", "41@5||42@6", "|41@5"})
  {
    EXPECT_THROW((void)read(withOlder, "0,1,0,0,0,16666667,1,0,0,," + std::string(older)), FD::DataFormatError) << "olderFrames '" << older << "'";
  }
  EXPECT_THROW((void)read(FrameColumns, "0,1,0,0,0,16666667,1,0,0,Late|"), FD::DataFormatError) << "an empty flag";

  // The error names the file and the line
  try
  {
    (void)ReadFrames(std::string(FrameColumns) + "\n0,1,0,0,0,16666667,1,0,0,\n\n0,2,1.5,0,0,16666667,1,0,0,\n");
    FAIL() << "a fraction was read";
  }
  catch (const FD::DataFormatError& error)
  {
    const std::string what = error.what();
    EXPECT_NE(what.find("frames.csv' line 4"), std::string::npos) << what;
    EXPECT_NE(what.find("'1.5'"), std::string::npos) << what;
  }
}

TEST(AnalysisOutput, CapturesCarrySourceDropsMissedRefreshesAndTheSyncMarker)
{
  const auto rows = ReadCaptures(std::string(CaptureColumns) + "\r\n4,66666668,Torn,Frame,7,12,200000004,3,1,7,11,70100000,66666668,4D46\r\n" +
                                 "5,,NotRecorded,,,,,0,0,,,,,\r\n");
  ASSERT_EQ(rows.size(), 2u);
  EXPECT_EQ(rows[0].CaptureIndex, 4);
  EXPECT_EQ(rows[0].CaptureTime, FP::NanosecondTickCount(66'666'668));
  EXPECT_EQ(rows[0].CaptureStatus, "Torn");
  EXPECT_EQ(rows[0].Kind, "Frame");
  EXPECT_EQ(rows[0].RunId, 7u);
  EXPECT_EQ(rows[0].FrameIndex, 12u);
  EXPECT_EQ(rows[0].AnimationTime, FP::NanosecondTimeSpan(200'000'004));
  EXPECT_EQ(rows[0].SyncRunId, 7u);
  EXPECT_EQ(rows[0].SyncFrameIndex, 11u);
  EXPECT_EQ(rows[0].SourceDropsBefore, 3);
  EXPECT_EQ(rows[0].MissedBefore, 1);
  EXPECT_EQ(rows[0].HostTime, FP::NanosecondTickCount(70'100'000));
  EXPECT_EQ(rows[0].DeviceTime, FP::NanosecondTickCount(66'666'668));
  EXPECT_EQ(rows[0].Payload, (std::vector<uint8_t>{0x4D, 0x46}));
  EXPECT_FALSE(rows[1].CaptureTime.has_value()) << "a capture the recorder dropped";
  EXPECT_FALSE(rows[1].Kind.has_value());
  EXPECT_FALSE(rows[1].SyncFrameIndex.has_value());
  EXPECT_TRUE(rows[1].Payload.empty());
}

TEST(AnalysisOutput, ACapturesCsvFromBeforeTheNanosecondsReadsAsCapturesWithoutTimes)
{
  // Only the capture index is required, so the file reads; its times, in ticks of 100 ns in "...Ticks" columns, are columns the reader
  // does not know and never become nanoseconds
  const auto rows = ReadCaptures(std::string(TickCaptureColumns) + "\n4,666667,Torn,Frame,7,12,2000000,3,1,7,11,701000,666667,4D46\n");
  ASSERT_EQ(rows.size(), 1u);
  EXPECT_EQ(rows[0].CaptureIndex, 4);
  EXPECT_EQ(rows[0].CaptureStatus, "Torn");
  EXPECT_EQ(rows[0].FrameIndex, 12u);
  EXPECT_FALSE(rows[0].CaptureTime.has_value());
  EXPECT_FALSE(rows[0].AnimationTime.has_value());
  EXPECT_FALSE(rows[0].HostTime.has_value());
  EXPECT_FALSE(rows[0].DeviceTime.has_value());
}

TEST(AnalysisOutput, ContentThatIsNotACaptureIsRefused)
{
  const auto read = [](const std::string_view line) { return ReadCaptures(std::string(CaptureColumns) + "\n" + std::string(line) + "\n"); };
  EXPECT_EQ(read("4,66666668,Decoded,Frame,4294967295,12,200000004,0,0,4294967295,11,70100000,66666668,4D46").at(0).RunId, 4294967295u);
  EXPECT_THROW((void)read("4,66666668,Decoded,Frame,-1,12,200000004,0,0,,,70100000,66666668,4D46"), FD::DataFormatError) << "a run id below 0";
  EXPECT_THROW((void)read("4,66666668,Decoded,Frame,4294967296,12,200000004,0,0,,,70100000,66666668,4D46"), FD::DataFormatError)
    << "a run id beyond 32 bits";
  EXPECT_THROW((void)read("4,66666668,Torn,Frame,7,12,200000004,0,0,4294967296,11,70100000,66666668,4D46"), FD::DataFormatError)
    << "a sync run id beyond 32 bits";
  EXPECT_THROW((void)read("4,66666668.5,Decoded,Frame,7,12,200000004,0,0,,,70100000,66666668,4D46"), FD::DataFormatError) << "a fraction";
  EXPECT_THROW((void)read("4,66666668,Decoded,Frame,7,12,200000004,0,0,,,70100000,66666668,4D4"), FD::DataFormatError) << "half a byte";
  EXPECT_THROW((void)read("4,66666668,Decoded,Frame,7,12,200000004,0,0,,,70100000,66666668,4D4G"), FD::DataFormatError) << "no hex digit";
  EXPECT_THROW((void)read("4,66666668,Decoded,Frame,7,12,200000004,0,0,,,70100000,66666668,+D46"), FD::DataFormatError) << "a sign in a byte";
  EXPECT_THROW((void)read("x,66666668,Decoded,Frame,7,12,200000004,0,0,,,70100000,66666668,4D46"), FD::DataFormatError) << "no capture index";
  EXPECT_THROW((void)ReadCaptures(""), FD::DataFormatError) << "an empty file";
}
