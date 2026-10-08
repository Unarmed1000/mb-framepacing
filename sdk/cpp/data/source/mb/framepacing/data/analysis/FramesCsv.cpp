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
      frame.AnimationTime = Csv::ParseNanosecondTimeSpan(table.Cell(row, "animationNs"));
      frame.FirstCaptureIndex = Csv::ParseInteger<int64_t>(table.Cell(row, "firstCaptureIndex"));
      frame.FirstSeenTime = Csv::ParseNanosecondTickCount(table.Cell(row, "firstSeenNs"));
      frame.OnScreen = Csv::ParseNanosecondTimeSpan(table.Cell(row, "onScreenNs"));
      frame.Captures = Csv::ParseInteger<int32_t>(table.Cell(row, "captures"));
      frame.SkippedBefore = Csv::ParseInteger<uint64_t>(table.Cell(row, "skippedBefore"));
      frame.DisplayDelta = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "displayDeltaNs"));
      frame.AnimationDelta = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "animationDeltaNs"));
      frame.AnimationError = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "animationErrorNs"));
      frame.Drift = Csv::ParseNanosecondTimeSpan(table.Cell(row, "driftNs"));
      ForEachEntry(table.Cell(row, "flags"), "flags", [&frame](const std::string_view flag) { frame.Flags.emplace_back(flag); });
      frame.IntendedDisplayTime = Csv::OptionalNanosecondTickCount(table.Cell(row, "intendedDisplayNs"));
      frame.MarkerTargetFrameTime = Csv::OptionalNanosecondTimeDuration(table.Cell(row, "markerTargetNs"));
      frame.TargetFrameTime = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "targetNs"));
      frame.MarkerPreferredFrameTime = Csv::OptionalNanosecondTimeDuration(table.Cell(row, "markerPreferredNs"));
      frame.PreferredFrameTime = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "preferredNs"));
      frame.PacingError = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "pacingErrorNs"));
      frame.PredictionError = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "predictionErrorNs"));
      frame.Lateness = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "latenessNs"));
      frame.LastSeenTime = Csv::OptionalNanosecondTickCount(table.Cell(row, "lastSeenNs"));
      frame.CpuStartTime = Csv::OptionalNanosecondTickCount(table.Cell(row, "cpuStartNs"));
      frame.CpuBusy = Csv::OptionalNanosecondTimeDuration(table.Cell(row, "cpuBusyNs"));
      frame.FrameTime = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "frameTimeNs"));
      frame.CpuWait = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "cpuWaitNs"));
      // olderFrames: frameIndex@captureNs entries separated by |
      ForEachEntry(table.Cell(row, "olderFrames"), "olderFrames",
                   [&frame](const std::string_view entry)
                   {
                     const std::size_t at = entry.find('@');
                     if (at == std::string_view::npos || at == 0)
                     {
                       throw DataFormatError("Invalid olderFrames entry '" + std::string(entry) + "'");
                     }
                     frame.OlderFrames.push_back(
                       {Csv::ParseInteger<uint64_t>(entry.substr(0, at)), Csv::ParseNanosecondTickCount(entry.substr(at + 1))});
                   });
      frame.MainMarkerFirstSeenTime = Csv::OptionalNanosecondTickCount(table.Cell(row, "mainMarkerFirstSeenNs"));
      frame.ScanoutDelay = Csv::OptionalNanosecondTimeSpan(table.Cell(row, "scanoutDelayNs"));
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
