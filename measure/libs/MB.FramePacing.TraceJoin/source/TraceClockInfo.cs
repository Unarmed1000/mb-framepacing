//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A clock a trace has snapshots of.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. A clock a trace has snapshots of: Perfetto's id, the name where the trace has one, and how many snapshots.</summary>
  public sealed record TraceClockInfo(long ClockId, string? Name, long Snapshots);
}
