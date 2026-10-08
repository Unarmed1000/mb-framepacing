//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* An hour of a 240 Hz game (or another rate) as an analysed run, for the card and playback benchmarks: every 97th frame late (held two
//* refreshes, off by one refresh), every 7th off by half a millisecond either way, and one 700 ms hitch (the report tests' synthetic run).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Charts;

namespace MB.FramePacing.Benchmarks
{
  internal static class SyntheticHour
  {
    /// <summary>What every 7th frame is off by, either way.</summary>
    private const long HalfMillisecond = NanosecondTimeSpan.NanosecondsPerMillisecond / 2;

    /// <param name="count">The frames.</param>
    /// <param name="refreshHz">The display's refresh rate: the game runs at it.</param>
    public static ChartRun Create(int count = 240 * 3600, int lateEvery = 97, int hitchFrame = 500_000, int refreshHz = 240)
    {
      // The refresh period is a second over the rate, cut to the nanosecond, and so are half of it (the CPU's start before the frame)
      // and a third (CPU busy)
      long refresh = NanosecondTimeSpan.NanosecondsPerSecond / refreshHz;
      long halfRefresh = refresh / 2;
      long thirdRefresh = refresh / 3;
      var frames = new List<PresentedFrame>(count);
      long time = 0;
      for (int i = 0; i < count; ++i)
      {
        bool hitch = i == hitchFrame;
        bool late = hitch || (i > 0 && i % lateEvery == 0);
        long display =
          hitch ? 168 * refresh
          : late ? 2 * refresh
          : refresh;
        long error =
          hitch ? -700 * NanosecondTimeSpan.NanosecondsPerMillisecond
          : late ? -refresh
          : i % 7 == 0 ? (i % 14 == 0 ? HalfMillisecond : -HalfMillisecond)
          : 0;
        if (i > 0)
          time += display;
        bool first = i == 0;
        frames.Add(
          new PresentedFrame(
            0,
            (ulong)i,
            new NanosecondTimeSpan(time),
            i,
            new NanosecondTickCount(time),
            new NanosecondTickCount(time),
            1,
            new NanosecondTimeSpan(refresh),
            0,
            first ? null : new NanosecondTimeSpan(display),
            first ? null : new NanosecondTimeSpan(display + error),
            first ? null : new NanosecondTimeSpan(error),
            NanosecondTimeSpan.Zero,
            late ? PresentedFrameFlags.Late : PresentedFrameFlags.None,
            TargetFrameTime: first ? null : new NanosecondTimeSpan(refresh),
            CpuStartTime: new NanosecondTickCount(time - halfRefresh),
            CpuBusy: NanosecondTimeDuration.FromNanoseconds(thirdRefresh),
            FrameTime: first ? null : new NanosecondTimeSpan(display)
          )
        );
      }
      int lateCount = frames.Count(f => (f.Flags & PresentedFrameFlags.Late) != 0);
      var pacing = new RunPacing(
        new NanosecondTimeSpan(refresh),
        false,
        new NanosecondTimeSpan(refresh),
        PacingSource.NativeRefresh,
        lateCount,
        lateCount / (double)(count - 1),
        LateShare.Worst(frames, LateShare.Window),
        lateCount,
        0,
        PacingVerdict.BadPacing
      );
      var analysis = new RunAnalysis(
        1,
        "one hour",
        null,
        true,
        true,
        new RunCounts(count, count, 0, 0, 0, 0, 0, count, 0, 0, 0, 1),
        RunStatistics.From(frames, NanosecondTimeSpan.FromMilliseconds(1), new NanosecondTimeSpan(refresh)),
        frames,
        Array.Empty<string>(),
        Pacing: pacing
      );
      return new ChartRun(analysis, new NanosecondTimeSpan(refresh), NanosecondTimeSpan.FromMilliseconds(1), false);
    }
  }
}
