// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The golden data (test-data/data): this library reads what digest.json says, the same values as the C# and Python libraries. The digest
// is computed as sdk/csharp/data/UnitTest/source/DataDigest.cs does: counts and sums of what a reader reads from every file.
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/data/analysis/AnalysisFiles.hpp>
#include <mb/framepacing/data/analysis/AnalysisSummary.hpp>
#include <mb/framepacing/data/analysis/CapturesCsv.hpp>
#include <mb/framepacing/data/analysis/FrameRow.hpp>
#include <mb/framepacing/data/analysis/FramesCsv.hpp>
#include <mb/framepacing/data/capture/CaptureDataReader.hpp>
#include <mb/framepacing/data/capture/CaptureDataStatus.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <nlohmann/json.hpp>
#include <gtest/gtest.h>
#include <filesystem>
#include <fstream>
#include <functional>
#include <map>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace FD = MB::FramePacing::Data;
using Json = nlohmann::json;

namespace
{
  constexpr const char* Clip = "60-busy-full-rate";

  std::optional<std::filesystem::path> ClipDirectory()
  {
    for (auto folder = std::filesystem::path(MB_FRAMEPACING_DATA_SOURCE_DIR); !folder.empty(); folder = folder.parent_path())
    {
      auto candidate = folder / "test-data" / "data" / Clip;
      if (std::filesystem::is_regular_file(candidate / "digest.json"))
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

  Json Rect(const MB::FramePacing::Rectangle& rect)
  {
    return Json::array({rect.X(), rect.Y(), rect.Width(), rect.Height()});
  }

  Json Counts(const std::vector<std::string>& names)
  {
    std::map<std::string, int64_t> counts;
    for (const auto& name : names)
    {
      ++counts[name];
    }
    Json result = Json::object();
    for (const auto& [name, count] : counts)
    {
      result[name] = count;
    }
    return result;
  }

  std::string StatusName(const FD::CaptureDataStatus status)
  {
    switch (status)
    {
    case FD::CaptureDataStatus::Decoded:
      return "Decoded";
    case FD::CaptureDataStatus::Torn:
      return "Torn";
    case FD::CaptureDataStatus::Undecodable:
      break;
    }
    return "Undecodable";
  }

  Json CaptureData(const std::filesystem::path& path)
  {
    FD::CaptureDataReader reader(path);
    const auto& header = reader.Header();
    const auto records = reader.ReadAll();
    Json markers = Json::array();
    for (const auto& marker : header.Markers)
    {
      markers.push_back({{"bounds", Rect(marker.Bounds)}, {"moduleSizePx", marker.ModuleSizePx}});
    }
    int64_t captureIndexSum = 0;
    int64_t hostTicksSum = 0;
    int64_t deviceTicksCount = 0;
    int64_t deviceTicksSum = 0;
    int64_t sourceDropsSum = 0;
    int64_t mainByteCount = 0;
    int64_t secondByteCount = 0;
    int64_t decodedPayloads = 0;
    int64_t frameIndexSum = 0;
    int64_t animationTicksSum = 0;
    std::vector<std::string> statuses;
    for (const auto& record : records)
    {
      captureIndexSum += record.CaptureIndex;
      hostTicksSum += record.HostTime.Ticks();
      if (record.DeviceTime)
      {
        ++deviceTicksCount;
        deviceTicksSum += record.DeviceTime->Ticks();
      }
      sourceDropsSum += record.SourceDrops;
      statuses.push_back(StatusName(record.Status));
      mainByteCount += static_cast<int64_t>(record.MainBytes.size());
      secondByteCount += static_cast<int64_t>(record.SecondBytes.size());
      MB::FramePacing::Marker::Payload payload;
      if (record.TryDecodeMain(payload))
      {
        ++decodedPayloads;
        frameIndexSum += static_cast<int64_t>(payload.FrameIndex());
        animationTicksSum += payload.AnimationTime().Ticks();
      }
    }
    return {
      {"header",
       {{"width", header.Width},
        {"height", header.Height},
        {"frameRateNumerator", header.FrameRateNumerator},
        {"frameRateDenominator", header.FrameRateDenominator},
        {"sourceWidth", header.SourceWidth},
        {"sourceHeight", header.SourceHeight},
        {"region", Rect(header.Region)},
        {"markers", markers},
        {"framesStored", header.FramesStored},
        {"camera", header.Camera}}},
      {"recordCount", records.size()},
      {"captureIndexSum", captureIndexSum},
      {"hostTicksSum", hostTicksSum},
      {"deviceTicksCount", deviceTicksCount},
      {"deviceTicksSum", deviceTicksSum},
      {"sourceDropsSum", sourceDropsSum},
      {"statusCounts", Counts(statuses)},
      {"mainByteCount", mainByteCount},
      {"secondByteCount", secondByteCount},
      {"decodedMainPayloads", decodedPayloads},
      {"frameIndexSum", frameIndexSum},
      {"animationTicksSum", animationTicksSum},
    };
  }

  Json Optional(const std::optional<std::string>& text)
  {
    return text ? Json(*text) : Json(nullptr);
  }

  Json Summary(const FD::AnalysisSummary& summary)
  {
    Json runs = Json::array();
    for (const auto& run : summary.Runs)
    {
      runs.push_back({
        {"runId", run.RunId},
        {"sequenceId", Optional(run.SequenceId)},
        {"framesFile", run.FramesFile},
        {"hasStartMarker", run.HasStartMarker},
        {"hasEndMarker", run.HasEndMarker},
        {"presentedFrames", run.Counts.PresentedFrames},
        {"droppedFrames", run.Counts.DroppedFrames},
        {"sourceDroppedFrames", run.Counts.SourceDroppedFrames},
        {"missedCaptures", run.Counts.MissedCaptures},
        {"captures", run.Counts.Captures},
        {"displayDeltaCount", run.Statistics.DisplayDeltaMs.Count},
        {"displayDeltaP50", run.Statistics.DisplayDeltaMs.P50},
        {"animationErrorMax", run.Statistics.AnimationErrorMs.Max},
        {"averageFps", run.Statistics.AverageFps},
        {"excludedStaticFrames", run.Statistics.ExcludedStaticFrames},
        {"uncertainSteps", run.Statistics.UncertainSteps},
        {"cpuBusyCount", run.Statistics.CpuBusyMs.Count},
        {"pacingSource", run.Pacing ? Json(run.Pacing->Source) : Json(nullptr)},
        {"lateFrames", run.Pacing ? run.Pacing->LateFrames : 0},
        {"histogramBins", run.Histograms ? run.Histograms->AnimationErrorMs.Bins.size() : 0u},
      });
    }
    return {
      {"formatVersion", summary.FormatVersion},       {"scanout", Optional(summary.Scanout)},
      {"timeSource", Optional(summary.TimeSource)},   {"capturePeriodMs", summary.CapturePeriodMs},
      {"errorThresholdMs", summary.ErrorThresholdMs}, {"markerCount", summary.Markers.size()},
      {"warningCount", summary.Warnings.size()},      {"runs", runs},
    };
  }

  //! A time value's ticks, as the digest sums them.
  template <typename T>
  std::optional<int64_t> Ticks(const T& value)
  {
    return static_cast<int64_t>(value.Ticks());
  }

  template <typename T>
  std::optional<int64_t> Ticks(const std::optional<T>& value)
  {
    return value ? Ticks(*value) : std::nullopt;
  }

  Json Frames(const std::filesystem::path& path)
  {
    const auto rows = FD::ReadFrames(path);
    using Value = std::function<std::optional<int64_t>(const FD::FrameRow&)>;
    const std::vector<std::pair<const char*, Value>> columns{
      {"segment", [](const FD::FrameRow& r) { return std::optional<int64_t>(r.Segment); }},
      {"frameIndex", [](const FD::FrameRow& r) { return std::optional<int64_t>(static_cast<int64_t>(r.FrameIndex)); }},
      {"animationMs", [](const FD::FrameRow& r) { return Ticks(r.AnimationTime); }},
      {"firstCaptureIndex", [](const FD::FrameRow& r) { return std::optional<int64_t>(r.FirstCaptureIndex); }},
      {"firstSeenMs", [](const FD::FrameRow& r) { return Ticks(r.FirstSeenTime); }},
      {"onScreenMs", [](const FD::FrameRow& r) { return Ticks(r.OnScreen); }},
      {"captures", [](const FD::FrameRow& r) { return std::optional<int64_t>(r.Captures); }},
      {"skippedBefore", [](const FD::FrameRow& r) { return std::optional<int64_t>(static_cast<int64_t>(r.SkippedBefore)); }},
      {"displayDeltaMs", [](const FD::FrameRow& r) { return Ticks(r.DisplayDelta); }},
      {"animationDeltaMs", [](const FD::FrameRow& r) { return Ticks(r.AnimationDelta); }},
      {"animationErrorMs", [](const FD::FrameRow& r) { return Ticks(r.AnimationError); }},
      {"driftMs", [](const FD::FrameRow& r) { return Ticks(r.Drift); }},
      {"intendedDisplayMs", [](const FD::FrameRow& r) { return Ticks(r.IntendedDisplayTime); }},
      {"markerTargetMs", [](const FD::FrameRow& r) { return Ticks(r.MarkerTargetFrameTime); }},
      {"targetMs", [](const FD::FrameRow& r) { return Ticks(r.TargetFrameTime); }},
      {"pacingErrorMs", [](const FD::FrameRow& r) { return Ticks(r.PacingError); }},
      {"predictionErrorMs", [](const FD::FrameRow& r) { return Ticks(r.PredictionError); }},
      {"latenessMs", [](const FD::FrameRow& r) { return Ticks(r.Lateness); }},
      {"lastSeenMs", [](const FD::FrameRow& r) { return Ticks(r.LastSeenTime); }},
      {"cpuStartMs", [](const FD::FrameRow& r) { return Ticks(r.CpuStartTime); }},
      {"cpuBusyMs", [](const FD::FrameRow& r) { return Ticks(r.CpuBusy); }},
      {"frameTimeMs", [](const FD::FrameRow& r) { return Ticks(r.FrameTime); }},
      {"cpuWaitMs", [](const FD::FrameRow& r) { return Ticks(r.CpuWait); }},
      {"mainMarkerFirstSeenMs", [](const FD::FrameRow& r) { return Ticks(r.MainMarkerFirstSeenTime); }},
      {"scanoutDelayMs", [](const FD::FrameRow& r) { return Ticks(r.ScanoutDelay); }},
    };
    Json result = Json::object();
    for (const auto& [name, value] : columns)
    {
      int64_t count = 0;
      int64_t sum = 0;
      for (const auto& row : rows)
      {
        if (const auto v = value(row))
        {
          ++count;
          sum += *v;
        }
      }
      result[name] = {{"count", count}, {"sum", sum}};
    }
    std::vector<std::string> flags;
    for (const auto& row : rows)
    {
      flags.insert(flags.end(), row.Flags.begin(), row.Flags.end());
    }
    std::size_t olderCount = 0;
    uint64_t olderIndexSum = 0;
    for (const auto& row : rows)
    {
      olderCount += row.OlderFrames.size();
      for (const auto& older : row.OlderFrames)
      {
        olderIndexSum += older.FrameIndex;
      }
    }
    return {{"file", path.filename().string()}, {"rowCount", rows.size()},       {"columns", result},
            {"flagCounts", Counts(flags)},      {"olderFrameCount", olderCount}, {"olderFrameIndexSum", olderIndexSum}};
  }

  Json Captures(const std::filesystem::path& path)
  {
    const auto rows = FD::ReadCaptures(path);
    std::vector<std::string> statuses;
    std::vector<std::string> kinds;
    int64_t captureTicksSum = 0;
    int64_t frameIndexSum = 0;
    int64_t hostTicksSum = 0;
    int64_t sourceDropsSum = 0;
    int64_t missedSum = 0;
    int64_t syncCount = 0;
    int64_t syncFrameIndexSum = 0;
    int64_t payloadByteCount = 0;
    for (const auto& row : rows)
    {
      statuses.push_back(row.Status);
      if (row.Kind)
      {
        kinds.push_back(*row.Kind);
      }
      captureTicksSum += Ticks(row.CaptureTime).value_or(0);
      frameIndexSum += static_cast<int64_t>(row.FrameIndex.value_or(0));
      hostTicksSum += Ticks(row.HostTime).value_or(0);
      sourceDropsSum += row.SourceDropsBefore;
      missedSum += row.MissedBefore;
      if (row.SyncFrameIndex)
      {
        ++syncCount;
        syncFrameIndexSum += static_cast<int64_t>(*row.SyncFrameIndex);
      }
      payloadByteCount += static_cast<int64_t>(row.Payload.size());
    }
    return {
      {"rowCount", rows.size()},
      {"statusCounts", Counts(statuses)},
      {"kindCounts", Counts(kinds)},
      {"captureTicksSum", captureTicksSum},
      {"frameIndexSum", frameIndexSum},
      {"hostTicksSum", hostTicksSum},
      {"sourceDropsSum", sourceDropsSum},
      {"missedSum", missedSum},
      {"syncCount", syncCount},
      {"syncFrameIndexSum", syncFrameIndexSum},
      {"payloadByteCount", payloadByteCount},
    };
  }
}

TEST(GoldenData, TheDigestMatchesTheCSharpLibrary)
{
  const auto clip = ClipDirectory();
  if (!clip)
  {
    GTEST_SKIP() << "test-data/data not found (a copy of the library outside mb-framepacing)";
  }
  const auto analysis = *clip / FD::AnalysisDirectoryName;
  const auto summary = FD::ReadSummary(analysis / FD::SummaryFileName);
  Json frames = Json::array();
  for (const auto& run : summary.Runs)
  {
    frames.push_back(Frames(analysis / run.FramesFile));
  }
  const Json digest = {
    {"captureData", CaptureData(*clip / FD::CaptureDataReader::FileName)},
    {"summary", Summary(summary)},
    {"frames", frames},
    {"captures", Captures(analysis / FD::CapturesFileName)},
  };
  std::ifstream file(*clip / "digest.json");
  const Json expected = Json::parse(file);
  EXPECT_EQ(digest, expected) << "computed:\n" << digest.dump(2);
}
