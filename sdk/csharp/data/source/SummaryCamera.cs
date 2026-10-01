//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's runs[].camera (EXPERIMENTAL camera captures): the scanout delay between the zones and the tears the camera saw.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  /// <param name="ScanoutDelay">Per frame: first seen in the second zone minus first seen in the timing zone (ms).</param>
  /// <param name="FramesSeenInBothZones">Presented frames the second zone saw too.</param>
  /// <param name="TornFrames">Frames that reached the second zone clearly before the timing zone: presented mid-scanout.</param>
  /// <param name="SecondZoneOnlyFrames">Frame indices only the second zone saw.</param>
  public sealed record SummaryCamera(
    [property: JsonRequired] ValueStatistics ScanoutDelay,
    [property: JsonRequired] long FramesSeenInBothZones,
    [property: JsonRequired] long TornFrames,
    [property: JsonRequired] long SecondZoneOnlyFrames
  );
}
