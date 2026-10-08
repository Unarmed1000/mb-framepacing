// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/AnalysisSummary.hpp>
#include <mb/framepacing/data/analysis/SummaryCamera.hpp>
#include <mb/framepacing/data/analysis/SummaryHistogram.hpp>
#include <mb/framepacing/data/analysis/SummaryHistograms.hpp>
#include <mb/framepacing/data/analysis/SummaryPacing.hpp>
#include <mb/framepacing/data/analysis/SummaryRun.hpp>
#include <mb/framepacing/data/analysis/ValueStatistics.hpp>
#include <nlohmann/json.hpp>
#include <concepts>
#include <cstdint>
#include <fstream>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace MB::FramePacing::Data
{
  namespace
  {
    //! The newest analysis output format this reader reads.
    constexpr int32_t AnalysisFormatVersion = 1;

    using Json = nlohmann::json;

    //! The member, or null when the object lacks it or holds null: absent and null are the same (the writer leaves nulls out).
    const Json* Field(const Json& object, const char* name)
    {
      const auto found = object.find(name);
      return found != object.end() && !found->is_null() ? &*found : nullptr;
    }

    [[noreturn]] void ThrowLacks(const char* name)
    {
      throw DataFormatError(std::string("summary.json lacks '") + name + "'");
    }

    [[noreturn]] void ThrowNot(const char* name, const char* what)
    {
      throw DataFormatError(std::string("summary.json: '") + name + "' is not " + what);
    }

    //! The value as a T, exactly: a whole number within T's range for an integer, any number for a real one, and never one kind as
    //! another (nlohmann's get<T> casts: -1 becomes 4294967295 as a uint32_t).
    template <typename T>
    T Convert(const Json& value, const char* name)
    {
      if constexpr (std::same_as<T, bool>)
      {
        if (!value.is_boolean())
        {
          ThrowNot(name, "true or false");
        }
        return value.template get<bool>();
      }
      else if constexpr (std::integral<T>)
      {
        if (value.is_number_unsigned())
        {
          if (const auto number = value.template get<uint64_t>(); std::in_range<T>(number))
          {
            return static_cast<T>(number);
          }
        }
        else if (value.is_number_integer())
        {
          if (const auto number = value.template get<int64_t>(); std::in_range<T>(number))
          {
            return static_cast<T>(number);
          }
        }
        ThrowNot(name, "a whole number in its range");
      }
      else if constexpr (std::floating_point<T>)
      {
        if (!value.is_number())
        {
          ThrowNot(name, "a number");
        }
        return value.template get<T>();
      }
      else
      {
        if (!value.is_string())
        {
          ThrowNot(name, "a text");
        }
        return value.template get<T>();
      }
    }

    template <typename T>
    T Required(const Json& object, const char* name)
    {
      const Json* value = Field(object, name);
      if (value == nullptr)
      {
        ThrowLacks(name);
      }
      return Convert<T>(*value, name);
    }

    template <typename T>
    std::optional<T> Optional(const Json& object, const char* name)
    {
      const Json* value = Field(object, name);
      return value != nullptr ? std::optional<T>(Convert<T>(*value, name)) : std::nullopt;
    }

    template <typename T>
    T OrDefault(const Json& object, const char* name, const T fallback)
    {
      return Optional<T>(object, name).value_or(fallback);
    }

    //! The member when it is an object, null when absent. Throws DataFormatError for anything else.
    const Json* OptionalObject(const Json& object, const char* name)
    {
      const Json* value = Field(object, name);
      if (value != nullptr && !value->is_object())
      {
        ThrowNot(name, "an object");
      }
      return value;
    }

    const Json& RequiredObject(const Json& object, const char* name)
    {
      const Json* value = OptionalObject(object, name);
      if (value == nullptr)
      {
        ThrowLacks(name);
      }
      return *value;
    }

    //! The member when it is a list, null when absent. Throws DataFormatError for anything else.
    const Json* OptionalList(const Json& object, const char* name)
    {
      const Json* value = Field(object, name);
      if (value != nullptr && !value->is_array())
      {
        ThrowNot(name, "a list");
      }
      return value;
    }

    //! A list's entry must be an object.
    void RequireObject(const Json& entry, const char* name)
    {
      if (!entry.is_object())
      {
        ThrowNot(name, "a list of objects");
      }
    }

    //! A time: a whole number of nanoseconds.
    NanosecondTimeSpan RequiredNanoseconds(const Json& object, const char* name)
    {
      return NanosecondTimeSpan(Required<int64_t>(object, name));
    }

    std::vector<std::string> Texts(const Json& object, const char* name)
    {
      std::vector<std::string> texts;
      if (const Json* values = OptionalList(object, name))
      {
        for (const Json& value : *values)
        {
          texts.push_back(Convert<std::string>(value, name));
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
      const Json* bins = OptionalList(value, "bins");
      if (bins == nullptr)
      {
        ThrowLacks("bins");
      }
      for (const Json& bin : *bins)
      {
        RequireObject(bin, "bins");
        histogram.Bins.push_back({Required<double>(bin, "centerMs"), Required<int64_t>(bin, "count")});
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

      const Json& counts = RequiredObject(value, "counts");
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

      const Json& statistics = RequiredObject(value, "statistics");
      auto& s = run.Statistics;
      s.DisplayDeltaMs = ToStatistics(&RequiredObject(statistics, "displayDeltaMs"));
      s.AnimationDeltaMs = ToStatistics(&RequiredObject(statistics, "animationDeltaMs"));
      s.AnimationErrorMs = ToStatistics(&RequiredObject(statistics, "animationErrorMs"));
      s.AbsoluteAnimationErrorMs = ToStatistics(&RequiredObject(statistics, "absoluteAnimationErrorMs"));
      s.DriftMs = ToStatistics(&RequiredObject(statistics, "driftMs"));
      s.OnScreenMs = ToStatistics(&RequiredObject(statistics, "onScreenMs"));
      s.FramesWithAnimationError = Required<int64_t>(statistics, "framesWithAnimationError");
      s.ErrorPerFrameMs = Required<double>(statistics, "errorPerFrameMs");
      s.PercentError = Required<double>(statistics, "percentError");
      s.AverageFps = OrDefault<double>(statistics, "averageFps", 0.0);
      s.OnePercentLowFps = Optional<double>(statistics, "onePercentLowFps");
      s.PointOnePercentLowFps = Optional<double>(statistics, "pointOnePercentLowFps");
      s.ExcludedStaticFrames = Required<int64_t>(statistics, "excludedStaticFrames");
      s.UncertainSteps = Required<int64_t>(statistics, "uncertainSteps");
      s.CpuBusyMs = ToStatistics(OptionalObject(statistics, "cpuBusyMs"));
      s.FrameTimeMs = ToStatistics(OptionalObject(statistics, "frameTimeMs"));
      s.CpuWaitMs = ToStatistics(OptionalObject(statistics, "cpuWaitMs"));

      if (const Json* pacing = OptionalObject(value, "pacing"))
      {
        SummaryPacing p;
        p.RefreshPeriod = RequiredNanoseconds(*pacing, "refreshPeriodNs");
        p.RefreshCalculated = Required<bool>(*pacing, "refreshCalculated");
        p.TargetFrameTime = RequiredNanoseconds(*pacing, "targetFrameNs");
        p.Source = Required<std::string>(*pacing, "source");
        p.LateFrames = Required<int64_t>(*pacing, "lateFrames");
        p.LateShare = Required<double>(*pacing, "lateShare");
        p.WorstLateShare = Required<double>(*pacing, "worstLateShare");
        p.ErrorFramesWithUnevenDisplay = Required<int64_t>(*pacing, "errorFramesWithUnevenDisplay");
        p.ErrorFramesWithEvenDisplay = Required<int64_t>(*pacing, "errorFramesWithEvenDisplay");
        p.Verdict = Required<std::string>(*pacing, "verdict");
        p.ExpectedRefreshHz = Optional<double>(*pacing, "expectedRefreshHz");
        if (const Json* pacingError = OptionalObject(*pacing, "pacingErrorMs"))
        {
          p.PacingErrorMs = ToStatistics(pacingError);
        }
        if (const Json* predictionError = OptionalObject(*pacing, "predictionErrorMs"))
        {
          p.PredictionErrorMs = ToStatistics(predictionError);
        }
        p.RefreshHz = OrDefault<double>(*pacing, "refreshHz", 0.0);
        p.RefreshDeviation = Optional<double>(*pacing, "refreshDeviation");
        p.MatchesExpectedRefresh = Optional<bool>(*pacing, "matchesExpectedRefresh");
        run.Pacing = p;
      }
      if (const Json* histograms = OptionalObject(value, "histograms"))
      {
        run.Histograms =
          SummaryHistograms{ToHistogram(RequiredObject(*histograms, "animationErrorMs")), ToHistogram(RequiredObject(*histograms, "displayDeltaMs"))};
      }
      if (const Json* camera = OptionalObject(value, "camera"))
      {
        run.Camera = SummaryCamera{ToStatistics(&RequiredObject(*camera, "scanoutDelay")), Required<int64_t>(*camera, "framesSeenInBothZones"),
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
    // 0 is a file without the field, as C# reads it
    summary.FormatVersion = OrDefault<int32_t>(root, "formatVersion", 0);
    if (summary.FormatVersion < 0)
    {
      ThrowNot("formatVersion", "a format version");
    }
    if (summary.FormatVersion == 0)
    {
      summary.FormatVersion = 1;
    }
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
    summary.CapturePeriod = RequiredNanoseconds(root, "capturePeriodNs");
    // 0 is a file without the field, as C# reads it
    summary.MeasurementResolution = NanosecondTimeSpan(OrDefault<int64_t>(root, "measurementResolutionNs", 0));
    if (summary.MeasurementResolution == NanosecondTimeSpan::Zero())
    {
      summary.MeasurementResolution = summary.CapturePeriod;
    }
    summary.ErrorThreshold = RequiredNanoseconds(root, "errorThresholdNs");
    if (const Json* markers = OptionalList(root, "markers"))
    {
      for (const Json& marker : *markers)
      {
        RequireObject(marker, "markers");
        summary.Markers.push_back({Required<std::string>(marker, "bounds"), Required<double>(marker, "moduleSizePx")});
      }
    }
    summary.Warnings = Texts(root, "warnings");
    if (const Json* runs = OptionalList(root, "runs"))
    {
      for (const Json& run : *runs)
      {
        RequireObject(run, "runs");
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
