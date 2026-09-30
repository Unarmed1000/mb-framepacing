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
      frame.AnimationTicks = ParseTicks(table.Cell(row, "animationMs"));
      frame.FirstCaptureIndex = Detail::ParseInteger<int64_t>(table.Cell(row, "firstCaptureIndex"));
      frame.FirstSeenTicks = ParseTicks(table.Cell(row, "firstSeenMs"));
      frame.OnScreenTicks = ParseTicks(table.Cell(row, "onScreenMs"));
      frame.Captures = Detail::ParseInteger<int32_t>(table.Cell(row, "captures"));
      frame.SkippedBefore = Detail::ParseInteger<uint64_t>(table.Cell(row, "skippedBefore"));
      frame.DisplayDeltaTicks = Detail::OptionalTicks(table.Cell(row, "displayDeltaMs"));
      frame.AnimationDeltaTicks = Detail::OptionalTicks(table.Cell(row, "animationDeltaMs"));
      frame.AnimationErrorTicks = Detail::OptionalTicks(table.Cell(row, "animationErrorMs"));
      frame.DriftTicks = ParseTicks(table.Cell(row, "driftMs"));
      const std::string_view flags = table.Cell(row, "flags");
      for (std::size_t start = 0; start < flags.size();)
      {
        const std::size_t bar = flags.find('|', start);
        const std::size_t stop = bar == std::string_view::npos ? flags.size() : bar;
        frame.Flags.emplace_back(flags.substr(start, stop - start));
        start = stop + 1;
      }
      frame.IntendedDisplayTicks = Detail::OptionalTicks(table.Cell(row, "intendedDisplayMs"));
      frame.MarkerTargetTicks = Detail::OptionalTicks(table.Cell(row, "markerTargetMs"));
      frame.TargetTicks = Detail::OptionalTicks(table.Cell(row, "targetMs"));
      frame.MarkerPreferredTicks = Detail::OptionalTicks(table.Cell(row, "markerPreferredMs"));
      frame.PreferredTicks = Detail::OptionalTicks(table.Cell(row, "preferredMs"));
      frame.PacingErrorTicks = Detail::OptionalTicks(table.Cell(row, "pacingErrorMs"));
      frame.PredictionErrorTicks = Detail::OptionalTicks(table.Cell(row, "predictionErrorMs"));
      frame.LatenessTicks = Detail::OptionalTicks(table.Cell(row, "latenessMs"));
      frame.LastSeenTicks = Detail::OptionalTicks(table.Cell(row, "lastSeenMs"));
      frame.CpuStartTicks = Detail::OptionalTicks(table.Cell(row, "cpuStartMs"));
      frame.CpuBusyTicks = Detail::OptionalTicks(table.Cell(row, "cpuBusyMs"));
      frame.FrameTimeTicks = Detail::OptionalTicks(table.Cell(row, "frameTimeMs"));
      frame.CpuWaitTicks = Detail::OptionalTicks(table.Cell(row, "cpuWaitMs"));
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
        frame.OlderFrames.push_back({Detail::ParseInteger<uint64_t>(entry.substr(0, at)), ParseTicks(entry.substr(at + 1))});
        start = stop + 1;
      }
      frame.MainMarkerFirstSeenTicks = Detail::OptionalTicks(table.Cell(row, "mainMarkerFirstSeenMs"));
      frame.ScanoutDelayTicks = Detail::OptionalTicks(table.Cell(row, "scanoutDelayMs"));
      frames.push_back(std::move(frame));
    }
    return frames;
  }
}
