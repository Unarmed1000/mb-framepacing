//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a track's event is: a stretch of time, a moment, or a value.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>The kind of a <see cref="TraceEvent"/>.</summary>
  public enum TraceEventKind
  {
    /// <summary>A stretch of time on a track.</summary>
    Slice,

    /// <summary>A moment on a track.</summary>
    Instant,

    /// <summary>A value on a counter track, from its time until the next one.</summary>
    Counter,
  }
}
