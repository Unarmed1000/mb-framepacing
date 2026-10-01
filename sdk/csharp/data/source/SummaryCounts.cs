//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's runs[].counts: what the run's captures held.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  public sealed record SummaryCounts(
    [property: JsonRequired] long Captures,
    [property: JsonRequired] long Decoded,
    [property: JsonRequired] long Undecodable,
    [property: JsonRequired] long Torn,
    [property: JsonRequired] long NotRecorded,
    [property: JsonRequired] long SourceDroppedFrames,
    [property: JsonRequired] long MissedCaptures,
    [property: JsonRequired] long PresentedFrames,
    [property: JsonRequired] long SkippedFrameIndices,
    [property: JsonRequired] long DroppedFrames,
    [property: JsonRequired] long OutOfOrderCaptures,
    [property: JsonRequired] int Segments
  );
}
