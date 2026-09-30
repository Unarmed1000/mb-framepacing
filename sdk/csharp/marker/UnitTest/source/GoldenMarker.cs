//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One golden marker image written by the C++ library (test-data/markers/manifest.csv): the payload, the placement and the image file.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker.UnitTest
{
  public sealed record GoldenMarker(string File, Payload Payload, StartMetadata Start, Options Options, Point Origin, int Width, int Height)
  {
    public override string ToString() => File;
  }
}
