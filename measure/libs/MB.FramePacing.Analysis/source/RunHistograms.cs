//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The distributions reported per run.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Linq;

namespace MB.FramePacing.Analysis
{
  /// <summary>The distributions reported per run.</summary>
  public sealed record RunHistograms(Histogram AnimationErrorMs, Histogram DisplayDeltaMs)
  {
    public static RunHistograms Create(RunAnalysis run, long capturePeriodTicks)
    {
      var frames = run.Frames.Where(f => f.AnimationErrorTicks.HasValue).ToArray();
      return new RunHistograms(
        Histogram.FromTicks(frames.Select(f => f.AnimationErrorTicks!.Value), capturePeriodTicks),
        Histogram.FromTicks(frames.Select(f => f.DisplayDeltaTicks!.Value), capturePeriodTicks)
      );
    }
  }
}
