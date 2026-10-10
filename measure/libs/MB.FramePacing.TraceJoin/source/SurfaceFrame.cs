//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One frame of the application as the system's frame timeline has it (an actual surface frame): from when the application began it to
//* when it was handed over or its GPU work ended, whichever was later. Times are whole nanoseconds on the trace's clock.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. A frame of the application in a trace's frame timeline, with what the system says of it.</summary>
  public sealed record SurfaceFrame(
    long StartNs,
    long EndNs,
    long SurfaceFrameToken,
    long? DisplayFrameToken,
    string? LayerName,
    string? PresentType,
    string? JankType,
    string? JankTag,
    bool? OnTimeFinish,
    bool? GpuComposition
  );
}
