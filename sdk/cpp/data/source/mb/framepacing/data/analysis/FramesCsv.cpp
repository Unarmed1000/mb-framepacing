// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/FrameRow.hpp>
#include <mb/framepacing/data/analysis/FramesCsv.hpp>
#include <cstddef>
#include <string>
#include <string_view>
#include "detail/CsvCells.hpp"
#include "detail/CsvTable.hpp"

namespace MB::FramePacing::Data
{
  namespace
  {
    //! The cell's entries, separated by '|': none for an empty cell. Throws DataFormatError for an empty entry.
    template <typename TEntry>
    void ForEachEntry(const std::string_view cell, const char* column, const TEntry& entry)
    {
      for (std::size_t start = 0; start < cell.size();)
      {
        const std::size_t bar = cell.find('|', start);
        const std::size_t stop = bar == std::string_view::npos ? cell.size() : bar;
        if (stop == start || stop + 1 == cell.size())
        {
          throw DataFormatError(std::string("An empty entry in ") + column + " '" + std::string(cell) + "'");
        }
        entry(cell.substr(start, stop - start));
        start = stop + 1;
      }
    }

    FrameRow ToFrame(const Csv::CsvTable& table, const std::vector<std::string>& row)
    {
      FrameRow frame;
      frame.Segment = Csv::ParseInteger<int32_t>(table.Cell(row, "segment"));
      frame.FrameIndex = Csv::ParseInteger<uint64_t>(table.Cell(row, "frameIndex"));
      frame.AnimationTime = Csv::ParseTimeSpan(table.Cell(row, "animationTicks"));
      frame.FirstCaptureIndex = Csv::ParseInteger<int64_t>(table.Cell(row, "firstCaptureIndex"));
      frame.FirstSeenTime = Csv::ParseTickCount64(table.Cell(row, "firstSeenTicks"));
      frame.OnScreen = Csv::ParseTimeSpan(table.Cell(row, "onScreenTicks"));
      frame.Captures = Csv::ParseInteger<int32_t>(table.Cell(row, "captures"));
      frame.SkippedBefore = Csv::ParseInteger<uint64_t>(table.Cell(row, "skippedBefore"));
      frame.DisplayDelta = Csv::OptionalTimeSpan(table.Cell(row, "displayDeltaTicks"));
      frame.AnimationDelta = Csv::OptionalTimeSpan(table.Cell(row, "animationDeltaTicks"));
      frame.AnimationError = Csv::OptionalTimeSpan(table.Cell(row, "animationErrorTicks"));
      frame.Drift = Csv::ParseTimeSpan(table.Cell(row, "driftTicks"));
      ForEachEntry(table.Cell(row, "flags"), "flags", [&frame](const std::string_view flag) { frame.Flags.emplace_back(flag); });
      frame.IntendedDisplayTime = Csv::OptionalTickCount64(table.Cell(row, "intendedDisplayTicks"));
      frame.MarkerTargetFrameTime = Csv::OptionalTimeSpan32(table.Cell(row, "markerTargetTicks"));
      frame.TargetFrameTime = Csv::OptionalTimeSpan(table.Cell(row, "targetTicks"));
      frame.MarkerPreferredFrameTime = Csv::OptionalTimeSpan32(table.Cell(row, "markerPreferredTicks"));
      frame.PreferredFrameTime = Csv::OptionalTimeSpan(table.Cell(row, "preferredTicks"));
      frame.PacingError = Csv::OptionalTimeSpan(table.Cell(row, "pacingErrorTicks"));
      frame.PredictionError = Csv::OptionalTimeSpan(table.Cell(row, "predictionErrorTicks"));
      frame.Lateness = Csv::OptionalTimeSpan(table.Cell(row, "latenessTicks"));
      frame.LastSeenTime = Csv::OptionalTickCount64(table.Cell(row, "lastSeenTicks"));
      frame.CpuStartTime = Csv::OptionalTickCount64(table.Cell(row, "cpuStartTicks"));
      frame.CpuBusy = Csv::OptionalTimeSpan32(table.Cell(row, "cpuBusyTicks"));
      frame.FrameTime = Csv::OptionalTimeSpan(table.Cell(row, "frameTimeTicks"));
      frame.CpuWait = Csv::OptionalTimeSpan(table.Cell(row, "cpuWaitTicks"));
      // olderFrames: frameIndex@captureTicks entries separated by |
      ForEachEntry(table.Cell(row, "olderFrames"), "olderFrames",
                   [&frame](const std::string_view entry)
                   {
                     const std::size_t at = entry.find('@');
                     if (at == std::string_view::npos || at == 0)
                     {
                       throw DataFormatError("Invalid olderFrames entry '" + std::string(entry) + "'");
                     }
                     frame.OlderFrames.push_back({Csv::ParseInteger<uint64_t>(entry.substr(0, at)), Csv::ParseTickCount64(entry.substr(at + 1))});
                   });
      frame.MainMarkerFirstSeenTime = Csv::OptionalTickCount64(table.Cell(row, "mainMarkerFirstSeenTicks"));
      frame.ScanoutDelay = Csv::OptionalTimeSpan(table.Cell(row, "scanoutDelayTicks"));
      return frame;
    }
  }

  std::vector<FrameRow> ReadFrames(const std::filesystem::path& path)
  {
    const Csv::CsvTable table = Csv::CsvTable::Read(path);
    std::vector<FrameRow> frames;
    frames.reserve(table.Rows.size());
    for (std::size_t i = 0; i < table.Rows.size(); ++i)
    {
      try
      {
        frames.push_back(ToFrame(table, table.Rows[i]));
      }
      catch (const DataFormatError& error)
      {
        throw table.InRow(i, error);
      }
    }
    return frames;
  }
}
