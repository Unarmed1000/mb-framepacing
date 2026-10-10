//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A frame row's times as the whole nanoseconds the trace arithmetic works in. A trace's clock is a plain count of nanoseconds (Perfetto's
//* is unsigned 64-bit, and a clock that counts from 1970 is past 2^60): the join adds and compares them as numbers, as
//* tools/trace_alignment.py does, never as the wrapping counters the capture's typed times are.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.Data;

namespace MB.FramePacing.TraceJoin
{
  internal static class FrameRowTimes
  {
    public static long FirstSeenNs(this FrameRow row) => row.FirstSeenTime.Nanoseconds;

    public static long OnScreenNs(this FrameRow row) => row.OnScreen.Nanoseconds;

    public static long? IntendedDisplayNs(this FrameRow row) => row.IntendedDisplayTime?.Nanoseconds;

    public static long? LatenessNs(this FrameRow row) => row.Lateness?.Nanoseconds;

    public static long? CpuStartNs(this FrameRow row) => row.CpuStartTime?.Nanoseconds;

    public static long? CpuBusyNs(this FrameRow row) => row.CpuBusy?.Value.Nanoseconds;

    /// <summary>True when the markers gave the frame both ends of its CPU work.</summary>
    public static bool HasCpuWork(this FrameRow row) => row.CpuStartTime != null && row.CpuBusy != null;
  }
}
