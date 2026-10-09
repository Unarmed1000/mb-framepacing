//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The application clocks by the names the options take, and by Perfetto's ids of the system's clocks (its BuiltinClock).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. Names and Perfetto clock ids of <see cref="TraceAppClock"/>.</summary>
  public static class TraceAppClockUtil
  {
    /// <summary>The names an option takes, the default first.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "monotonic", "boottime", "monotonic-raw", "realtime" };

    public static string NameOf(TraceAppClock clock) =>
      clock switch
      {
        TraceAppClock.Monotonic => "monotonic",
        TraceAppClock.Boottime => "boottime",
        TraceAppClock.MonotonicRaw => "monotonic-raw",
        TraceAppClock.Realtime => "realtime",
        _ => throw new ArgumentOutOfRangeException(nameof(clock)),
      };

    /// <summary>Perfetto's id of the clock.</summary>
    public static int ClockId(TraceAppClock clock) =>
      clock switch
      {
        TraceAppClock.Monotonic => 3,
        TraceAppClock.Boottime => 6,
        TraceAppClock.MonotonicRaw => 5,
        TraceAppClock.Realtime => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(clock)),
      };

    public static bool TryParse(string? name, out TraceAppClock clock)
    {
      foreach (TraceAppClock candidate in Enum.GetValues<TraceAppClock>())
      {
        if (string.Equals(NameOf(candidate), name, StringComparison.Ordinal))
        {
          clock = candidate;
          return true;
        }
      }
      clock = TraceAppClock.Monotonic;
      return false;
    }

    /// <summary>The application clock of a join's options. A name that is none of the clocks' is a <see cref="TraceException"/>.</summary>
    public static TraceAppClock Of(TraceJoinOptions options)
    {
      if (options.AppClock == null)
        return TraceAppClock.Monotonic;
      return TryParse(options.AppClock, out var clock)
        ? clock
        : throw new TraceException($"'{options.AppClock}' is not an application clock: one of {string.Join(", ", Names)}.");
    }

    /// <summary>The name of the clock with Perfetto's <paramref name="clockId"/>; null for a clock that is not one of these.</summary>
    public static string? NameOfId(long clockId)
    {
      foreach (TraceAppClock candidate in Enum.GetValues<TraceAppClock>())
      {
        if (ClockId(candidate) == clockId)
          return NameOf(candidate);
      }
      return null;
    }
  }
}
