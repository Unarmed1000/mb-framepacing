// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Size probe: the marker's per-frame path plus the data module as a reader uses it: summary.json, the frames CSVs and captures.mbcd.
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/data/analysis/AnalysisFiles.hpp>
#include <mb/framepacing/data/analysis/AnalysisSummary.hpp>
#include <mb/framepacing/data/analysis/FramesCsv.hpp>
#include <mb/framepacing/data/capture/CaptureDataReader.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <array>
#include <cstdint>
#include <cstdio>
#include <exception>
#include <filesystem>
#include <span>

namespace FP = MB::FramePacing;
namespace FD = MB::FramePacing::Data;
namespace FM = MB::FramePacing::Marker;

namespace
{
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> g_vertices{};

  std::size_t ReadAnalysis(const std::filesystem::path& captureFolder)
  {
    std::size_t count = 0;
    const auto analysis = FD::FindAnalysis(captureFolder);
    if (analysis)
    {
      const FD::AnalysisSummary summary = FD::ReadSummary(*analysis / FD::SummaryFileName);
      for (const FD::SummaryRun& run : summary.Runs)
      {
        count += FD::ReadFrames(*analysis / run.FramesFile).size();
      }
    }
    FD::CaptureDataReader reader(captureFolder / FD::CaptureDataReader::FileName);
    for (const FD::CaptureDataRecord& record : reader.ReadAll())
    {
      FM::Payload payload;
      count += record.TryDecodeMain(payload) ? 1u : 0u;
    }
    return count;
  }
}

int main(int argc, char* argv[])
{
  const std::span<char* const> arguments(argv, static_cast<std::size_t>(argc));
  const FM::Payload payload(FM::MarkerKind::Frame, 1u, static_cast<uint64_t>(argc), FM::MarkerFlags::NoFlags,
                            FP::NanosecondTimeSpan::FromSeconds(argc / 60.0));
  FM::ModuleMatrix matrix;
  std::size_t count = 0;
  if (FM::GenerateModules(payload, matrix))
  {
    count = FM::ModulesToTriangles(matrix, FM::Options{}, {32, 32}, g_vertices);
  }
  try
  {
    if (arguments.size() > 1)
    {
      count += ReadAnalysis(arguments[1]);
    }
  }
  catch (const std::exception& ex)
  {
    std::printf("%s\n", ex.what());
    return 1;
  }
  std::printf("%zu %s\n", count, arguments[0]);
  return 0;
}
