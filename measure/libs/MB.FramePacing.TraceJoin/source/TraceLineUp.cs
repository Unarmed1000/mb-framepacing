//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How well a run's markers and a trace line up, as numbers (TraceLineUpCheck works them out; measure/doc/perfetto.md "Do they line up").
//* Each part is there when the trace has what it needs. Numbers only: what is usual for them is not known until recorded traces have
//* been through it, so nothing here says pass or fail.
//*
//* None of it can show a delay the whole run shares: that is not in the difference between any two frames.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Globalization;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>How a run's markers and a trace line up, as numbers.</summary>
  /// <param name="ScheduledFrames">Frames the trace has the thread's scheduling for, over their markers' CPU busy time.</param>
  /// <param name="BusyNs">Those frames' CPU busy time by their markers, in all.</param>
  /// <param name="AwakeNs">Of it, how long the thread was running or waiting for a CPU.</param>
  /// <param name="AwakeEarlierNs">The same with every frame's stretch begun where the frame before began: a match that is a frame off.</param>
  /// <param name="AwakeLaterNs">The same with every frame's stretch begun where the next frame began.</param>
  /// <param name="MostlyAsleepFrames">Frames whose thread was neither running nor waiting for more than half of their CPU busy time.</param>
  /// <param name="PresentedFrames">Frames the trace has the system's own present time for.</param>
  /// <param name="PresentDifferenceMedianNs">The measured display time minus the system's present time: the median over those frames.</param>
  /// <param name="PresentDifferenceOffFrames">Frames whose difference is more than half a refresh from the median.</param>
  /// <param name="Record">The markers against the frame record the application logged, which the run was placed by.</param>
  public sealed record TraceLineUp(
    int ScheduledFrames,
    long BusyNs,
    long AwakeNs,
    long AwakeEarlierNs,
    long AwakeLaterNs,
    int MostlyAsleepFrames,
    int PresentedFrames,
    long PresentDifferenceMedianNs,
    long PresentDifferenceMinNs,
    long PresentDifferenceMaxNs,
    int PresentDifferenceOffFrames,
    FrameRecordCheck Record
  )
  {
    /// <summary>What a run's check file is named after the run's prefix (run-&lt;id&gt;).</summary>
    public const string FileSuffix = "-trace-check.txt";

    /// <summary>The numbers in words, a line for each part the trace had what it needs for; a first line says what they are.</summary>
    public IReadOnlyList<string> Lines()
    {
      var lines = new List<string> { "Do the markers and the trace line up (numbers only, and none of them shows a delay the whole run shares):" };
      if (ScheduledFrames > 0)
        lines.Add(
          $"  CPU busy against the thread's scheduling, {Count(ScheduledFrames)} frames: the thread was running or waiting for a CPU for "
            + $"{Percent(AwakeNs, BusyNs)} of the markers' CPU busy time (matched a frame earlier {Percent(AwakeEarlierNs, BusyNs)}, a frame "
            + $"later {Percent(AwakeLaterNs, BusyNs)}); {Count(MostlyAsleepFrames)} frames were neither for more than half of theirs."
        );
      else
        lines.Add("  CPU busy against the thread's scheduling: the trace has no scheduling of the application's thread for these frames.");
      if (PresentedFrames > 0)
        lines.Add(
          $"  Measured display time minus the system's present time, {Count(PresentedFrames)} frames: median {Ms(PresentDifferenceMedianNs)} ms, "
            + $"from {Ms(PresentDifferenceMinNs)} to {Ms(PresentDifferenceMaxNs)} ms; {Count(PresentDifferenceOffFrames)} frames more than "
            + "half a refresh from the median."
        );
      else
        lines.Add("  Measured display time minus the system's present time: the trace has no present time of the system's for these frames.");
      lines.Add(Record.Line());
      return lines;
    }

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Ms(long nanoseconds) => (nanoseconds / 1_000_000.0).ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);

    private static string Percent(long part, long whole) =>
      whole > 0 ? (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + " %" : "- %";
  }
}
