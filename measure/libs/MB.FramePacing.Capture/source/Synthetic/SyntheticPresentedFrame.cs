//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One application frame that reached the screen.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture.Synthetic
{
  /// <summary>One application frame that reached the screen.</summary>
  public readonly record struct SyntheticPresentedFrame(MarkerPayload Payload, TickCount64 DisplayTime);
}
