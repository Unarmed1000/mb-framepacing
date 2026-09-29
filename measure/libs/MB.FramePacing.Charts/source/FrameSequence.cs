//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One kind of value of a run's frames (their animation errors, their holds' levels, their frametimes...), in frame order, for the frames
//* that have one: which frames do (RankBits) and the values as a wavelet matrix (built on first use, once). A range of frames maps to the
//* range of its values in constant time, and the matrix answers that range's minimum, maximum, percentiles and counts exactly. Immutable.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Threading;

namespace MB.FramePacing.Charts
{
  public sealed class FrameSequence
  {
    private readonly Lazy<WaveletMatrix> m_values;

    /// <summary>The value <paramref name="valueOf"/> gives each of <paramref name="frameCount"/> frames, where it gives one.</summary>
    public FrameSequence(int frameCount, Func<int, long?> valueOf)
    {
      Frames = new RankBits(frameCount, i => valueOf(i).HasValue);
      m_values = new Lazy<WaveletMatrix>(
        () =>
        {
          var values = new long[Frames.Count];
          int next = 0;
          for (int i = 0; i < frameCount; ++i)
          {
            if (valueOf(i) is { } value)
              values[next++] = value;
          }
          return new WaveletMatrix(values);
        },
        LazyThreadSafetyMode.ExecutionAndPublication
      );
    }

    /// <summary>Which frames have a value.</summary>
    public RankBits Frames { get; }

    /// <summary>The values, in frame order.</summary>
    public WaveletMatrix Values => m_values.Value;

    /// <summary>How many values there are.</summary>
    public int Count => Frames.Count;

    /// <summary>The values of frames <paramref name="frameStart"/> to <paramref name="frameEnd"/> (exclusive): where they start and end.</summary>
    public (int Start, int End) Of(int frameStart, int frameEnd) =>
      frameEnd > frameStart ? (Frames.Rank(frameStart), Frames.Rank(frameEnd)) : (Frames.Rank(frameStart), Frames.Rank(frameStart));

    /// <summary>The frames in <paramref name="frameStart"/> to <paramref name="frameEnd"/> (exclusive) that have a value, in order.</summary>
    public IEnumerable<int> FramesIn(int frameStart, int frameEnd)
    {
      for (int i = frameStart; i < frameEnd; ++i)
      {
        if (Frames[i])
          yield return i;
      }
    }
  }
}
