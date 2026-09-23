//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One row of test-data/markers/modules.csv: a payload and the module matrix the C++ library generated for it (row major, one bit per
//* module, most significant bit first).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FrameMarker.UnitTest
{
  public sealed record ModuleDigestRow(int Line, Payload Payload, StartMetadata Start, int Size, string ModulesHex)
  {
    public override string ToString() => $"line {Line}";
  }
}
