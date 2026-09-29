#ifndef MB_FRAMEPACINGDATA_CONSTANTS_HPP
#define MB_FRAMEPACINGDATA_CONSTANTS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstddef>
#include <cstdint>
#include <limits>
#include <string_view>

namespace MB::FramePacingData
{
  //! captures.mbcd: its file name in a capture folder, its format version, and the header and record sizes (doc/capture-data-format.md).
  inline constexpr std::string_view CaptureDataFileName = "captures.mbcd";
  inline constexpr uint32_t CaptureDataMagic = 0x4443424Du;    // "MBCD" little endian
  inline constexpr uint16_t CaptureDataFormatVersion = 1;
  inline constexpr std::size_t CaptureDataHeaderSize = 256;
  inline constexpr std::size_t CaptureDataRecordSize = 192;
  inline constexpr std::size_t MaxMarkerLocations = 4;
  //! Two equal slots: either can hold any marker payload (the longest, a start marker, is 77 bytes).
  inline constexpr std::size_t MainMarkerCapacity = 80;
  inline constexpr std::size_t SecondMarkerCapacity = 80;

  //! The marker's target and preferred frame time of an application that presents only when something changes (429496.7295 ms).
  inline constexpr int64_t OnDemandFrameTicks = 0xFFFF'FFFF;

  //! A device timestamp the capture source did not give.
  inline constexpr int64_t UnknownTicks = std::numeric_limits<int64_t>::min();

  //! The analysis output (doc/analysis-output-format.md): the folder inside a capture folder, its files, and its format version.
  inline constexpr std::string_view AnalysisDirectoryName = "analysis";
  inline constexpr std::string_view SummaryFileName = "summary.json";
  inline constexpr std::string_view CapturesFileName = "captures.csv";
  inline constexpr int32_t AnalysisFormatVersion = 1;

  //! Times are 100 ns ticks (TimeSpan ticks).
  inline constexpr int64_t TicksPerMillisecond = 10'000;
}

#endif
