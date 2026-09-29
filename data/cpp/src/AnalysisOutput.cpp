// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacingdata/FramePacingData.hpp>
#include <nlohmann/json.hpp>
#include <charconv>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <unordered_map>

namespace MB::FramePacingData
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
      run.Counts.SourceDropEvents = Required<int64_t>(counts, "sourceDropEvents");
      run.Counts.PresentedFrames = Required<int64_t>(counts, "presentedFrames");
      run.Counts.SkippedFrameIndices = Required<int64_t>(counts, "skippedFrameIndices");
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

    //! The lines of a CSV file, split on commas, and its columns by name.
    struct Table
    {
      std::unordered_map<std::string, std::size_t> Columns;
      std::vector<std::vector<std::string>> Rows;

      std::string_view Cell(const std::vector<std::string>& row, const char* name) const
      {
        const auto found = Columns.find(name);
        return found != Columns.end() && found->second < row.size() ? std::string_view(row[found->second]) : std::string_view();
      }
    };

    std::vector<std::string> Split(const std::string& line)
    {
      std::vector<std::string> cells;
      std::size_t start = 0;
      while (true)
      {
        const std::size_t comma = line.find(',', start);
        cells.push_back(line.substr(start, comma == std::string::npos ? std::string::npos : comma - start));
        if (comma == std::string::npos)
        {
          return cells;
        }
        start = comma + 1;
      }
    }

    Table ReadTable(const std::filesystem::path& path)
    {
      std::ifstream file(path, std::ios::binary);
      if (!file)
      {
        throw std::runtime_error("Cannot open '" + path.string() + "'");
      }
      Table table;
      std::string line;
      bool header = true;
      while (std::getline(file, line))
      {
        if (!line.empty() && line.back() == '\r')
        {
          line.pop_back();
        }
        if (header)
        {
          const auto names = Split(line);
          for (std::size_t i = 0; i < names.size(); ++i)
          {
            table.Columns[names[i]] = i;
          }
          header = false;
        }
        else if (!line.empty())
        {
          table.Rows.push_back(Split(line));
        }
      }
      if (header)
      {
        throw DataFormatError("'" + path.string() + "' is empty");
      }
      return table;
    }

    template <typename T>
    T ParseInteger(const std::string_view text)
    {
      T value{};
      const auto* const end = text.data() + text.size();
      const auto result = std::from_chars(text.data(), end, value);
      if (text.empty() || result.ec != std::errc() || result.ptr != end)
      {
        throw DataFormatError("'" + std::string(text) + "' is not an integer");
      }
      return value;
    }

    template <typename T>
    std::optional<T> OptionalInteger(const std::string_view text)
    {
      return text.empty() ? std::nullopt : std::optional<T>(ParseInteger<T>(text));
    }

    std::optional<int64_t> OptionalTicks(const std::string_view text)
    {
      return text.empty() ? std::nullopt : std::optional<int64_t>(ParseTicks(text));
    }

    std::vector<uint8_t> FromHex(const std::string_view text)
    {
      if (text.size() % 2 != 0)
      {
        throw DataFormatError("'" + std::string(text) + "' is not hexadecimal bytes");
      }
      std::vector<uint8_t> bytes(text.size() / 2);
      for (std::size_t i = 0; i < bytes.size(); ++i)
      {
        const auto* const begin = text.data() + (2 * i);
        const auto result = std::from_chars(begin, begin + 2, bytes[i], 16);
        if (result.ec != std::errc() || result.ptr != begin + 2)
        {
          throw DataFormatError("'" + std::string(text) + "' is not hexadecimal bytes");
        }
      }
      return bytes;
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
    std::ifstream file(path, std::ios::binary);
    if (!file)
    {
      throw std::runtime_error("Cannot open '" + path.string() + "'");
    }
    std::ostringstream text;
    text << file.rdbuf();
    return ParseSummary(text.str());
  }

  std::vector<FrameRow> ReadFrames(const std::filesystem::path& path)
  {
    const Table table = ReadTable(path);
    std::vector<FrameRow> frames;
    frames.reserve(table.Rows.size());
    for (const auto& row : table.Rows)
    {
      FrameRow frame;
      frame.Segment = ParseInteger<int32_t>(table.Cell(row, "segment"));
      frame.FrameIndex = ParseInteger<uint64_t>(table.Cell(row, "frameIndex"));
      frame.AnimationTicks = ParseTicks(table.Cell(row, "animationMs"));
      frame.FirstCaptureIndex = ParseInteger<int64_t>(table.Cell(row, "firstCaptureIndex"));
      frame.FirstSeenTicks = ParseTicks(table.Cell(row, "firstSeenMs"));
      frame.OnScreenTicks = ParseTicks(table.Cell(row, "onScreenMs"));
      frame.Captures = ParseInteger<int32_t>(table.Cell(row, "captures"));
      frame.SkippedBefore = ParseInteger<uint64_t>(table.Cell(row, "skippedBefore"));
      frame.DisplayDeltaTicks = OptionalTicks(table.Cell(row, "displayDeltaMs"));
      frame.AnimationDeltaTicks = OptionalTicks(table.Cell(row, "animationDeltaMs"));
      frame.AnimationErrorTicks = OptionalTicks(table.Cell(row, "animationErrorMs"));
      frame.DriftTicks = ParseTicks(table.Cell(row, "driftMs"));
      const std::string_view flags = table.Cell(row, "flags");
      for (std::size_t start = 0; start < flags.size();)
      {
        const std::size_t bar = flags.find('|', start);
        const std::size_t stop = bar == std::string_view::npos ? flags.size() : bar;
        frame.Flags.emplace_back(flags.substr(start, stop - start));
        start = stop + 1;
      }
      frame.IntendedDisplayTicks = OptionalTicks(table.Cell(row, "intendedDisplayMs"));
      frame.MarkerTargetTicks = OptionalTicks(table.Cell(row, "markerTargetMs"));
      frame.TargetTicks = OptionalTicks(table.Cell(row, "targetMs"));
      frame.MarkerPreferredTicks = OptionalTicks(table.Cell(row, "markerPreferredMs"));
      frame.PreferredTicks = OptionalTicks(table.Cell(row, "preferredMs"));
      frame.PacingErrorTicks = OptionalTicks(table.Cell(row, "pacingErrorMs"));
      frame.PredictionErrorTicks = OptionalTicks(table.Cell(row, "predictionErrorMs"));
      frame.LatenessTicks = OptionalTicks(table.Cell(row, "latenessMs"));
      frame.LastSeenTicks = OptionalTicks(table.Cell(row, "lastSeenMs"));
      frame.CpuStartTicks = OptionalTicks(table.Cell(row, "cpuStartMs"));
      frame.CpuBusyTicks = OptionalTicks(table.Cell(row, "cpuBusyMs"));
      frame.FrameTimeTicks = OptionalTicks(table.Cell(row, "frameTimeMs"));
      frame.CpuWaitTicks = OptionalTicks(table.Cell(row, "cpuWaitMs"));
      frame.MainMarkerFirstSeenTicks = OptionalTicks(table.Cell(row, "mainMarkerFirstSeenMs"));
      frame.ScanoutDelayTicks = OptionalTicks(table.Cell(row, "scanoutDelayMs"));
      frames.push_back(std::move(frame));
    }
    return frames;
  }

  std::vector<CaptureCsvRow> ReadCaptures(const std::filesystem::path& path)
  {
    const Table table = ReadTable(path);
    std::vector<CaptureCsvRow> captures;
    captures.reserve(table.Rows.size());
    for (const auto& row : table.Rows)
    {
      CaptureCsvRow capture;
      capture.CaptureIndex = ParseInteger<int64_t>(table.Cell(row, "captureIndex"));
      capture.CaptureTicks = OptionalTicks(table.Cell(row, "captureMs"));
      capture.Status = std::string(table.Cell(row, "status"));
      if (const std::string_view kind = table.Cell(row, "kind"); !kind.empty())
      {
        capture.Kind = std::string(kind);
      }
      capture.RunId = OptionalInteger<uint32_t>(table.Cell(row, "runId"));
      capture.FrameIndex = OptionalInteger<uint64_t>(table.Cell(row, "frameIndex"));
      capture.AnimationTicks = OptionalTicks(table.Cell(row, "animationMs"));
      capture.SourceDropBefore = table.Cell(row, "sourceDropBefore") == "1";
      capture.HostTicks = OptionalTicks(table.Cell(row, "hostMs"));
      capture.DeviceTicks = OptionalTicks(table.Cell(row, "deviceMs"));
      capture.Payload = FromHex(table.Cell(row, "payloadHex"));
      capture.SecondZoneFrameIndex = OptionalInteger<uint64_t>(table.Cell(row, "secondZoneFrameIndex"));
      captures.push_back(std::move(capture));
    }
    return captures;
  }

  std::optional<std::filesystem::path> FindAnalysis(const std::filesystem::path& folder)
  {
    if (std::filesystem::is_regular_file(folder / SummaryFileName))
    {
      return folder;
    }
    const auto analysis = folder / AnalysisDirectoryName;
    if (std::filesystem::is_regular_file(analysis / SummaryFileName))
    {
      return analysis;
    }
    return std::nullopt;
  }

  std::string RunFilePrefix(const uint32_t runId, const int32_t ordinal)
  {
    return ordinal == 0 ? "run-" + std::to_string(runId) : "run-" + std::to_string(runId) + "-" + std::to_string(ordinal + 1);
  }

  std::string FramesFileName(const uint32_t runId, const int32_t ordinal)
  {
    return RunFilePrefix(runId, ordinal) + "-frames.csv";
  }

  int64_t ParseTicks(const std::string_view milliseconds)
  {
    // Decimal digits, not floating point: exact for any value the files hold
    std::string_view text = milliseconds;
    const bool negative = !text.empty() && text.front() == '-';
    if (negative)
    {
      text.remove_prefix(1);
    }
    const std::size_t point = text.find('.');
    const std::string_view whole = text.substr(0, point);
    const std::string_view fraction = point == std::string_view::npos ? std::string_view() : text.substr(point + 1);
    const auto isDigits = [](const std::string_view digits) { return digits.find_first_not_of("0123456789") == std::string_view::npos; };
    if (whole.empty() || !isDigits(whole) || !isDigits(fraction) || (point != std::string_view::npos && fraction.empty()))
    {
      throw DataFormatError("'" + std::string(milliseconds) + "' is not a milliseconds value");
    }
    int64_t ticks = ParseInteger<int64_t>(whole) * TicksPerMillisecond;
    int64_t scale = TicksPerMillisecond / 10;
    for (std::size_t i = 0; i < fraction.size() && scale > 0; ++i, scale /= 10)
    {
      ticks += static_cast<int64_t>(fraction[i] - '0') * scale;
    }
    // A fifth decimal rounds to the nearest tick
    if (fraction.size() > 4 && fraction[4] >= '5')
    {
      ++ticks;
    }
    return negative ? -ticks : ticks;
  }
}
