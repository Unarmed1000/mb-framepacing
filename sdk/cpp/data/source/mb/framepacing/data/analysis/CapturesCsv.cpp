// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/CaptureCsvRow.hpp>
#include <mb/framepacing/data/analysis/CapturesCsv.hpp>
#include <charconv>
#include <cstddef>
#include <string>
#include <string_view>
#include "detail/CsvCells.hpp"
#include "detail/CsvTable.hpp"

namespace MB::FramePacing::Data
{
  namespace
  {
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

  namespace
  {
    CaptureCsvRow ToCapture(const Csv::CsvTable& table, const std::vector<std::string>& row)
    {
      CaptureCsvRow capture;
      capture.CaptureIndex = Csv::ParseInteger<int64_t>(table.Cell(row, "captureIndex"));
      capture.CaptureTime = Csv::OptionalTickCount64(table.Cell(row, "captureTicks"));
      capture.CaptureStatus = std::string(table.Cell(row, "status"));
      if (const std::string_view kind = table.Cell(row, "kind"); !kind.empty())
      {
        capture.Kind = std::string(kind);
      }
      capture.RunId = Csv::OptionalInteger<uint32_t>(table.Cell(row, "runId"));
      capture.FrameIndex = Csv::OptionalInteger<uint64_t>(table.Cell(row, "frameIndex"));
      capture.AnimationTime = Csv::OptionalTimeSpan(table.Cell(row, "animationTicks"));
      capture.SourceDropsBefore = Csv::OptionalInteger<int64_t>(table.Cell(row, "sourceDropsBefore")).value_or(0);
      capture.MissedBefore = Csv::OptionalInteger<int64_t>(table.Cell(row, "missedBefore")).value_or(0);
      capture.SyncRunId = Csv::OptionalInteger<uint32_t>(table.Cell(row, "syncRunId"));
      capture.SyncFrameIndex = Csv::OptionalInteger<uint64_t>(table.Cell(row, "syncFrameIndex"));
      capture.HostTime = Csv::OptionalTickCount64(table.Cell(row, "hostTicks"));
      capture.DeviceTime = Csv::OptionalTickCount64(table.Cell(row, "deviceTicks"));
      capture.Payload = FromHex(table.Cell(row, "payloadHex"));
      return capture;
    }
  }

  std::vector<CaptureCsvRow> ReadCaptures(const std::filesystem::path& path)
  {
    const Csv::CsvTable table = Csv::CsvTable::Read(path);
    std::vector<CaptureCsvRow> captures;
    captures.reserve(table.Rows.size());
    for (std::size_t i = 0; i < table.Rows.size(); ++i)
    {
      try
      {
        captures.push_back(ToCapture(table, table.Rows[i]));
      }
      catch (const DataFormatError& error)
      {
        throw table.InRow(i, error);
      }
    }
    return captures;
  }
}
