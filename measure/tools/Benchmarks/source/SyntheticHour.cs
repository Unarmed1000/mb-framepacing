//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* An hour of a 240 Hz game as an analysed run, for the card benchmarks: every 97th frame late (held two refreshes, off by one refresh),
//* every 7th off by half a millisecond either way, and one 700 ms hitch (the report tests' synthetic run).
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
    private const long Refresh = TimeSpan.TicksPerSecond / 240;

    public static ChartRun Create(int count = 240 * 3600, int lateEvery = 97, int hitchFrame = 500_000)
    {
      var frames = new List<PresentedFrame>(count);
      long time = 0;
      for (int i = 0; i < count; ++i)
      {
        bool hitch = i == hitchFrame;
        bool late = hitch || (i > 0 && i % lateEvery == 0);
        long display =
          hitch ? 168 * Refresh
          : late ? 2 * Refresh
          : Refresh;
        long error =
          hitch ? -700 * TimeSpan.TicksPerMillisecond
          : late ? -Refresh
          : i % 7 == 0 ? (i % 14 == 0 ? 5000 : -5000)
          : 0;
        if (i > 0)
          time += display;
        bool first = i == 0;
        frames.Add(
          new PresentedFrame(
            0,
            (ulong)i,
            new TimeSpan(time),
            i,
            new TickCount64(time),
            new TickCount64(time),
            1,
            new TimeSpan(Refresh),
            0,
            first ? null : new TimeSpan(display),
            first ? null : new TimeSpan(display + error),
            first ? null : new TimeSpan(error),
            TimeSpan.Zero,
            late ? PresentedFrameFlags.Late : PresentedFrameFlags.None,
            TargetFrameTime: first ? null : new TimeSpan(Refresh),
            CpuStartTime: new TickCount64(time - (Refresh / 2)),
            CpuBusy: new TimeSpan32((uint)(Refresh / 3)),
            FrameTime: first ? null : new TimeSpan(display)
          )
        );
      }
      int lateCount = frames.Count(f => (f.Flags & PresentedFrameFlags.Late) != 0);
      double refreshMs = Refresh / (double)TimeSpan.TicksPerMillisecond;
      var pacing = new RunPacing(
        refreshMs,
        false,
        refreshMs,
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
        RunStatistics.From(frames, TimeSpan.FromMilliseconds(1), new TimeSpan(Refresh)),
        frames,
        Array.Empty<string>(),
        Pacing: pacing
      );
      return new ChartRun(analysis, new TimeSpan(Refresh), TimeSpan.FromMilliseconds(1), false);
    }
  }
}
