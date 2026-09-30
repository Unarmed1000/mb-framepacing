//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame model of mb-framepacing-explained's simulations (tools/frame_pacing_video/adaptive_rate.py), as the C++ tests have it: a frame starts
//* when the previous one is shown, works its work time, and is shown at the refresh it targets, or at the first refresh after it is done when it is
//* done too late. The pacer paces it through its public API (BeginFrame, EndFrame; no display-time feedback: it infers the display as the model
//* shows it). The results must be the golden data's bytes (sdk/test-data/pacer, which the C++ pacer-sim writes).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MB.FramePacing.Pacer.UnitTest.Simulation
{
  internal static class PacerSimulation
  {
    /// <summary>The first frame starts at 1 s on the steady clock, on a refresh (refresh 0 of the results).</summary>
    public const long StartTicks = TimeSpan.TicksPerSecond;

    /// <summary>The columns of a result: one row per frame.</summary>
    public const string ResultHeader =
      "frame,workTicks,targetRefresh,shownRefresh,late,swapInterval,change,intendedDisplayTicks,animationTicks,windowFrames,windowLateFrames,"
      + "referenceSwapInterval,referenceShownRefresh";

    /// <summary>A scenario's frames from a CSV file with the columns workTicks, referenceSwapInterval and referenceShownRefresh.</summary>
    public static ScenarioFrame[] ReadFrames(string path)
    {
      string[] lines = File.ReadAllText(path).Replace("\r\n", "\n").TrimStart('﻿').Split('\n');
      if (lines[0] != "workTicks,referenceSwapInterval,referenceShownRefresh")
        throw new InvalidDataException($"{path}: not a scenario frames file");
      var frames = new List<ScenarioFrame>();
      for (int index = 1; index < lines.Length; ++index)
      {
        if (lines[index].Length == 0)
          continue;
        string[] fields = lines[index].Split(',');
        if (fields.Length != 3)
          throw new InvalidDataException($"{path}: a row without three fields");
        frames.Add(
          new ScenarioFrame(
            long.Parse(fields[0], CultureInfo.InvariantCulture),
            int.Parse(fields[1], CultureInfo.InvariantCulture),
            long.Parse(fields[2], CultureInfo.InvariantCulture)
          )
        );
      }
      return frames.ToArray();
    }

    /// <summary>The golden scenarios, as the C++ tests have them: 60-busy, 60-busy-full-rate, 100-stages and 60-relapse.</summary>
    public static Scenario[] GoldenScenarios(string testDataPacer)
    {
      // The busy stretch of mb-framepacing-explained's 60-busy-swappy clip, frame by frame, played twice as its simulation does (so the clip starts
      // in the state it ends in); its swap intervals and refreshes are the reference
      var busy = new Scenario
      {
        Name = "60-busy",
        RateNumerator = 60,
        Frames = ReadFrames(Path.Combine(testDataPacer, "60-busy-frames.csv")),
        Passes = 2,
      };
      // The 60-busy-full-rate clip: presented at every refresh without a pacer that adapts
      var fullRate = new Scenario
      {
        Name = "60-busy-full-rate",
        RateNumerator = 60,
        Frames = ReadFrames(Path.Combine(testDataPacer, "60-busy-full-rate-frames.csv")),
        AutoSwapInterval = false,
      };
      // mb-framepacing-explained's 100 Hz chart: calm 5 to 8 ms frames, and a load that rises one refresh at a time from 1.5 s and falls back
      // until 20.5 s, of 24 s
      var stages = new Scenario
      {
        Name = "100-stages",
        RateNumerator = 100,
        DurationTicks = Milliseconds(24_000),
        Seed = 20_260_930,
        Calm = new LoadStage(0, 0, Milliseconds(5), Milliseconds(8)),
        Stages = new[]
        {
          new LoadStage(Milliseconds(1_500), Milliseconds(5_500), Milliseconds(13), Milliseconds(17)),
          new LoadStage(Milliseconds(5_500), Milliseconds(9_500), Milliseconds(22), Milliseconds(27)),
          new LoadStage(Milliseconds(9_500), Milliseconds(13_500), Milliseconds(31), Milliseconds(37)),
          new LoadStage(Milliseconds(13_500), Milliseconds(17_000), Milliseconds(22), Milliseconds(27)),
          new LoadStage(Milliseconds(17_000), Milliseconds(20_500), Milliseconds(13), Milliseconds(17)),
        },
      };
      // The case mb-framepacing-explained gives for the late count fix: the busy stretch, and the load back again right after the rule has sped up
      var relapse = new Scenario
      {
        Name = "60-relapse",
        RateNumerator = 60,
        DurationTicks = Milliseconds(13_000),
        Seed = 20_261_001,
        Calm = new LoadStage(0, 0, Milliseconds(9), Milliseconds(13)),
        Stages = new[]
        {
          new LoadStage(Milliseconds(1_500), Milliseconds(5_500), Milliseconds(12), Milliseconds(24)),
          new LoadStage(Milliseconds(6_600), Milliseconds(10_600), Milliseconds(12), Milliseconds(24)),
        },
      };
      return new[] { busy, fullRate, stages, relapse };
    }

    /// <summary>The rules a scenario is paced with: both, or LateCount alone for a scenario at a fixed swap interval (no rule decides there).</summary>
    public static SlowDownRule[] RulesFor(Scenario scenario) =>
      scenario.AutoSwapInterval ? new[] { SlowDownRule.FullWindow, SlowDownRule.LateCount } : new[] { SlowDownRule.LateCount };

    /// <summary>The file name of a scenario's result with a rule: &lt;name&gt;-&lt;rule&gt;.csv, or &lt;name&gt;-Fixed.csv at a fixed swap interval.</summary>
    public static string ResultFileName(Scenario scenario, SlowDownRule rule) =>
      $"{scenario.Name}-{(scenario.AutoSwapInterval ? rule.ToString() : "Fixed")}.csv";

    /// <summary>Pace the scenario with the rule (the other settings at their defaults); the result as CSV text (ResultHeader, "\n" line ends).</summary>
    public static string Simulate(Scenario scenario, SlowDownRule rule)
    {
      var period = RefreshPeriod.FromRate(scenario.RateNumerator, scenario.RateDenominator);
      var pacer = new FramePacer(new PacerSettings(period) { SlowDown = rule, AutoSwapInterval = scenario.AutoSwapInterval });
      var clock = new AnimationClock(period);
      var random = new SplitMix64(scenario.Seed);

      long passFrames = scenario.Frames.Length;
      long listFrames = passFrames * Math.Max(scenario.Passes, 1);
      var output = new StringBuilder();
      output.Append(ResultHeader).Append('\n');
      long now = StartTicks;
      for (long frame = 0; ; ++frame)
      {
        ScenarioFrame source;
        if (passFrames > 0)
        {
          if (frame >= listFrames)
            break;
          source = scenario.Frames[frame % passFrames];
          // The reference describes the last pass only
          if (frame < listFrames - passFrames)
            source = new ScenarioFrame(source.WorkTicks, 0, -1);
        }
        else
        {
          long elapsed = now - StartTicks;
          if (elapsed >= scenario.DurationTicks)
            break;
          LoadStage stage = scenario.Calm;
          foreach (LoadStage candidate in scenario.Stages)
          {
            if (candidate.FromTicks <= elapsed && elapsed < candidate.ToTicks)
            {
              stage = candidate;
              break;
            }
          }
          source = new ScenarioFrame(Draw(random, stage), 0, -1);
        }

        FrameSchedule schedule = pacer.BeginFrame(new FrameInput(now));
        WindowState window = pacer.Window;
        long target = period.NearestRefreshes(schedule.IntendedDisplayTicks - StartTicks);
        long done = now + source.WorkTicks;
        long shown = Math.Max(target, FirstRefreshAtOrAfter(period, done - StartTicks));
        pacer.EndFrame(new FrameEnd(done, source.WorkTicks));
        AnimationTime animation = clock.Advance(schedule);

        output
          .Append(Invariant(frame))
          .Append(',')
          .Append(Invariant(source.WorkTicks))
          .Append(',')
          .Append(Invariant(target))
          .Append(',')
          .Append(Invariant(shown))
          .Append(',')
          .Append(shown > target ? '1' : '0')
          .Append(',')
          .Append(Invariant(schedule.SwapInterval))
          .Append(',')
          .Append(schedule.Change.ToString())
          .Append(',')
          .Append(Invariant(schedule.IntendedDisplayTicks))
          .Append(',')
          .Append(Invariant(animation.AnimationTicks))
          .Append(',')
          .Append(Invariant(window.Frames))
          .Append(',')
          .Append(Invariant(window.LateFrames))
          .Append(',')
          .Append(Invariant(source.ReferenceSwapInterval))
          .Append(',')
          .Append(Invariant(source.ReferenceShownRefresh))
          .Append('\n');
        // The next frame starts when this one is shown
        now = StartTicks + period.TicksFor(shown);
      }
      return output.ToString();
    }

    private static long Milliseconds(long milliseconds) => milliseconds * TimeSpan.TicksPerMillisecond;

    private static long Draw(SplitMix64 random, LoadStage stage)
    {
      ulong range = (ulong)(stage.MaxWorkTicks - stage.MinWorkTicks + 1);
      return stage.MinWorkTicks + (long)(random.Next() % range);
    }

    // The first refresh at or after ticks (from refresh 0 at 0)
    private static long FirstRefreshAtOrAfter(RefreshPeriod period, long ticks)
    {
      long refresh = period.FloorRefreshes(ticks);
      return period.TicksFor(refresh) < ticks ? refresh + 1 : refresh;
    }

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
  }
}
