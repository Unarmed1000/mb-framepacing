//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Capture and frame counts of one run: decoded, undecodable, torn and not recorded captures, presented and skipped frames, out of order
//* captures and segments.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record RunCounts(
    long Captures,
    long Decoded,
    long Undecodable,
    long Torn,
    long NotRecorded,
    // Frames the capture source reported dropping, and refreshes the capture's device clock says were missed (MissedCaptures)
    long SourceDroppedFrames,
    long MissedCaptures,
    long PresentedFrames,
    long SkippedFrameIndices,
    // Frames the target dropped: skipped frame indices over a capture without gaps that never came back out of order (DroppedFrames)
    long DroppedFrames,
    long OutOfOrderCaptures,
    int Segments
  );
}
