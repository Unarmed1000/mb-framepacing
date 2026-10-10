//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A thread of a process in a trace.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. A thread of a process in a trace.</summary>
  /// <param name="Utid">The trace processor's own id of the thread.</param>
  public sealed record TraceThreadInfo(long Utid, string Process, string Thread, bool IsMain);
}
