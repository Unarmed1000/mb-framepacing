//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A frame's present, as the markers place it (CPU start time + CPU busy, on the application's clock), against its plan and the screen
//* (PresentTimings): what the pacer planned after it, how much later than the run's quickest frame it reached the screen, why it was late.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  /// <param name="PlannedLatency">
  /// The intended display time minus the present, on the application's clock: when the pacer planned the frame to be shown after its
  /// present. Null without a schedule or the CPU fields.
  /// </param>
  /// <param name="ExtraDelay">
  /// The frame's first sighting minus its present, less the run's lowest such value: how much later than the run's quickest frame it reached
  /// the screen after its present (the clocks' offset cancels out). Null without the CPU fields.
  /// </param>
  /// <param name="LateReason">Why the frame was late, for a late frame with the CPU fields of its own and of the frame before; else null.</param>
  /// <param name="WorkOverTarget">Its CPU busy was its intended period or longer; null without CPU busy or an intended period.</param>
  public sealed record PresentTiming(NanosecondTimeSpan? PlannedLatency, NanosecondTimeSpan? ExtraDelay, LateReason? LateReason, bool? WorkOverTarget)
  {
    /// <summary>A frame without the CPU fields: nothing is known.</summary>
    public static readonly PresentTiming None = new PresentTiming(null, null, null, null);
  }
}
