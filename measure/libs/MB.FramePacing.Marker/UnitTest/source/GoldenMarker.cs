//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One golden marker image listed in test-data/markers/manifest.csv: its file, payload, start metadata and placement.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker.UnitTest
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
