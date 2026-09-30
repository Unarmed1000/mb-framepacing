#ifndef MB_FRAMEPACING_DATA_HPP
#define MB_FRAMEPACING_DATA_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB::FramePacing::Data - the data module: reads the data of the mb-framepacing tools, the capture data (captures.mbcd,
// doc/capture-data-format.md) and the analysis output (summary.json, captures.csv and run-<id>-frames.csv, doc/analysis-output-format.md).
//
// This is the header to include: it pulls in every type (one header per type), the marker module (Marker.hpp, which decodes the stored
// markers, and the core) and declares the functions. Reading allocates and throws (DataFormatError for content it cannot read); it is not
// meant for a per-frame path.

#include <mb/framepacing/Marker.hpp>
#include <mb/framepacing/data/AnalysisSummary.hpp>
#include <mb/framepacing/data/CaptureCsvRow.hpp>
#include <mb/framepacing/data/CaptureDataHeader.hpp>
#include <mb/framepacing/data/CaptureDataReader.hpp>
#include <mb/framepacing/data/CaptureDataRecord.hpp>
#include <mb/framepacing/data/CaptureDataStatus.hpp>
#include <mb/framepacing/data/Constants.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/FrameRow.hpp>
#include <mb/framepacing/data/MarkerLocation.hpp>
#include <mb/framepacing/data/OlderFrame.hpp>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace MB::FramePacing::Data
{
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
