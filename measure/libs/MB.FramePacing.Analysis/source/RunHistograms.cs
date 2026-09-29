//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The distributions reported per run.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Linq;

namespace MB.FramePacing.Analysis
{
  /// <summary>The distributions reported per run.</summary>
  public sealed record RunHistograms(Histogram AnimationErrorMs, Histogram DisplayDeltaMs)
  {
    public static RunHistograms Create(RunAnalysis run)
    {
      // The animation errors of the frames with one; the display time steps of the frames that count toward the frame rate
      return new RunHistograms(
        Histogram.FromTicks(
          run.Frames.Where(f => f.AnimationErrorTicks.HasValue).Select(f => f.AnimationErrorTicks!.Value),
          Histogram.DefaultBinWidthTicks
        ),
        Histogram.FromTicks(
          run.Frames.Where(RunStatistics.CountsTowardFrameRate).Select(f => f.DisplayDeltaTicks!.Value),
          Histogram.DefaultBinWidthTicks
        )
      );
    }
  }
}
