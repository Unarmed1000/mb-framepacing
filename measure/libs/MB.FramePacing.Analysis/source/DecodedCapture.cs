//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Result of CaptureDecoder: the capture file header, where the markers are, which clock was used and one row per capture.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Collections.Generic;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Analysis
{
  public sealed record DecodedCapture(CaptureFileHeader Header, MarkerLayout Layout, TimeSource TimeSource, IReadOnlyList<CaptureRow> Rows);
}
