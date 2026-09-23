//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One application frame as it reached the display: when it was first seen, how long it stayed, its display and animation delta, animation
//* error and drift.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record PresentedFrame(
    int Segment,
    ulong FrameIndex,
    long AnimationTicks,
    long FirstCaptureIndex,
    long FirstSeenTicks,
    long LastSeenTicks,
    int CaptureCount,
    long OnScreenTicks,
    ulong SkippedBefore,
    long? DisplayDeltaTicks,
    long? AnimationDeltaTicks,
    long? AnimationErrorTicks,
    long DriftTicks,
    PresentedFrameFlags Flags
  );
}
