// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/FrameRow.hpp>
#include <mb/framepacing/data/analysis/FramesCsv.hpp>
#include <mb/framepacing/data/analysis/Milliseconds.hpp>
#include <string>
#include "detail/CsvCells.hpp"
#include "detail/CsvTable.hpp"

namespace MB::FramePacing::Data
{
  std::vector<FrameRow> ReadFrames(const std::filesystem::path& path)
  {
    const Detail::CsvTable table = Detail::CsvTable::Read(path);
    std::vector<FrameRow> frames;
    frames.reserve(table.Rows.size());
    for (const auto& row : table.Rows)
    {
      FrameRow frame;
      frame.Segment = Detail::ParseInteger<int32_t>(table.Cell(row, "segment"));
      frame.FrameIndex = Detail::ParseInteger<uint64_t>(table.Cell(row, "frameIndex"));
      frame.AnimationTime = ParseMilliseconds(table.Cell(row, "animationMs"));
      frame.FirstCaptureIndex = Detail::ParseInteger<int64_t>(table.Cell(row, "firstCaptureIndex"));
      frame.FirstSeenTime = Detail::ParseTickCount64(table.Cell(row, "firstSeenMs"));
      frame.OnScreen = ParseMilliseconds(table.Cell(row, "onScreenMs"));
      frame.Captures = Detail::ParseInteger<int32_t>(table.Cell(row, "captures"));
      frame.SkippedBefore = Detail::ParseInteger<uint64_t>(table.Cell(row, "skippedBefore"));
      frame.DisplayDelta = Detail::OptionalTimeSpan(table.Cell(row, "displayDeltaMs"));
      frame.AnimationDelta = Detail::OptionalTimeSpan(table.Cell(row, "animationDeltaMs"));
      frame.AnimationError = Detail::OptionalTimeSpan(table.Cell(row, "animationErrorMs"));
      frame.Drift = ParseMilliseconds(table.Cell(row, "driftMs"));
      const std::string_view flags = table.Cell(row, "flags");
      for (std::size_t start = 0; start < flags.size();)
      {
        const std::size_t bar = flags.find('|', start);
        const std::size_t stop = bar == std::string_view::npos ? flags.size() : bar;
        frame.Flags.emplace_back(flags.substr(start, stop - start));
        start = stop + 1;
      }
      frame.IntendedDisplayTime = Detail::OptionalTickCount64(table.Cell(row, "intendedDisplayMs"));
      frame.MarkerTargetFrameTime = Detail::OptionalTimeSpan32(table.Cell(row, "markerTargetMs"));
      frame.TargetFrameTime = Detail::OptionalTimeSpan(table.Cell(row, "targetMs"));
      frame.MarkerPreferredFrameTime = Detail::OptionalTimeSpan32(table.Cell(row, "markerPreferredMs"));
      frame.PreferredFrameTime = Detail::OptionalTimeSpan(table.Cell(row, "preferredMs"));
      frame.PacingError = Detail::OptionalTimeSpan(table.Cell(row, "pacingErrorMs"));
      frame.PredictionError = Detail::OptionalTimeSpan(table.Cell(row, "predictionErrorMs"));
      frame.Lateness = Detail::OptionalTimeSpan(table.Cell(row, "latenessMs"));
      frame.LastSeenTime = Detail::OptionalTickCount64(table.Cell(row, "lastSeenMs"));
      frame.CpuStartTime = Detail::OptionalTickCount64(table.Cell(row, "cpuStartMs"));
      frame.CpuBusy = Detail::OptionalTimeSpan32(table.Cell(row, "cpuBusyMs"));
      frame.FrameTime = Detail::OptionalTimeSpan(table.Cell(row, "frameTimeMs"));
      frame.CpuWait = Detail::OptionalTimeSpan(table.Cell(row, "cpuWaitMs"));
      // olderFrames: frameIndex@captureMs entries separated by |
      const std::string_view older = table.Cell(row, "olderFrames");
      for (std::size_t start = 0; start < older.size();)
      {
        const std::size_t bar = older.find('|', start);
        const std::size_t stop = bar == std::string_view::npos ? older.size() : bar;
        const std::string_view entry = older.substr(start, stop - start);
        const std::size_t at = entry.find('@');
        if (at == std::string_view::npos || at == 0)
        {
          throw DataFormatError("Invalid olderFrames entry '" + std::string(entry) + "'");
        }
        frame.OlderFrames.push_back({Detail::ParseInteger<uint64_t>(entry.substr(0, at)), Detail::ParseTickCount64(entry.substr(at + 1))});
        start = stop + 1;
      }
      frame.MainMarkerFirstSeenTime = Detail::OptionalTickCount64(table.Cell(row, "mainMarkerFirstSeenMs"));
      frame.ScanoutDelay = Detail::OptionalTimeSpan(table.Cell(row, "scanoutDelayMs"));
      frames.push_back(std::move(frame));
    }
    return frames;
  }
}
