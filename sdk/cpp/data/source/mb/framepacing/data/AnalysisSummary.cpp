// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/data/AnalysisSummary.hpp>
#include <mb/framepacing/data/Constants.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/SummaryCamera.hpp>
#include <mb/framepacing/data/SummaryHistogram.hpp>
#include <mb/framepacing/data/SummaryHistograms.hpp>
#include <mb/framepacing/data/SummaryPacing.hpp>
#include <mb/framepacing/data/SummaryRun.hpp>
#include <mb/framepacing/data/ValueStatistics.hpp>
#include <nlohmann/json.hpp>
#include <fstream>
#include <sstream>
#include <stdexcept>

namespace MB::FramePacing::Data
{
  namespace
  {
    using Json = nlohmann::json;

    const Json* Field(const Json& object, const char* name)
    {
      const auto found = object.find(name);
      return found != object.end() && !found->is_null() ? &*found : nullptr;
    }

    template <typename T>
    T Required(const Json& object, const char* name)
    {
      const Json* value = Field(object, name);
      if (value == nullptr)
      {
        throw DataFormatError(std::string("summary.json lacks '") + name + "'");
      }
      try
      {
        return value->get<T>();
      }
      catch (const Json::exception& error)
      {
        throw DataFormatError(std::string("summary.json: '") + name + "' " + error.what());
      }
    }

    template <typename T>
    std::optional<T> Optional(const Json& object, const char* name)
    {
      if (Field(object, name) == nullptr)
      {
        return std::nullopt;
      }
      return Required<T>(object, name);
    }

    template <typename T>
    T OrDefault(const Json& object, const char* name, const T fallback)
    {
      return Optional<T>(object, name).value_or(fallback);
    }

    std::vector<std::string> Texts(const Json& object, const char* name)
    {
      std::vector<std::string> texts;
      if (const Json* values = Field(object, name))
      {
        for (const Json& value : *values)
        {
          if (value.is_string())
          {
            texts.push_back(value.get<std::string>());
          }
        }
      }
      return texts;
    }

    ValueStatistics ToStatistics(const Json* value)
    {
      ValueStatistics statistics;
      if (value != nullptr)
      {
        statistics.Count = Required<int64_t>(*value, "count");
        statistics.Min = Required<double>(*value, "min");
        statistics.Mean = Required<double>(*value, "mean");
        statistics.StdDev = Required<double>(*value, "stdDev");
        statistics.P50 = Required<double>(*value, "p50");
        statistics.P95 = Required<double>(*value, "p95");
        statistics.P99 = Required<double>(*value, "p99");
        statistics.P999 = OrDefault<double>(*value, "p999", 0.0);
        statistics.Max = Required<double>(*value, "max");
      }
      return statistics;
    }

    SummaryHistogram ToHistogram(const Json& value)
    {
      SummaryHistogram histogram;
      histogram.BinWidthMs = Required<double>(value, "binWidthMs");
      histogram.Total = Required<int64_t>(value, "total");
      if (const Json* bins = Field(value, "bins"))
      {
        for (const Json& bin : *bins)
        {
          histogram.Bins.push_back({Required<double>(bin, "centerMs"), Required<int64_t>(bin, "count")});
        }
      }
      return histogram;
    }

