//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The names of the clocks a trace has snapshots of, for listing what a trace has: the system clocks by the numbers Perfetto gives its
//* built-in clocks. The frame record is on the monotonic one (sdk/doc/trace-frame-record.md).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>The names of a trace's built-in clocks.</summary>
  public static class TraceClockText
  {
    /// <summary>The name of the built-in clock with the id <paramref name="clockId"/>; null for another clock.</summary>
    public static string? NameOfId(long clockId) =>
      clockId switch
      {
        1 => "realtime",
        3 => "monotonic",
        5 => "monotonic-raw",
        6 => "boottime",
        _ => null,
      };
  }
}
