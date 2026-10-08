//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The late share of a whole run, computed once: per frame, over the frames with a display time step in the 2 s window that ends at it,
//* the share of frames late (later than their target: the pacer's intent in the markers, else one refresh) or held longer than a refresh as
//* the pacer intended, and whether any was late and any held longer (the line's red, amber or green). The window reaches back before any
//* section's start, so a section shows the run's own values. Per pixel column: the shares' range from a wavelet matrix, the colours from
//* rank bits. Immutable.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public sealed class LateShareData
  {
    private LateShareData(double[] percent, RankBits anyLate, RankBits anyHeld, bool anyHeldLonger)
    {
      Percent = percent;
      AnyLate = anyLate;
      AnyHeld = anyHeld;
      AnyHeldLonger = anyHeldLonger;
      // Shares are 0 or more: their bits as whole numbers sort as the shares do
      var bits = new long[percent.Length];
      for (int i = 0; i < percent.Length; ++i)
        bits[i] = BitConverter.DoubleToInt64Bits(percent[i]);
      Shares = new WaveletMatrix(bits);
    }

    /// <summary>Per frame: the share in percent.</summary>
    public IReadOnlyList<double> Percent { get; }

    /// <summary>Per frame: a frame in its window was late.</summary>
    public RankBits AnyLate { get; }

    /// <summary>Per frame: a frame in its window was held longer than a refresh as the pacer intended.</summary>
    public RankBits AnyHeld { get; }

    /// <summary>Some frame of the run was held longer than a refresh as the pacer intended.</summary>
    public bool AnyHeldLonger { get; }

    /// <summary>The shares (as their bits) in frame order: a column's lowest and highest.</summary>
    public WaveletMatrix Shares { get; }

    /// <summary>A share's value from <see cref="Shares"/>.</summary>
    public static double ShareOf(long bits) => BitConverter.Int64BitsToDouble(bits);

    public static LateShareData Create(IReadOnlyList<PresentedFrame> frames, RunPacing pacing)
    {
      // Held longer: on screen half a refresh or more beyond the frame time the application wants (its preferred frame time, else one
      // refresh) without being late: the pacer intended it, but runs slower than the application wants. Never for a static step (the time on
      // screen of a frame nothing animated after) or on demand
      double half = pacing.RefreshPeriod.Nanoseconds / 2.0;
      var held = new bool[frames.Count];
      bool anyHeldLonger = false;
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        held[i] =
          (frame.Flags & (PresentedFrameFlags.Late | PresentedFrameFlags.StaticBefore)) == 0
          && Analysis.LateShare.Counts(frame)
          && frame.DisplayDelta?.Nanoseconds is { } display
          && frame.PreferredFrameTime?.Nanoseconds is { } preferred
          && display >= preferred + half;
        anyHeldLonger |= held[i];
      }

      var percent = new double[frames.Count];
      var anyLate = new bool[frames.Count];
      var anyHeld = new bool[frames.Count];
      int start = 0;
      int counted = 0;
      int late = 0;
      int heldCount = 0;
      void Add(int index, int sign)
      {
        if (!Analysis.LateShare.Counts(frames[index]))
          return;
        counted += sign;
        if ((frames[index].Flags & PresentedFrameFlags.Late) != 0)
          late += sign;
        else if (held[index])
          heldCount += sign;
      }
      for (int i = 0; i < frames.Count; ++i)
      {
        Add(i, 1);
        while (frames[i].FirstSeenTime - frames[start].FirstSeenTime >= Analysis.LateShare.Window)
          Add(start++, -1);
        percent[i] = (counted > 0 ? (late + heldCount) / (double)counted : 0) * 100;
        anyLate[i] = late > 0;
        anyHeld[i] = heldCount > 0;
      }
      return new LateShareData(percent, new RankBits(frames.Count, i => anyLate[i]), new RankBits(frames.Count, i => anyHeld[i]), anyHeldLonger);
    }
  }
}
