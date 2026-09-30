#ifndef MB_FRAMEPACING_DATA_ANALYSISFILES_HPP
#define MB_FRAMEPACING_DATA_ANALYSISFILES_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>

namespace MB::FramePacing::Data
{
  //! The analysis output folder of a folder: the folder itself, or its analysis folder; empty when neither holds summary.json.
  std::optional<std::filesystem::path> FindAnalysis(const std::filesystem::path& folder);

  //! "run-<id>", and "run-<id>-<n>" for the n-th run with the same id (ordinal counts from 0).
  std::string RunFilePrefix(uint32_t runId, int32_t ordinal = 0);

  //! A run's frames CSV name. summary.json names it (SummaryRun::FramesFile): prefer that name.
  std::string FramesFileName(uint32_t runId, int32_t ordinal = 0);
}

#endif
