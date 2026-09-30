// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/CaptureCsvRow.hpp>
#include <mb/framepacing/data/analysis/CapturesCsv.hpp>
#include <charconv>
#include <string>
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

  std::vector<CaptureCsvRow> ReadCaptures(const std::filesystem::path& path)
  {
    const Detail::CsvTable table = Detail::CsvTable::Read(path);
    std::vector<CaptureCsvRow> captures;
    captures.reserve(table.Rows.size());
    for (const auto& row : table.Rows)
    {
      CaptureCsvRow capture;
      capture.CaptureIndex = Detail::ParseInteger<int64_t>(table.Cell(row, "captureIndex"));
      capture.CaptureTicks = Detail::OptionalTicks(table.Cell(row, "captureMs"));
      capture.Status = std::string(table.Cell(row, "status"));
      if (const std::string_view kind = table.Cell(row, "kind"); !kind.empty())
      {
        capture.Kind = std::string(kind);
      }
      capture.RunId = Detail::OptionalInteger<uint32_t>(table.Cell(row, "runId"));
      capture.FrameIndex = Detail::OptionalInteger<uint64_t>(table.Cell(row, "frameIndex"));
      capture.AnimationTicks = Detail::OptionalTicks(table.Cell(row, "animationMs"));
      capture.SourceDropsBefore = Detail::OptionalInteger<int64_t>(table.Cell(row, "sourceDropsBefore")).value_or(0);
      capture.MissedBefore = Detail::OptionalInteger<int64_t>(table.Cell(row, "missedBefore")).value_or(0);
      capture.SyncRunId = Detail::OptionalInteger<uint32_t>(table.Cell(row, "syncRunId"));
      capture.SyncFrameIndex = Detail::OptionalInteger<uint64_t>(table.Cell(row, "syncFrameIndex"));
      capture.HostTicks = Detail::OptionalTicks(table.Cell(row, "hostMs"));
      capture.DeviceTicks = Detail::OptionalTicks(table.Cell(row, "deviceMs"));
      capture.Payload = FromHex(table.Cell(row, "payloadHex"));
      captures.push_back(std::move(capture));
    }
    return captures;
  }
}
