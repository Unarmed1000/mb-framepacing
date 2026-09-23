//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Monotonic capture clock in TimeSpan ticks, shared by a source and the recorder.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Diagnostics;

namespace MB.FramePacing.Capture
{
  /// <summary>Monotonic capture clock in TimeSpan ticks, shared by a source and the recorder.</summary>
  public sealed class CaptureClock
  {
    private readonly long m_startTimestamp = Stopwatch.GetTimestamp();

    public long NowTicks => Stopwatch.GetElapsedTime(m_startTimestamp).Ticks;
  }
}