    SummaryRun ToRun(const Json& value)
    {
      SummaryRun run;
      run.RunId = Required<uint32_t>(value, "runId");
      run.Name = Optional<std::string>(value, "name");
      run.SequenceId = Optional<std::string>(value, "sequenceId");
      run.StartTimeUtc = Optional<std::string>(value, "startTimeUtc");
      run.HasStartMarker = Required<bool>(value, "hasStartMarker");
      run.HasEndMarker = Required<bool>(value, "hasEndMarker");
      run.FramesFile = Required<std::string>(value, "framesFile");

      const Json& counts = value.at("counts");
      run.Counts.Captures = Required<int64_t>(counts, "captures");
      run.Counts.Decoded = Required<int64_t>(counts, "decoded");
      run.Counts.Undecodable = Required<int64_t>(counts, "undecodable");
      run.Counts.Torn = Required<int64_t>(counts, "torn");
      run.Counts.NotRecorded = Required<int64_t>(counts, "notRecorded");
      run.Counts.SourceDroppedFrames = Required<int64_t>(counts, "sourceDroppedFrames");
      run.Counts.MissedCaptures = Required<int64_t>(counts, "missedCaptures");
      run.Counts.PresentedFrames = Required<int64_t>(counts, "presentedFrames");
      run.Counts.SkippedFrameIndices = Required<int64_t>(counts, "skippedFrameIndices");
      run.Counts.DroppedFrames = Required<int64_t>(counts, "droppedFrames");
      run.Counts.OutOfOrderCaptures = Required<int64_t>(counts, "outOfOrderCaptures");
      run.Counts.Segments = Required<int64_t>(counts, "segments");

      const Json& statistics = value.at("statistics");
      auto& s = run.Statistics;
      s.DisplayDeltaMs = ToStatistics(Field(statistics, "displayDeltaMs"));
      s.AnimationDeltaMs = ToStatistics(Field(statistics, "animationDeltaMs"));
      s.AnimationErrorMs = ToStatistics(Field(statistics, "animationErrorMs"));
      s.AbsoluteAnimationErrorMs = ToStatistics(Field(statistics, "absoluteAnimationErrorMs"));
      s.DriftMs = ToStatistics(Field(statistics, "driftMs"));
      s.OnScreenMs = ToStatistics(Field(statistics, "onScreenMs"));
      s.FramesWithAnimationError = Required<int64_t>(statistics, "framesWithAnimationError");
      s.ErrorPerFrameMs = Required<double>(statistics, "errorPerFrameMs");
      s.PercentError = Required<double>(statistics, "percentError");
      s.AverageFps = OrDefault<double>(statistics, "averageFps", 0.0);
      s.OnePercentLowFps = Optional<double>(statistics, "onePercentLowFps");
      s.PointOnePercentLowFps = Optional<double>(statistics, "pointOnePercentLowFps");
      s.ExcludedStaticFrames = Required<int64_t>(statistics, "excludedStaticFrames");
      s.UncertainSteps = Required<int64_t>(statistics, "uncertainSteps");
      s.CpuBusyMs = ToStatistics(Field(statistics, "cpuBusyMs"));
      s.FrameTimeMs = ToStatistics(Field(statistics, "frameTimeMs"));
      s.CpuWaitMs = ToStatistics(Field(statistics, "cpuWaitMs"));

      if (const Json* pacing = Field(value, "pacing"))
      {
        SummaryPacing p;
        p.RefreshPeriodMs = Required<double>(*pacing, "refreshPeriodMs");
        p.RefreshCalculated = Required<bool>(*pacing, "refreshCalculated");
        p.TargetFrameMs = Required<double>(*pacing, "targetFrameMs");
        p.Source = Required<std::string>(*pacing, "source");
        p.LateFrames = Required<int64_t>(*pacing, "lateFrames");
        p.LateShare = Required<double>(*pacing, "lateShare");
        p.WorstLateShare = Required<double>(*pacing, "worstLateShare");
        p.ErrorFramesWithUnevenDisplay = Required<int64_t>(*pacing, "errorFramesWithUnevenDisplay");
        p.ErrorFramesWithEvenDisplay = Required<int64_t>(*pacing, "errorFramesWithEvenDisplay");
        p.Verdict = Required<std::string>(*pacing, "verdict");
        p.ExpectedRefreshHz = Optional<double>(*pacing, "expectedRefreshHz");
        if (const Json* pacingError = Field(*pacing, "pacingErrorMs"))
        {
          p.PacingErrorMs = ToStatistics(pacingError);
        }
        if (const Json* predictionError = Field(*pacing, "predictionErrorMs"))
        {
          p.PredictionErrorMs = ToStatistics(predictionError);
        }
        p.RefreshHz = OrDefault<double>(*pacing, "refreshHz", 0.0);
        p.RefreshDeviation = Optional<double>(*pacing, "refreshDeviation");
        p.MatchesExpectedRefresh = Optional<bool>(*pacing, "matchesExpectedRefresh");
        run.Pacing = p;
      }
      if (const Json* histograms = Field(value, "histograms"))
      {
        run.Histograms = SummaryHistograms{ToHistogram(histograms->at("animationErrorMs")), ToHistogram(histograms->at("displayDeltaMs"))};
      }
      if (const Json* camera = Field(value, "camera"))
      {
        run.Camera = SummaryCamera{ToStatistics(Field(*camera, "scanoutDelay")), Required<int64_t>(*camera, "framesSeenInBothZones"),
                                   Required<int64_t>(*camera, "tornFrames"), Required<int64_t>(*camera, "secondZoneOnlyFrames")};
      }
      run.Warnings = Texts(value, "warnings");
      return run;
    }
  }

  AnalysisSummary ParseSummary(const std::string_view json)
  {
    Json root;
    try
    {
      root = Json::parse(json);
    }
    catch (const Json::exception& error)
    {
      throw DataFormatError(std::string("summary.json is not JSON: ") + error.what());
    }
    if (!root.is_object())
    {
      throw DataFormatError("summary.json is not a JSON object");
    }
    AnalysisSummary summary;
    summary.FormatVersion = OrDefault<int32_t>(root, "formatVersion", 1);
    if (summary.FormatVersion > AnalysisFormatVersion)
    {
      throw DataFormatError("The analysis output has format version " + std::to_string(summary.FormatVersion) + ", newer than this reader reads (" +
                            std::to_string(AnalysisFormatVersion) + "): update the tools or the library");
    }
    summary.ToolVersion = Optional<std::string>(root, "toolVersion");
    summary.Experimental = Optional<std::string>(root, "experimental");
    summary.Scanout = Optional<std::string>(root, "scanout");
    summary.AnalysedUtc = Optional<std::string>(root, "analysedUtc");
    summary.CaptureDirectory = Optional<std::string>(root, "captureDirectory");
    if (const Json* capture = Field(root, "capture"))
    {
      summary.CaptureJson = capture->dump();
    }
    summary.FrameSize = Optional<std::string>(root, "frameSize");
    summary.TimeSource = Optional<std::string>(root, "timeSource");
    summary.CapturePeriodMs = Required<double>(root, "capturePeriodMs");
    summary.MeasurementResolutionMs = OrDefault<double>(root, "measurementResolutionMs", summary.CapturePeriodMs);
    summary.ErrorThresholdMs = Required<double>(root, "errorThresholdMs");
    if (const Json* markers = Field(root, "markers"))
    {
      for (const Json& marker : *markers)
      {
        summary.Markers.push_back({Required<std::string>(marker, "bounds"), Required<double>(marker, "moduleSizePx")});
      }
    }
    summary.Warnings = Texts(root, "warnings");
    if (const Json* runs = Field(root, "runs"))
    {
      for (const Json& run : *runs)
      {
        summary.Runs.push_back(ToRun(run));
      }
    }
    return summary;
  }

  AnalysisSummary ReadSummary(const std::filesystem::path& path)
  {
    const std::ifstream file(path, std::ios::binary);
    if (!file)
    {
      throw std::runtime_error("Cannot open '" + path.string() + "'");
    }
    std::ostringstream text;
    text << file.rdbuf();
    return ParseSummary(text.str());
  }
}
