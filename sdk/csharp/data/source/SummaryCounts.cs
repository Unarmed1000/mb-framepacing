//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's runs[].counts: what the run's captures held.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  public sealed record SummaryCounts(
    long Captures,
    long Decoded,
    long Undecodable,
    long Torn,
    long NotRecorded,
    long SourceDroppedFrames,
    long MissedCaptures,
    long PresentedFrames,
    long SkippedFrameIndices,
    long DroppedFrames,
    long OutOfOrderCaptures,
    int Segments
  );
}
