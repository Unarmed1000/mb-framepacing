//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What an EXPERIMENTAL camera capture adds to a run: how long the scanout takes between the two marker zones, and the tears it saw.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  /// <summary>What an EXPERIMENTAL camera capture adds to a run: the scanout delay between the zones and the tears the camera saw.</summary>
  /// <param name="ScanoutDelay">Per frame: first seen in the second zone minus first seen in the timing zone (ms).</param>
  /// <param name="FramesSeenInBothZones">Presented frames the second zone saw too.</param>
  /// <param name="TornFrames">Frames that reached the second zone clearly before the timing zone: presented mid-scanout (vsync off).</param>
  /// <param name="SecondZoneOnlyFrames">Frame indices only the second zone saw: replaced before the next scanout reached the timing zone.</param>
  public sealed record CameraRunStatistics(Statistics ScanoutDelay, long FramesSeenInBothZones, long TornFrames, long SecondZoneOnlyFrames);
}
