//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The zoom steps of a playback page: besides the whole report, cards whose plots show a minute, ten seconds or two seconds at a time and
//* scroll with the playhead. A zoomed card holds the whole report at its zoom, so its size grows with the report's length times the zoom:
//* a pixel column per frame once frames are wider than a pixel, else per pixel column. The steps a report gets are the ones that fit a size
//* budget, coarsest first: a long run gets the coarser steps, and a section of it (--from/--to) every step.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Charts.Playback
{
  public static class PlaybackZoom
  {
    /// <summary>The zoom steps there are, coarsest first: the seconds the plots show at a time.</summary>
    public static readonly IReadOnlyList<double> SecondsPerScreen = new[] { 60.0, 10.0, 2.0 };

    /// <summary>
    /// What the zoomed cards of one page may add up to, in bytes of SVG (estimated). The page parses a zoomed card only when it is first
    /// shown, so they cost the page's size, not its loading.
    /// </summary>
    public const long ByteBudget = 64_000_000;

    /// <summary>
    /// At most what a zoomed card writes per frame or pixel column it draws (its panels' bars, holds and strip cells): measured from about
    /// 140 (pixel columns, or frames a few pixels wide) to 300 (frames many pixels wide) on test clips and runs of up to an hour.
    /// </summary>
    internal const double BytesPerElement = 300;

    /// <summary>
    /// The zoom steps for a report <paramref name="seconds"/> long with <paramref name="frames"/> frames, its plots
    /// <paramref name="plotWidth"/> wide: each shorter than the report, while the estimated sizes fit <see cref="ByteBudget"/>.
    /// </summary>
    public static IReadOnlyList<double> Steps(double seconds, int frames, double plotWidth)
    {
      var steps = new List<double>();
      double remaining = ByteBudget;
      foreach (double step in SecondsPerScreen)
      {
        // A step that shows (nearly) all of the report is the whole report again
        if (step >= seconds * 0.8)
          continue;
        double columns = seconds / step * plotWidth;
        double estimate = BytesPerElement * Math.Min(frames, columns);
        if (estimate > remaining)
          break;
        remaining -= estimate;
        steps.Add(step);
      }
      return steps;
    }
  }
}
