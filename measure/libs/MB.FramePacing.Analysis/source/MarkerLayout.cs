//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where the markers are in the capture. Locks[0] is the top (timing) marker.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Collections.Generic;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Analysis
{
  /// <summary>Where the markers are in the capture. <see cref="Locks"/>[0] is the top (timing) marker.</summary>
  public sealed record MarkerLayout(IReadOnlyList<MarkerLock> Locks, float ModuleSizePx, IReadOnlyList<string> Warnings)
  {
    public MarkerLock Primary => Locks[0];
  }
}
