//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One golden marker image written by the C++ library (test-data/markers/manifest.csv): the payload, the placement and the image file.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FrameMarker.UnitTest
{
  public sealed record GoldenMarker(string File, Payload Payload, StartMetadata Start, Options Options, Point Origin, int Width, int Height)
  {
    public override string ToString() => File;
  }
}
