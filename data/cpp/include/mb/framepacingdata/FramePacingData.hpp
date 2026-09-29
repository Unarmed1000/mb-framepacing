#ifndef MB_FRAMEPACINGDATA_FRAMEPACINGDATA_HPP
#define MB_FRAMEPACINGDATA_FRAMEPACINGDATA_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB::FramePacingData - reads the data of the mb-framepacing tools: the capture data (captures.mbcd, doc/capture-data-format.md) and the
// analysis output (summary.json, captures.csv and run-<id>-frames.csv, doc/analysis-output-format.md).
//
// This is the header to include: it pulls in every type (one header per type) and declares the functions. Reading allocates and throws
// (DataFormatError for content it cannot read); it is not meant for a per-frame path.

#include <mb/framepacingdata/AnalysisSummary.hpp>
#include <mb/framepacingdata/CaptureCsvRow.hpp>
#include <mb/framepacingdata/CaptureDataHeader.hpp>
#include <mb/framepacingdata/CaptureDataReader.hpp>
#include <mb/framepacingdata/CaptureDataRecord.hpp>
#include <mb/framepacingdata/CaptureDataStatus.hpp>
#include <mb/framepacingdata/CaptureRecordFlags.hpp>
#include <mb/framepacingdata/Constants.hpp>
#include <mb/framepacingdata/DataFormatError.hpp>
#include <mb/framepacingdata/DataRect.hpp>
#include <mb/framepacingdata/FrameRow.hpp>
#include <mb/framepacingdata/LibraryVersion.hpp>
#include <mb/framepacingdata/MarkerLocation.hpp>
#include <mb/framepacingdata/OlderFrame.hpp>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace MB::FramePacingData
{
  //! The linked library's version. Version.hpp has it at compile time; this header does not include it, so a version bump does not
  //! rebuild every file that includes the library.
  LibraryVersion GetLibraryVersion() noexcept;

  //! Read summary.json. Throws DataFormatError for a newer format version or content that is not a summary.
  AnalysisSummary ReadSummary(const std::filesystem::path& path);

  //! Parse summary.json's text. A file without formatVersion is format 1; fields it lacks keep their defaults, unknown fields are ignored.
  AnalysisSummary ParseSummary(std::string_view json);

  //! A run's frames CSV (its name is in summary.json: SummaryRun::FramesFile). Columns are found by name.
  std::vector<FrameRow> ReadFrames(const std::filesystem::path& path);

  //! captures.csv. Columns are found by name.
  std::vector<CaptureCsvRow> ReadCaptures(const std::filesystem::path& path);

  //! The analysis output folder of a folder: the folder itself, or its analysis folder; empty when neither holds summary.json.
  std::optional<std::filesystem::path> FindAnalysis(const std::filesystem::path& folder);

  //! "run-<id>", and "run-<id>-<n>" for the n-th run with the same id (ordinal counts from 0).
  std::string RunFilePrefix(uint32_t runId, int32_t ordinal = 0);

  //! A run's frames CSV name. summary.json names it (SummaryRun::FramesFile): prefer that name.
  std::string FramesFileName(uint32_t runId, int32_t ordinal = 0);

  //! The 100 ns ticks of a CSV milliseconds text ("16.6667"): exact for the at most four decimals the files have, rounded to the nearest
  //! tick beyond that. Throws DataFormatError for text that is not a number.
  int64_t ParseTicks(std::string_view milliseconds);
}

#endif
