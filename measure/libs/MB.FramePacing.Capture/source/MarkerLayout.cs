//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where the markers are in the capture. Locks[0] is the top (timing) marker; further locks (the sync marker, a camera's lower zones)
//* check tearing or measure the scanout.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  /// <summary>Where the markers are in the capture. <see cref="Locks"/>[0] is the top (timing) marker.</summary>
  public sealed record MarkerLayout(IReadOnlyList<MarkerLock> Locks, double ModuleSizePx, IReadOnlyList<string> Warnings)
  {
    public MarkerLock Primary => Locks[0];

    /// <summary>
    /// The layout of <paramref name="locks"/> (the main marker first), with the warnings that go with it: a marker too small to decode
    /// reliably, or the camera capture notice.
    /// </summary>
    public static MarkerLayout For(IReadOnlyList<MarkerLock> locks, bool camera)
    {
      var warnings = new List<string>();
      double module = locks.Count > 0 ? locks[0].ModuleSizePx : 0;
      if (camera)
        warnings.Add("Camera capture: " + CameraRig.ExperimentalNotice);
      else if (module < 2)
        warnings.Add($"The marker is only {module:0.0} stored pixels per module (minimum 2, recommended 3): decoding will be unreliable.");
      else if (module < 2.75)
        warnings.Add($"The marker is {module:0.0} stored pixels per module (recommended 3 or more).");
      return new MarkerLayout(locks, module, warnings);
    }
  }
}
