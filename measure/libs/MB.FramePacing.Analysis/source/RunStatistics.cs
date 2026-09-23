//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Statistics of one run: display delta, animation delta, animation error (signed and absolute), drift and time on screen.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record RunStatistics(
    Statistics DisplayDeltaMs,
    Statistics AnimationDeltaMs,
    Statistics AnimationErrorMs,
    Statistics AbsoluteAnimationErrorMs,
    Statistics DriftMs,
    Statistics OnScreenMs,
    // Presented frames whose |animation error| exceeds one capture period (larger than the measurement uncertainty)
    long FramesWithAnimationError
  );
}
