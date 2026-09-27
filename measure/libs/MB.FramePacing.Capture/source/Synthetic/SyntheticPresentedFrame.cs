//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One application frame that reached the screen.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Synthetic
{
  /// <summary>One application frame that reached the screen.</summary>
  public readonly record struct SyntheticPresentedFrame(MarkerPayload Payload, long DisplayTicks);
}
