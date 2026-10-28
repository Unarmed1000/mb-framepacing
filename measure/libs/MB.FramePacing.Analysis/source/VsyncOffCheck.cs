//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Whether a run looks like vsync was off: nearly every frame it judged had frames before it that were never on screen (DroppedFrames), so
//* the application rendered faster than the display showed its frames. A warning only, with its numbers: the frames are measured as they
//* were presented, and a present mode that shows the newest frame at each refresh skips frames the same way.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MB.FramePacing.Analysis
{
  public static class VsyncOffCheck
  {
    /// <summary>The share of the judged frames with frames dropped before them from which the run is warned about.</summary>
    public const double Threshold = 0.9;

    /// <summary>The judged frames a run needs before it is warned about: a few frames say nothing about a setting.</summary>
    public const int MinimumFrames = 30;

    /// <summary>
    /// The share (0..1) of the frames with a judged display time step (<see cref="LateShare.Counts"/>) that had frames dropped before them,
    /// and how many frames were judged.
    /// </summary>
    public static (double Share, int Judged) Share(IReadOnlyList<PresentedFrame> frames)
    {
      var dropped = DroppedFrames.Before(frames);
      int judged = 0;
      int afterDrops = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        if (!LateShare.Counts(frames[i]))
          continue;
        ++judged;
        if (dropped[i] > 0)
          ++afterDrops;
      }
      return (judged > 0 ? afterDrops / (double)judged : 0, judged);
    }

    /// <summary>
    /// The warning for <paramref name="frames"/> as its lines (the facts, then what they probably mean), or none when fewer than
    /// <see cref="Threshold"/> of them had frames dropped before them.
    /// </summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<PresentedFrame> frames)
    {
      var (share, judged) = Share(frames);
      if (judged < MinimumFrames || share < Threshold)
        return Array.Empty<string>();
      return new[]
      {
        string.Create(
          CultureInfo.InvariantCulture,
          $"{share * 100:0} % of the frames had frames before them that were never shown: the application rendered faster than the display showed its frames."
        ),
        "Vsync is probably off (or a present mode shows the newest frame at each refresh). The frames are measured as they were presented; check the application's vsync setting.",
      };
    }

    /// <summary>The warning for <paramref name="frames"/> as one text, or null (<see cref="Lines"/>).</summary>
    public static string? Warning(IReadOnlyList<PresentedFrame> frames) => Lines(frames) is { Count: > 0 } lines ? string.Join(" ", lines) : null;
  }
}
