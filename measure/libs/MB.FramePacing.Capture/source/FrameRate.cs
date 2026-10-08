//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A rational frame rate (e.g. 60000/1001).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture
{
  /// <summary>A rational frame rate (e.g. 60000/1001).</summary>
  public readonly record struct FrameRate(uint Numerator, uint Denominator)
  {
    public static readonly FrameRate Unknown = new FrameRate(0, 0);

    public bool IsKnown => Numerator > 0 && Denominator > 0;

    public double FramesPerSecond => IsKnown ? (double)Numerator / Denominator : 0;

    /// <summary>The time from one frame to the next, rounded to the nearest nanosecond; zero if unknown.</summary>
    public NanosecondTimeSpan Interval =>
      // A second's nanoseconds times the denominator is exact in a double (a 32-bit number times 5^9 * 2^9), so one division rounds
      IsKnown
        ? new NanosecondTimeSpan((long)Math.Round(NanosecondTimeSpan.NanosecondsPerSecond * (double)Denominator / Numerator))
        : NanosecondTimeSpan.Zero;

    public static FrameRate FromFps(double fps)
    {
      if (fps <= 0)
        return Unknown;
      // Recognize the NTSC style rates exactly, everything else to 1/1000 fps precision
      foreach (uint rate in new uint[] { 24, 30, 48, 60, 120, 240 })
      {
        if (Math.Abs(fps - (rate * 1000.0 / 1001.0)) < 0.0005)
          return new FrameRate(rate * 1000, 1001);
      }
      return Math.Abs(fps - Math.Round(fps)) < 1e-9 ? new FrameRate((uint)Math.Round(fps), 1) : new FrameRate((uint)Math.Round(fps * 1000), 1000);
    }

    public override string ToString() => IsKnown ? (Denominator == 1 ? $"{Numerator}" : $"{FramesPerSecond:0.###}") : "unknown";
  }
}
