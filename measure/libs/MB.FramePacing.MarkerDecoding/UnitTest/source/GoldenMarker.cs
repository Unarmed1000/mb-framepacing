//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One golden marker image listed in sdk/test-data/markers/manifest.csv: its file, payload, start metadata and placement.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.MarkerDecoding.UnitTest
{
  public sealed record GoldenMarker(
    string Path,
    MarkerPayload Payload,
    StartMetadata? Start,
    int ModuleSizePx,
    int QuietZoneModules,
    int OriginX,
    int OriginY
  )
  {
    public override string ToString() => System.IO.Path.GetFileName(Path);
  }
}
