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
      var frames = run.Frames.Where(f => f.AnimationErrorTicks.HasValue).ToArray();
      return new RunHistograms(
        Histogram.FromTicks(frames.Select(f => f.AnimationErrorTicks!.Value), Histogram.DefaultBinWidthTicks),
        Histogram.FromTicks(frames.Select(f => f.DisplayDeltaTicks!.Value), Histogram.DefaultBinWidthTicks)
      );
    }
  }
}
