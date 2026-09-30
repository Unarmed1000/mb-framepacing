//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One row of test-data/markers/modules.csv: a payload and the module matrix the C++ library generated for it (row major, one bit per
//* module, most significant bit first).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker.UnitTest
{
  public sealed record ModuleDigestRow(int Line, Payload Payload, StartMetadata Start, int Size, string ModulesHex)
  {
    public override string ToString() => $"line {Line}";
  }
}
