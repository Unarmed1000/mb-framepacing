// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/data/analysis/AnalysisFiles.hpp>

namespace MB::FramePacing::Data
{
  std::optional<std::filesystem::path> FindAnalysis(const std::filesystem::path& folder)
  {
    if (std::filesystem::is_regular_file(folder / SummaryFileName))
    {
      return folder;
    }
    auto analysis = folder / AnalysisDirectoryName;
    if (std::filesystem::is_regular_file(analysis / SummaryFileName))
    {
      return analysis;
    }
    return std::nullopt;
  }

  std::string RunFilePrefix(const uint32_t runId, const int32_t ordinal)
  {
    return ordinal == 0 ? "run-" + std::to_string(runId) : "run-" + std::to_string(runId) + "-" + std::to_string(ordinal + 1);
  }

  std::string FramesFileName(const uint32_t runId, const int32_t ordinal)
  {
    return RunFilePrefix(runId, ordinal) + "-frames.csv";
  }
}
