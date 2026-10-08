// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The golden data (test-data/pacer, written by pacer-sim --golden, python tools/update_pacer_test_data.py): every golden scenario paced
// with its rules must give exactly the file's bytes (a port's tests compare with the same files). On top: the full-window rule reproduces the
// swap intervals and display refreshes of mb-framepacing-explained's simulation of it, frame by frame, the pacer at a fixed swap interval
// shows every frame of the full-rate clip on its refresh, and the late count fix never slows down later than the full-window rule.
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>
#include "PacerSimulation.hpp"
#include "TierLoopGolden.hpp"
#include "TierLoopGoldenRun.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;

namespace
{
  //! One row of a result (Sim::ResultHeader)
  struct ResultRow
  {
    int64_t Frame{0};
    int64_t TargetRefresh{0};
    int64_t ShownRefresh{0};
    bool Late{false};
    int64_t SwapInterval{0};
    std::string Change;
    int64_t ReferenceSwapInterval{0};
    int64_t ReferenceShownRefresh{-1};
  };

  std::optional<std::filesystem::path> FindTestData()
  {
    for (auto folder = std::filesystem::path(MB_FRAMEPACING_PACER_SOURCE_DIR); !folder.empty(); folder = folder.parent_path())
    {
      auto candidate = folder / "test-data" / "pacer";
      if (std::filesystem::exists(candidate / "60-busy-frames.csv"))
      {
        return candidate;
      }
      if (folder == folder.parent_path())
      {
        break;
      }
    }
    return std::nullopt;
  }

  std::string ReadText(const std::filesystem::path& path)
  {
    const std::ifstream file(path, std::ios::binary);
    std::stringstream text;
    text << file.rdbuf();
    return text.str();
  }

  std::vector<ResultRow> Parse(const std::string& text)
  {
    std::vector<ResultRow> rows;
    std::stringstream lines(text);
    std::string line;
    std::getline(lines, line);
    while (std::getline(lines, line))
    {
      std::vector<std::string> fields;
      std::stringstream stream(line);
      std::string field;
      while (std::getline(stream, field, ','))
      {
        fields.push_back(field);
      }
      rows.push_back({std::stoll(fields[0]), std::stoll(fields[2]), std::stoll(fields[3]), fields[4] == "1", std::stoll(fields[5]), fields[6],
                      std::stoll(fields[11]), std::stoll(fields[12])});
    }
    return rows;
  }

  // The name by value: GCC takes a reference returned from a call with a temporary argument for one into that temporary
  const Sim::Scenario& Named(const std::vector<Sim::Scenario>& scenarios, const std::string_view name)
  {
    return *std::find_if(scenarios.begin(), scenarios.end(), [name](const Sim::Scenario& scenario) { return scenario.Name == name; });
  }

  int64_t LateFrames(const std::vector<ResultRow>& rows)
  {
    return std::count_if(rows.begin(), rows.end(), [](const ResultRow& row) { return row.Late; });
  }

  int64_t FirstSlower(const std::vector<ResultRow>& rows)
  {
    const auto slower = std::find_if(rows.begin(), rows.end(), [](const ResultRow& row) { return row.Change == "Slower"; });
    return slower != rows.end() ? slower->Frame : -1;
  }

  //! Every frame with a reference has the reference's swap interval and is shown on its refresh (the clip counts refreshes from its own
  //! start, so the offset is the first frame's). Returns the frames compared.
  int64_t CompareWithReference(const std::vector<ResultRow>& rows)
  {
    std::optional<int64_t> offset;
    int64_t compared = 0;
    for (const ResultRow& row : rows)
    {
      if (row.ReferenceSwapInterval == 0)
      {
        continue;
      }
      EXPECT_EQ(row.SwapInterval, row.ReferenceSwapInterval) << "frame " << row.Frame;
      if (!offset)
      {
        offset = row.ShownRefresh - row.ReferenceShownRefresh;
      }
      EXPECT_EQ(row.ShownRefresh - row.ReferenceShownRefresh, *offset) << "frame " << row.Frame;
      ++compared;
    }
    return compared;
  }
}

TEST(Golden, EveryScenarioGivesItsFileExactly)
{
  const auto testData = FindTestData();
  if (!testData)
  {
    GTEST_SKIP() << "test-data/pacer not found (a copy of the library outside mb-framepacing)";
  }
  for (const Sim::Scenario& scenario : Sim::GoldenScenarios(*testData))
  {
    for (const PC::SlowDownRule rule : Sim::RulesFor(scenario))
    {
      const std::filesystem::path file = *testData / Sim::ResultFileName(scenario, rule);
      ASSERT_TRUE(std::filesystem::exists(file)) << file.string() << ": run python tools/update_pacer_test_data.py";
      EXPECT_EQ(Sim::Simulate(scenario, rule), ReadText(file)) << file.string();
    }
  }
}

