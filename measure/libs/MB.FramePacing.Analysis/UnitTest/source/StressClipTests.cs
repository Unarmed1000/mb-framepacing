//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The stress clips (a fixed script of lost frames: 15, 30, 40 and 60 Hz pacing on a 60 Hz display, displays of 50, 120 and 144 Hz) against a table
//* written by hand, scenario by scenario. ClipManifest mirrors the analysis's rules, so it can only find a frame the analysis read differently;
//* this table says what each scenario must give, so it can find a wrong rule. Never fill it in from what the tools print.
//*
//* Per presented frame of a scenario, in refreshes, measured from the last shown frame: the animation step, the display step, the frame's target
//* in whole refreshes (a drop does not change it), and by hand whether it is late and how many frames were dropped just before it. From those:
//*   stop or rush = target - display step      the frame before stayed too long (a stop) or too short (a rush);
//*   step         = animation step - target    the animation moved too far or too short;
//*   sum          = animation step - display step = stop or rush + step, the animation error, PresentMon's number.
//* A frame counts as an animation error when either part is off. A step from a static frame has no step and no sum, and counts for nothing.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  [Category("ffmpeg")]
  [Category("slow")] // imports the stress clips through ffmpeg: CI runs it on one system
  public class StressClipTests
  {
    // The clips' times are rounded to the nanosecond once each, and a target is whole capture periods: a value of the table in nanoseconds is off by this at most
    private const double ToleranceNanoseconds = 8;

    /// <summary>A presented frame of a scenario, in refreshes: no display step on the run's first frame.</summary>
    private readonly record struct Row(double AnimationStep, long? DisplayStep, long Target, bool Late, long Dropped, bool Static)
    {
      public double? StopOrRush => DisplayStep is { } display ? Target - display : null;
      public double? Step => DisplayStep.HasValue && !Static ? AnimationStep - Target : null;
      public double? Sum => DisplayStep is { } display && !Static ? AnimationStep - display : null;
      public bool IsError => DisplayStep.HasValue && !Static && (StopOrRush != 0 || Step != 0);
    }

    private static readonly Row g_first = new Row(0, null, 0, false, 0, false);

    private static Row[] Rows(params object[] rows) => rows.SelectMany(row => row is Row[] many ? many : new[] { (Row)row }).ToArray();

    private static Row[] Repeat(int count, params Row[] rows) => Enumerable.Repeat(rows, count).SelectMany(r => r).ToArray();

    /// <summary>A frame of a scenario at target <paramref name="target"/>, in refreshes.</summary>
    private static Row F(double animationStep, long displayStep, long target, bool late = false, long dropped = 0) =>
      new Row(animationStep, displayStep, target, late, dropped, false);

    /// <summary>A step from a static frame.</summary>
    private static Row S(double animationStep, long displayStep, long target, long dropped = 0) =>
      new Row(animationStep, displayStep, target, false, dropped, true);

    private static Row On(long target) => F(target, target, target);

    /// <summary>The scenarios drawn at every target, in multiples of it. Rests differ in length, so the frames of a rest are given.</summary>
    private static Dictionary<string, Row[]> Common(long t, int stepsFromRest, int stepsToLastAtRest, int stepsAfterComingBackInRest)
    {
      Row Frame(double animationStep, long displayStep, bool late = false, long dropped = 0) => F(animationStep, displayStep, t, late, dropped);
      Row on = On(t);
      var scenarios = new Dictionary<string, Row[]>
      {
        ["23-dropped-first-frame"] = Rows(g_first),
        ["01-dropped"] = Rows(on, Frame(2 * t, 2 * t, dropped: 1)),
        ["02-dropped-run-2"] = Rows(on, Frame(3 * t, 3 * t, dropped: 2)),
        ["02-dropped-run-3"] = Rows(on, Frame(4 * t, 4 * t, dropped: 3)),
        ["02-dropped-run-4"] = Rows(on, Frame(5 * t, 5 * t, dropped: 4)),
        // The clock lost the frame: a step of one target over two targets on screen
        ["03-dropped-clock-lost"] = Rows(on, Frame(t, 2 * t, late: true, dropped: 1)),
        ["04-late-step-stays"] = Rows(on, Frame(t, 2 * t, late: true), on),
        ["05-late-catches-up"] = Rows(on, Frame(t, 2 * t, late: true), Frame(2 * t, t)),
        ["06-two-late"] = Rows(on, Frame(t, 2 * t, late: true), Frame(2 * t, 2 * t), Frame(2 * t, t)),
        ["07-dropped-then-refresh-lost"] = Rows(on, Frame(2 * t, 3 * t, late: true, dropped: 1), Frame(2 * t, t)),
        ["08-every-other-dropped"] = Rows(on, Repeat(15, Frame(2 * t, 2 * t, dropped: 1))),
        ["09-hitch-clock-running"] = Rows(on, Frame(10 * t, 10 * t)),
        ["09-hitch-clock-stopped"] = Rows(on, Frame(t, 10 * t, late: true)),
        // c arrives a target early and b comes back after it, so nothing but d is dropped; e holds until f is due
        ["10-out-of-order-next-to-drop"] = Rows(on, Frame(2 * t, t), Frame(2 * t, 2 * t, dropped: 1), Frame(t, 2 * t, late: true)),
        ["11-present-failed"] = Rows(on, Frame(2 * t, 2 * t, dropped: 1)),
        ["11-dropped-by-target"] = Rows(on, Frame(2 * t, 2 * t, dropped: 1)),
        // The step into a rest is judged, with its drop; the steps out of a static frame are not
        ["12-rest-first-frame-dropped"] = Rows(on, Frame(2 * t, 2 * t, dropped: 1), Repeat(stepsFromRest, S(t, t, t))),
        ["12-rest-last-frame-dropped"] = Rows(on, on, Repeat(stepsToLastAtRest, S(t, t, t)), S(2 * t, 2 * t, t, dropped: 1)),
        ["14-plan-moves"] = Rows(on, Frame(3 * t, 3 * t, dropped: 1), on),
        ["14-plan-keeps-grid"] = Rows(on, Frame(2 * t, 2 * t, dropped: 1), on),
        ["20-two-drops-close"] = Rows(on, Frame(2 * t, 2 * t, dropped: 1), on, Frame(2 * t, 2 * t, dropped: 1)),
        ["21-dropped-after-late"] = Rows(on, Frame(t, 2 * t, late: true), Frame(3 * t, 2 * t, dropped: 1)),
        ["22-dropped-before-late"] = Rows(on, Frame(2 * t, 2 * t, dropped: 1), Frame(t, 2 * t, late: true), Frame(2 * t, t)),
        ["23-dropped-last-frame"] = Rows(on),
        ["24-dropped-overshoots"] = Rows(on, Frame(3 * t, 2 * t, dropped: 1)),
        // A clock that stands still on frames on time is not late: they were on screen as long as intended
        ["25-animation-stands-still"] = Rows(on, Repeat(23, Frame(0, t))),
        ["25-animation-goes-backwards"] = Rows(on, on, Frame(-t, t), Frame(3 * t, t)),
        ["29-dropped-run-long"] = Rows(on, Frame(102 * t, 102 * t, dropped: 101)),
        ["29-hold-long"] = Rows(on, Frame(120, 120)),
        ["30-present-failed-in-run"] = Rows(on, Frame(4 * t, 4 * t, dropped: 3)),
      };
      foreach (var (name, rows) in OutOfOrder(t, stepsAfterComingBackInRest))
        scenarios[name] = rows;
      return scenarios;
    }

    /// <summary>The scenarios of frames out of order, drawn in every clip with a whole target. An older frame shown is not dropped.</summary>
    private static Dictionary<string, Row[]> OutOfOrder(long t, int stepsAfterComingBackInRest)
    {
      Row Frame(double animationStep, long displayStep, bool late = false) => F(animationStep, displayStep, t, late);
      Row on = On(t);
      return new Dictionary<string, Row[]>
      {
        ["36-swapped-pair"] = Rows(on, Frame(2 * t, t), Frame(t, 2 * t, late: true)),
        ["36-block-of-3"] = Rows(on, Frame(3 * t, t), Frame(t, 3 * t, late: true)),
        ["36-block-of-4"] = Rows(on, Frame(2 * t, t), Frame(2 * t, t), Frame(t, 3 * t, late: true)),
        ["37-older-frame-again"] = Rows(on, on, Frame(2 * t, 2 * t)),
        ["38-two-back-again"] = Rows(on, on, on, Frame(2 * t, 2 * t)),
        ["39-out-of-order-next-to-late"] = Rows(on, Frame(t, 2 * t, late: true), Frame(3 * t, t), Frame(t, 2 * t, late: true)),
        ["40-out-of-order-in-rest"] = Rows(on, on, S(2 * t, t, t), S(t, 2 * t, t), Repeat(stepsAfterComingBackInRest, S(t, t, t))),
      };
    }

    private static Dictionary<string, Row[]> Edges(long t) =>
      new Dictionary<string, Row[]>
      {
        ["41-out-of-order-first-frames"] = Rows(g_first, F(t, 2 * t, t, late: true)),
        ["41-out-of-order-last-frames"] = Rows(F(2 * t, t, t)),
      };

    /// <summary>The recovery forms at a target of <paramref name="t"/>, after a frame <paramref name="late"/> refreshes late: the pacer re-aligns, or goes back to its grid.</summary>
    private static Dictionary<string, Row[]> Recovery(long t, int late)
    {
      Row on = On(t);
      return new Dictionary<string, Row[]>
      {
        [$"31-realign-late-{late}"] = Rows(on, F(t, t + late, t, late: true), F(t + late, t, t), on),
        [$"32-back-on-grid-late-{late}"] = Rows(on, F(t, t + late, t, late: true), F(t, t - late, t), on),
      };
    }

    private static Dictionary<string, Row[]> Merge(params Dictionary<string, Row[]>[] tables) =>
      tables.SelectMany(table => table).ToDictionary(entry => entry.Key, entry => entry.Value);

    /// <summary>The scenarios of a display clip: <paramref name="t"/> is 1 or 2, a target of 2 named so.</summary>
    private static Dictionary<string, Row[]> Display(long t)
    {
      string suffix = t == 1 ? string.Empty : "-target-" + t;
      Row on = On(t);
      return new Dictionary<string, Row[]>
      {
        ["01-dropped" + suffix] = Rows(on, F(2 * t, 2 * t, t, dropped: 1)),
        ["03-dropped-clock-lost" + suffix] = Rows(on, F(t, 2 * t, t, late: true, dropped: 1)),
        ["04-late-step-stays" + suffix] = Rows(on, F(t, 2 * t, t, late: true), on),
        ["05-late-catches-up" + suffix] = Rows(on, F(t, 2 * t, t, late: true), F(2 * t, t, t)),
        ["07-dropped-then-refresh-lost" + suffix] = Rows(on, F(2 * t, 3 * t, t, late: true, dropped: 1), F(2 * t, t, t)),
        ["10-out-of-order-next-to-drop" + suffix] = Rows(on, F(2 * t, t, t), F(2 * t, 2 * t, t, dropped: 1), F(t, 2 * t, t, late: true)),
      };
    }

    private static Dictionary<string, Row[]> DisplayClip(int stepsAfterComingBackInRest) =>
      Merge(Display(1), Display(2), OutOfOrder(1, stepsAfterComingBackInRest), Edges(1));

    /// <summary>
    /// A stretch whose marker aims at 25 ms or 20 ms on a 60 Hz display: the target is its real refreshes, 2, and every frame is animated for
    /// 1.5 or 1.2 refreshes. Short frames are a rush, and no frame is late: none stays longer than the target.
    /// </summary>
    private static Row[] Stretch25Ms() => Repeat(4, F(1.5, 2, 2), F(1.5, 1, 2));

    private static Row[] Stretch20Ms() => Repeat(2, Rows(F(1.2, 2, 2), Repeat(4, F(1.2, 1, 2))));

    private static readonly Dictionary<string, Dictionary<string, Row[]>> g_expected = new Dictionary<string, Dictionary<string, Row[]>>
    {
      ["60-stress"] = Merge(
        Common(1, stepsFromRest: 6, stepsToLastAtRest: 5, stepsAfterComingBackInRest: 4),
        new Dictionary<string, Row[]>
        {
          // The frame after the change has the new target
          ["13-pacing-60-to-30"] = Rows(On(1), F(4, 4, 2, dropped: 1)),
          ["13-pacing-30-to-60"] = Rows(On(2), F(2, 2, 1, dropped: 1)),
          // 120 fps on a 60 Hz display: a target of one refresh, and every frame on it, whatever was dropped between
          ["15-faster-than-display"] = Rows(On(1), Repeat(23, F(1, 1, 1, dropped: 1))),
        }
      ),
      ["30-stress"] = Merge(
        Common(2, stepsFromRest: 2, stepsToLastAtRest: 1, stepsAfterComingBackInRest: 0),
        Recovery(2, 1),
        new Dictionary<string, Row[]>
        {
          ["13-pacing-30-to-60"] = Rows(On(2), F(2, 2, 1, dropped: 1)),
          ["13-pacing-60-to-30"] = Rows(On(1), F(4, 4, 2, dropped: 1)),
          // Half a target late, then back on the grid: the frame after it is a rush, its motion is right
          ["16-late-by-one"] = Rows(On(2), F(2, 3, 2, late: true), F(2, 1, 2)),
          ["17-early-then-back"] = Rows(On(2), F(2, 1, 2), On(2), F(2, 3, 2, late: true)),
          ["18-uneven-cadence"] = Rows(On(2), Repeat(12, F(2, 3, 2, late: true), F(2, 1, 2))),
          // Evenly on screen, and the animation at half its speed
          ["19-wrong-animation-rate"] = Rows(On(2), Repeat(23, F(1, 2, 2))),
          // Twice as fast as the target, on screen and in the animation: every frame a rush and a step short, and the sum 0
          ["26-frames-every-refresh"] = Rows(On(2), Repeat(24, F(1, 1, 2))),
          ["31-realign-late-2"] = Rows(On(2), F(2, 4, 2, late: true), F(4, 2, 2), On(2)),
          ["31-realign-late-3"] = Rows(On(2), F(2, 5, 2, late: true), F(5, 2, 2), On(2)),
          ["31-realign-dropped"] = Rows(On(2), F(3, 3, 2, dropped: 1), On(2), On(2)),
          ["32-back-on-grid-dropped"] = Rows(On(2), F(3, 3, 2, dropped: 1), F(1, 1, 2), On(2)),
          ["33-interval-1-late-1"] = Rows(On(2), F(2, 3, 2, late: true), F(2, 1, 2), On(2)),
          // The frame presented at interval 1 says so in its marker: its target is 1
          ["33-interval-1-late-1-target-1"] = Rows(On(2), F(2, 3, 2, late: true), F(2, 1, 1), On(2)),
          ["33-interval-1-dropped"] = Rows(On(2), F(3, 3, 2, dropped: 1), F(1, 1, 2), On(2)),
          ["33-interval-1-dropped-target-1"] = Rows(On(2), F(3, 3, 2, dropped: 1), On(1), On(2)),
          ["34-realign-clock-stays"] = Rows(On(2), F(2, 3, 2, late: true), On(2), On(2)),
          ["34-interval-1-clock-stays"] = Rows(On(2), F(2, 3, 2, late: true), F(2, 1, 2), On(2)),
        }
      ),
      ["15-stress"] = Merge(
        Common(4, stepsFromRest: 4, stepsToLastAtRest: 3, stepsAfterComingBackInRest: 2),
        Recovery(4, 1),
        Recovery(4, 2),
        Recovery(4, 3),
        new Dictionary<string, Row[]>
        {
          ["13-pacing-15-to-30"] = Rows(On(4), F(4, 4, 2, dropped: 1)),
          ["13-pacing-30-to-15"] = Rows(On(2), F(8, 8, 4, dropped: 1)),
          ["16-late-by-one"] = Rows(On(4), F(4, 6, 4, late: true), F(4, 2, 4)),
          ["17-early-then-back"] = Rows(On(4), F(4, 2, 4), On(4), F(4, 6, 4, late: true)),
          ["18-uneven-cadence"] = Rows(On(4), Repeat(12, F(4, 6, 4, late: true), F(4, 2, 4))),
          ["19-wrong-animation-rate"] = Rows(On(4), Repeat(23, F(2, 4, 4))),
          ["26-frames-every-refresh"] = Rows(On(4), Repeat(24, F(2, 2, 4))),
          ["35-target-3"] = Repeat(8, On(3)),
          ["31-realign-dropped"] = Rows(On(4), F(5, 5, 4, dropped: 1), On(4), On(4)),
          ["32-back-on-grid-dropped"] = Rows(On(4), F(5, 5, 4, dropped: 1), F(3, 3, 4), On(4)),
          // One frame at interval 1, then the next on the next step of the old grid
          ["33-interval-1-late-1"] = Rows(On(4), F(4, 5, 4, late: true), F(2, 1, 4), F(2, 2, 4)),
          ["33-interval-1-late-1-target-1"] = Rows(On(4), F(4, 5, 4, late: true), F(2, 1, 1), F(2, 2, 4)),
          ["33-interval-1-late-2"] = Rows(On(4), F(4, 6, 4, late: true), F(3, 1, 4), F(1, 1, 4)),
          ["33-interval-1-late-2-target-1"] = Rows(On(4), F(4, 6, 4, late: true), F(3, 1, 1), F(1, 1, 4)),
          ["33-interval-1-late-3"] = Rows(On(4), F(4, 7, 4, late: true), F(4, 1, 4), On(4)),
          ["33-interval-1-late-3-target-1"] = Rows(On(4), F(4, 7, 4, late: true), F(4, 1, 1), On(4)),
          ["33-interval-1-dropped"] = Rows(On(4), F(5, 5, 4, dropped: 1), F(1, 1, 4), F(2, 2, 4)),
          ["33-interval-1-dropped-target-1"] = Rows(On(4), F(5, 5, 4, dropped: 1), On(1), F(2, 2, 4)),
          ["34-realign-clock-stays"] = Rows(On(4), F(4, 5, 4, late: true), On(4), On(4)),
          ["34-interval-1-clock-stays"] = Rows(On(4), F(4, 5, 4, late: true), F(4, 1, 4), F(4, 2, 4)),
        }
      ),
      ["60-stress-edges"] = Edges(1),
      ["30-stress-edges"] = Edges(2),
      ["15-stress-edges"] = Edges(4),
      // A pacer at 25 ms on a 60 Hz display: 1, 2, 1, 2 refreshes against a target of 2, each frame animated for 1.5
      ["40-stress"] = new Dictionary<string, Row[]> { ["43-whole-capture"] = Rows(g_first, Repeat(159, F(1.5, 1, 2), F(1.5, 2, 2)), F(1.5, 1, 2)) },
      ["60-stress-uneven"] = Merge(
        OutOfOrder(1, stepsAfterComingBackInRest: 4),
        Edges(1),
        new Dictionary<string, Row[]>
        {
          ["42-stretch-25-ms-1"] = Stretch25Ms(),
          ["42-stretch-25-ms-2"] = Stretch25Ms(),
          ["42-stretch-25-ms-3"] = Stretch25Ms(),
          ["42-stretch-25-ms-4"] = Stretch25Ms(),
          ["42-stretch-20-ms-1"] = Stretch20Ms(),
          ["42-stretch-20-ms-2"] = Stretch20Ms(),
          ["42-stretch-20-ms-3"] = Stretch20Ms(),
          ["42-stretch-20-ms-4"] = Stretch20Ms(),
        }
      ),
      ["50-stress"] = DisplayClip(stepsAfterComingBackInRest: 2),
      ["120-stress"] = DisplayClip(stepsAfterComingBackInRest: 10),
      ["144-stress"] = DisplayClip(stepsAfterComingBackInRest: 12),
    };

    private string m_directory = string.Empty;
    private string m_ffmpeg = string.Empty;

    [SetUp]
    public void SetUp()
    {
      m_ffmpeg = VideoClips.FindFfmpegOrIgnore();
      m_directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
      try
      {
        if (Directory.Exists(m_directory))
          Directory.Delete(m_directory, true);
      }
      catch (IOException) { }
    }

    public static IEnumerable<string> Clips() => g_expected.Keys;

    public static IEnumerable<TestCaseData> Scenarios() =>
      g_expected.SelectMany(clip => clip.Value.Keys.Select(scenario => new TestCaseData(clip.Key, scenario).SetArgDisplayNames(clip.Key, scenario)));

    /// <summary>The table holds every scenario of every clip, and no other.</summary>
    [TestCaseSource(nameof(Clips))]
    public void TheTable_HasEveryScenarioOfTheClip(string clip)
    {
      Assert.That(g_expected[clip].Keys, Is.EquivalentTo(VideoClips.Manifest(clip).Scenarios.Select(s => s.Name).Distinct()));
    }

    /// <summary>What the markers and the display say: this holds whatever the frames are judged by.</summary>
    [TestCaseSource(nameof(Scenarios))]
    public void Scenario_IsReadAsItWasShown(string clip, string scenario)
    {
      var (frames, dropped, rows, positions, fps) = Analysed(clip, scenario);
      Assert.Multiple(() =>
      {
        for (int i = 0; i < rows.Length; ++i)
        {
          var (frame, row) = (frames[positions[i]], rows[i]);
          string where = $"{clip} {scenario}: frame index {frame.FrameIndex}";
          if (row.DisplayStep.HasValue)
            AssertRefreshes(frame.AnimationDelta?.Nanoseconds, row.AnimationStep, fps, where + ": animation step");
          AssertRefreshes(frame.DisplayDelta?.Nanoseconds, row.DisplayStep, fps, where + ": display step");
          AssertRefreshes(frame.AnimationError?.Nanoseconds, row.Sum, fps, where + ": the sum, the animation error");
          Assert.That(dropped[positions[i]], Is.EqualTo(row.Dropped), where + ": frames dropped before it");
        }
      });
    }

    /// <summary>What the frames are judged as: the two parts against the frame's own target, whether it counts as an animation error, and late.</summary>
    [TestCaseSource(nameof(Scenarios))]
    public void Scenario_IsJudgedAgainstItsTarget(string clip, string scenario)
    {
      var (frames, _, rows, positions, fps) = Analysed(clip, scenario);
      Assert.Multiple(() =>
      {
        for (int i = 0; i < rows.Length; ++i)
        {
          var (frame, row) = (frames[positions[i]], rows[i]);
          string where = $"{clip} {scenario}: frame index {frame.FrameIndex}";
          if (row.DisplayStep.HasValue)
            AssertRefreshes(frame.TargetFrameTime?.Nanoseconds, row.Target, fps, where + ": target");
          AssertRefreshes(-frame.PacingError?.Nanoseconds, row.StopOrRush, fps, where + ": stop or rush");
          AssertRefreshes(frame.PredictionError?.Nanoseconds, row.Step, fps, where + ": step");
          Assert.That(
            AnimationErrorRule.Counts(frame, TimelineAnalyzer.DefaultErrorThreshold),
            Is.EqualTo(row.IsError),
            where + ": counts as an animation error"
          );
          Assert.That(frame.Flags.HasFlag(PresentedFrameFlags.Late), Is.EqualTo(row.Late), where + ": late");
        }
      });
    }

    /// <summary>
    /// The two warnings of the report: a target the display can not show, and an older frame coming back. Which clips have which is the script's:
    /// 60-stress runs at 120 fps for a stretch, 40-stress is a whole run of 25 ms and has nothing out of order.
    /// </summary>
    [TestCase("60-stress", true, true)]
    [TestCase("30-stress", false, true)]
    [TestCase("15-stress", false, true)]
    [TestCase("60-stress-edges", false, true)]
    [TestCase("40-stress", true, false)]
    [TestCase("60-stress-uneven", true, true)]
    [TestCase("50-stress", false, true)]
    [TestCase("120-stress", false, true)]
    [TestCase("144-stress", false, true)]
    public void TheWarnings_SayWhatTheClipDoes(string clip, bool invalidTarget, bool outOfOrder)
    {
      var run = CaptureAnalyzer
        .Analyze(VideoClips.Imported(clip, m_ffmpeg, Path.Combine(m_directory, "capture")), new AnalysisOptions())
        .Timeline.Runs.Single();

      Assert.That(
        run.Warnings.Any(w => w.Contains("which is not a whole number", StringComparison.Ordinal)),
        Is.EqualTo(invalidTarget),
        $"{clip}: invalid target"
      );
      Assert.That(
        run.Warnings.Any(w => w.Contains("An older frame came back", StringComparison.Ordinal)),
        Is.EqualTo(outOfOrder),
        $"{clip}: out of order"
      );
    }

    private (IReadOnlyList<PresentedFrame> Frames, long[] Dropped, Row[] Rows, int[] Positions, int Fps) Analysed(string clip, string scenario)
    {
      var manifest = VideoClips.Manifest(clip);
      var run = CaptureAnalyzer
        .Analyze(VideoClips.Imported(clip, m_ffmpeg, Path.Combine(m_directory, "capture")), new AnalysisOptions())
        .Timeline.Runs.Single();
      var (_, first, count) = manifest.Scenarios.Single(s => s.Name == scenario);
      int[] positions = Enumerable
        .Range(0, manifest.FrameCount)
        .Where(i => manifest.Presented[i] >= first && manifest.Presented[i] < first + count)
        .ToArray();
      Row[] rows = g_expected[clip][scenario];
      Assert.That(positions, Has.Length.EqualTo(rows.Length), $"{clip} {scenario}: a row per presented frame");
      return (run.Frames, DroppedFrames.Before(run.Frames), rows, positions, manifest.Fps);
    }

    private static void AssertRefreshes(long? nanoseconds, double? refreshes, int fps, string what)
    {
      if (refreshes is { } expected)
        Assert.That((double?)nanoseconds, Is.EqualTo(expected * NanosecondTimeSpan.NanosecondsPerSecond / fps).Within(ToleranceNanoseconds), what);
      else
        Assert.That(nanoseconds, Is.Null, what);
    }
  }
}
