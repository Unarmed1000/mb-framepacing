//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The golden data (test-data/pacer, written by the C++ pacer-sim): every golden scenario paced with its rules must give exactly the file's bytes,
//* so the C# pacer decides and plans as the C++ one to the tick. On top, as the C++ tests: Swappy's rule reproduces mb-framepacing-explained's
//* simulation of it, the pacer at a fixed swap interval shows every frame of the full-rate clip on its refresh, and the late count fix never slows
//* down later than Swappy's rule.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Globalization;
using System.IO;
using System.Linq;
using MB.FramePacing.Pacer.UnitTest.Simulation;
using NUnit.Framework;

namespace MB.FramePacing.Pacer.UnitTest
{
  [TestFixture]
  public class GoldenTests
  {
    [Test]
    public void EveryScenario_GivesItsFileExactly()
    {
      string testData = TestData.PacerDirectory;
      foreach (Scenario scenario in PacerSimulation.GoldenScenarios(testData))
      {
        foreach (SlowDownRule rule in PacerSimulation.RulesFor(scenario))
        {
          string file = Path.Combine(testData, PacerSimulation.ResultFileName(scenario, rule));
          Assert.That(File.Exists(file), Is.True, $"{file}: run python tools/update_pacer_test_data.py");
          Assert.That(PacerSimulation.Simulate(scenario, rule), Is.EqualTo(File.ReadAllText(file)), file);
        }
      }
    }

    [Test]
    public void SwappysRule_ReproducesTheSisterRepositorysSimulation()
    {
      Row[] rows = Parse(PacerSimulation.Simulate(Named("60-busy"), SlowDownRule.FullWindow));
      Assert.That(CompareWithReference(rows), Is.EqualTo(336));
    }

    [Test]
    public void AtAFixedSwapInterval_ThePacerShowsTheFullRateClipsFrames()
    {
      Scenario fullRate = Named("60-busy-full-rate");
      Assert.That(fullRate.AutoSwapInterval, Is.False);
      Row[] rows = Parse(PacerSimulation.Simulate(fullRate, SlowDownRule.LateCount));
      Assert.That(CompareWithReference(rows), Is.EqualTo(388));
      // The busy stretch misses refreshes: the clip shows 92 frames a refresh late
      Assert.That(rows.Count(row => row.Late), Is.EqualTo(92));
    }

    [Test]
    public void TheLateCountFix_SlowsDownNoLaterThanSwappysRule()
    {
      foreach (Scenario scenario in PacerSimulation.GoldenScenarios(TestData.PacerDirectory).Where(scenario => scenario.AutoSwapInterval))
      {
        Row[] swappy = Parse(PacerSimulation.Simulate(scenario, SlowDownRule.FullWindow));
        Row[] fix = Parse(PacerSimulation.Simulate(scenario, SlowDownRule.LateCount));
        Assert.That(FirstSlower(fix), Is.GreaterThanOrEqualTo(0), scenario.Name);
        Assert.That(FirstSlower(fix), Is.LessThanOrEqualTo(FirstSlower(swappy)), scenario.Name);
        Assert.That(fix.Count(row => row.Late), Is.LessThanOrEqualTo(swappy.Count(row => row.Late)), scenario.Name);
      }
      // The staged load slows down step after step: there the fix does not wait a full window after each step
      Scenario stages = Named("100-stages");
      Assert.That(
        Parse(PacerSimulation.Simulate(stages, SlowDownRule.LateCount)).Count(row => row.Late),
        Is.LessThan(Parse(PacerSimulation.Simulate(stages, SlowDownRule.FullWindow)).Count(row => row.Late))
      );
    }

    private static Scenario Named(string name) => PacerSimulation.GoldenScenarios(TestData.PacerDirectory).Single(scenario => scenario.Name == name);

    private static Row[] Parse(string text) =>
      text.Split('\n')
        .Skip(1)
        .Where(line => line.Length > 0)
        .Select(line => line.Split(','))
        .Select(fields => new Row(
          Number(fields[0]),
          Number(fields[3]),
          fields[4] == "1",
          Number(fields[5]),
          fields[6],
          Number(fields[11]),
          Number(fields[12])
        ))
        .ToArray();

    private static long Number(string text) => long.Parse(text, CultureInfo.InvariantCulture);

    private static long FirstSlower(Row[] rows) => rows.FirstOrDefault(row => row.Change == "Slower")?.Frame ?? -1;

    // Every frame with a reference has the reference's swap interval and is shown on its refresh (the clip counts refreshes from its own start, so
    // the offset is the first frame's). Returns the frames compared.
    private static int CompareWithReference(Row[] rows)
    {
      long? offset = null;
      int compared = 0;
      foreach (Row row in rows.Where(row => row.ReferenceSwapInterval != 0))
      {
        Assert.That(row.SwapInterval, Is.EqualTo(row.ReferenceSwapInterval), $"frame {row.Frame}");
        offset ??= row.ShownRefresh - row.ReferenceShownRefresh;
        Assert.That(row.ShownRefresh - row.ReferenceShownRefresh, Is.EqualTo(offset), $"frame {row.Frame}");
        ++compared;
      }
      return compared;
    }

    private sealed record Row(
      long Frame,
      long ShownRefresh,
      bool Late,
      long SwapInterval,
      string Change,
      long ReferenceSwapInterval,
      long ReferenceShownRefresh
    );
  }
}