TEST(Golden, TheFullWindowRuleReproducesTheSisterRepositorysSimulation)
{
  const auto testData = FindTestData();
  if (!testData)
  {
    GTEST_SKIP() << "test-data/pacer not found (a copy of the library outside mb-framepacing)";
  }
  const std::vector<Sim::Scenario> scenarios = Sim::GoldenScenarios(*testData);
  const std::vector<ResultRow> rows = Parse(Sim::Simulate(Named(scenarios, "60-busy"), PC::SlowDownRule::FullWindow));
  EXPECT_EQ(CompareWithReference(rows), 336);
}

TEST(Golden, AtAFixedSwapIntervalThePacerShowsTheFullRateClipsFrames)
{
  const auto testData = FindTestData();
  if (!testData)
  {
    GTEST_SKIP() << "test-data/pacer not found (a copy of the library outside mb-framepacing)";
  }
  const std::vector<Sim::Scenario> scenarios = Sim::GoldenScenarios(*testData);
  const Sim::Scenario& fullRate = Named(scenarios, "60-busy-full-rate");
  ASSERT_FALSE(fullRate.AutoSwapInterval);
  const std::vector<ResultRow> rows = Parse(Sim::Simulate(fullRate, PC::SlowDownRule::LateCount));
  EXPECT_EQ(CompareWithReference(rows), 388);
  // The busy stretch misses refreshes: the clip shows 92 frames a refresh late
  EXPECT_EQ(LateFrames(rows), 92);
}

TEST(Golden, TheLateCountFixSlowsDownNoLaterThanTheFullWindowRule)
{
  const auto testData = FindTestData();
  if (!testData)
  {
    GTEST_SKIP() << "test-data/pacer not found (a copy of the library outside mb-framepacing)";
  }
  const std::vector<Sim::Scenario> scenarios = Sim::GoldenScenarios(*testData);
  for (const Sim::Scenario& scenario : scenarios)
  {
    if (!scenario.AutoSwapInterval)
    {
      continue;
    }
    const std::vector<ResultRow> fullWindow = Parse(Sim::Simulate(scenario, PC::SlowDownRule::FullWindow));
    const std::vector<ResultRow> fix = Parse(Sim::Simulate(scenario, PC::SlowDownRule::LateCount));
    EXPECT_GE(FirstSlower(fix), 0) << scenario.Name;
    EXPECT_LE(FirstSlower(fix), FirstSlower(fullWindow)) << scenario.Name;
    EXPECT_LE(LateFrames(fix), LateFrames(fullWindow)) << scenario.Name;
  }
  // The staged load slows down step after step: there the fix does not wait a full window after each step
  const Sim::Scenario& stages = Named(scenarios, "100-stages");
  EXPECT_LT(LateFrames(Parse(Sim::Simulate(stages, PC::SlowDownRule::LateCount))),
            LateFrames(Parse(Sim::Simulate(stages, PC::SlowDownRule::FullWindow))));
}

// The tier pacer's simulated loop (test-data/pacer/tier-loops.csv and tier-loop-*.csv): every way of pacing that has a pacer,
// both aims and a list of cases, on the display model. A change in how a frame is paced shows here as a line of the digest
// file that differs, and for the runs that are written whole as the frames that differ.

TEST(Golden, EveryRunOfTheTierPacersLoopGivesTheBytesOfItsGoldenFile)
{
  const std::optional<std::filesystem::path> folder = FindTestData();
  if (!folder.has_value())
  {
    GTEST_SKIP() << "test-data/pacer not found";
  }
  const std::vector<Sim::TierLoopGoldenRun> runs = Sim::TierLoopGolden::Runs();
  // Four ways of pacing, two aims, fifteen cases
  ASSERT_EQ(runs.size(), 120u);
  uint32_t whole = 0;
  for (const Sim::TierLoopGoldenRun& run : runs)
  {
    if (run.WritesFrames)
    {
      ++whole;
      const std::string expected = ReadText(*folder / Sim::TierLoopGolden::FileNameOf(run));
      ASSERT_FALSE(expected.empty()) << run.Name;
      EXPECT_TRUE(Sim::TierLoopGolden::Simulate(run) == expected) << run.Name;
    }
  }
  EXPECT_EQ(whole, 8u);

  // The digest file, line by line, so that a difference names its run
  const std::string expected = ReadText(*folder / Sim::TierLoopGolden::DigestFileName);
  const std::string digests = Sim::TierLoopGolden::Digests(runs);
  std::stringstream expectedLines(expected);
  std::stringstream lines(digests);
  std::string expectedLine;
  std::string line;
  uint32_t count = 0;
  while (std::getline(lines, line))
  {
    ASSERT_TRUE(static_cast<bool>(std::getline(expectedLines, expectedLine))) << line;
    EXPECT_EQ(line, expectedLine);
    ++count;
  }
  EXPECT_EQ(count, 121u);
  EXPECT_TRUE(digests == expected);
}
