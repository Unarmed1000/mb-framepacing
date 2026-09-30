//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The adaptive swap interval rule (sdk/doc/pacer.md): Swappy's (SwappyCommon::updateSwapInterval, as mb-framepacing-explained simulates it in
//* tools/frame_pacing_video/adaptive_rate.py) and the late count fix. It keeps the frames of the last WindowTicks since its last change and decides
//* after every frame whether to run slower or faster. FramePacer uses it; an application with its own frame loop can use it alone. The window is
//* allocated once, when the rule is made; AddFrame and the rest never allocate. Integer arithmetic only, the C++ library's to the tick.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer
{
  public sealed class SwapIntervalRule
  {
    private const long OneTickQ32 = RefreshPeriod.OneTickQ32;

    // Swappy's calculateSwapInterval: a remainder of at most 500 ns does not need another refresh
    private const long RemainderMarginTicks = 5;

    private readonly PacerSettings m_settings;
    private readonly Entry[] m_entries;
    private int m_first;
    private int m_count;
    private long m_workSum;
    private uint m_lateCount;
    private uint m_swapInterval;

    /// <summary>Allocates the window (<see cref="PacerSettings.WindowCapacity"/> frames, or enough for the window at the preferred swap interval).</summary>
    public SwapIntervalRule(PacerSettings settings)
    {
      if (settings == null)
        throw new ArgumentNullException(nameof(settings));
      m_settings = settings.Clone();
      m_entries = new Entry[Capacity(m_settings)];
      m_swapInterval = m_settings.PreferredSwapInterval;
    }

    /// <summary>The swap interval the next frame is paced at.</summary>
    public uint SwapInterval => m_swapInterval;

    /// <summary>What the window holds now.</summary>
    public WindowState Window
    {
      get
      {
        if (m_count == 0)
          return default;
        long span = At(m_count - 1).DisplayTicks - At(0).DisplayTicks;
        return new WindowState((uint)m_count, m_lateCount, m_workSum / m_count, span, span > m_settings.WindowTicks);
      }
    }

    /// <summary>A copy of the settings the rule was made with.</summary>
    public PacerSettings Settings => m_settings.Clone();

    /// <summary>
    /// A frame was shown: when (<paramref name="displayTicks"/>), how long it worked (<paramref name="workTicks"/>) and whether it was late. Then
    /// the rule decides at the refresh period: the new swap interval is <see cref="SwapInterval"/>, and the window restarts on a change.
    /// </summary>
    /// <exception cref="ArgumentException">For default(RefreshPeriod).</exception>
    public SwapIntervalChange AddFrame(long displayTicks, long workTicks, bool late, RefreshPeriod period)
    {
      PacerSettings.RequireRefresh(period, nameof(period));
      // The frames of the last WindowTicks: the oldest go once the second oldest is more than WindowTicks older than this one (so the window
      // keeps one frame beyond it, as Swappy's does), and when the window is full
      if (m_count == m_entries.Length)
        PopFront();
      m_entries[(m_first + m_count) % m_entries.Length] = new Entry(displayTicks, Math.Clamp(workTicks, 0, m_settings.WindowTicks), late);
      ++m_count;
      m_workSum += At(m_count - 1).WorkTicks;
      m_lateCount += late ? 1u : 0u;
      while (m_count >= 2 && displayTicks - At(1).DisplayTicks > m_settings.WindowTicks)
        PopFront();

      if (!m_settings.AutoSwapInterval)
        return SwapIntervalChange.None;
      uint frames = (uint)m_count;
      bool full = displayTicks - At(0).DisplayTicks > m_settings.WindowTicks;
      long frameTicks = (m_workSum / frames) + m_settings.FrameMarginTicks;

      bool mayGoSlower =
        m_swapInterval < PacerSettings.MaxSwapInterval
        && (long)m_swapInterval * period.TicksQ32 <= (m_settings.SlowestFrameTicks + m_settings.FrameMarginTicks) * OneTickQ32;
      bool slowerByShare = full && LatePercent(m_lateCount, frames) > m_settings.SlowDownLatePercent;
      // The fix: as many late frames as would make a full window's share late, without waiting for the window to fill again
      bool slowerByCount =
        m_settings.SlowDown == SlowDownRule.LateCount
        && 100L * m_lateCount > m_settings.SlowDownLatePercent * FullWindowFrames(m_settings.WindowTicks, m_swapInterval, period);

      var change = SwapIntervalChange.None;
      uint swapInterval = m_swapInterval;
      if (mayGoSlower && (slowerByShare || slowerByCount))
      {
        swapInterval = Math.Max(m_swapInterval + 1, NeededSwapInterval(frameTicks, period));
        change = SwapIntervalChange.Slower;
      }
      else if (
        full
        && m_lateCount == 0
        && m_swapInterval > m_settings.PreferredSwapInterval
        && (frameTicks + m_settings.FrameMarginTicks) * OneTickQ32 < (long)(m_swapInterval - 1) * period.TicksQ32
      )
      {
        swapInterval = Math.Max(m_settings.PreferredSwapInterval, NeededSwapInterval(frameTicks, period));
        change = SwapIntervalChange.Faster;
      }
      if (change != SwapIntervalChange.None)
      {
        m_swapInterval = swapInterval;
        Clear();
      }
      return change;
    }

    /// <summary>Start again at <paramref name="swapInterval"/> (at least the preferred one) with an empty window.</summary>
    public void Reset(uint swapInterval)
    {
      m_swapInterval = Math.Clamp(swapInterval, m_settings.PreferredSwapInterval, PacerSettings.MaxSwapInterval);
      Clear();
    }

    /// <summary>Empty the window, keeping the swap interval.</summary>
    public void Clear()
    {
      m_first = 0;
      m_count = 0;
      m_workSum = 0;
      m_lateCount = 0;
    }

    private Entry At(int index) => m_entries[(m_first + index) % m_entries.Length];

    private void PopFront()
    {
      Entry oldest = At(0);
      m_workSum -= oldest.WorkTicks;
      m_lateCount -= oldest.Late ? 1u : 0u;
      m_first = (m_first + 1) % m_entries.Length;
      --m_count;
    }

    private static int Capacity(PacerSettings settings)
    {
      if (settings.WindowCapacity > 0)
        return (int)settings.WindowCapacity;
      // The frames of a window at the preferred swap interval, and the frame on either side; twice that for a faster display mode
      long frames = (settings.WindowTicks / (settings.Refresh.Ticks * settings.PreferredSwapInterval)) + 2;
      return (int)Math.Min(frames * 2, PacerSettings.MaxWindowCapacity);
    }

    // The refreshes a frame of frameTicks needs (Swappy's calculateSwapInterval): at least 1, and one more for a remainder beyond 500 ns; at most
    // PacerSettings.MaxSwapInterval. frameTicks is at most a window and two margins, so frameTicks * 2^32 fits 64 bits.
    private static uint NeededSwapInterval(long frameTicks, RefreshPeriod period)
    {
      long frameQ32 = frameTicks * OneTickQ32;
      if (frameQ32 < period.TicksQ32)
        return 1;
      long whole = frameQ32 / period.TicksQ32;
      long rest = frameQ32 - (whole * period.TicksQ32);
      long needed = whole + (rest > RemainderMarginTicks * OneTickQ32 ? 1 : 0);
      return (uint)Math.Min(needed, PacerSettings.MaxSwapInterval);
    }

    // 100 * late / frames rounded to a whole percent, a half to the even one (as Python's round in the simulation)
    private static long LatePercent(uint late, uint frames)
    {
      long numerator = 100L * late;
      long count = frames;
      long whole = numerator / count;
      long twiceRest = 2 * (numerator % count);
      return whole + ((twiceRest > count || (twiceRest == count && whole % 2 == 1)) ? 1 : 0);
    }

    // The frames a full window holds at swapInterval: windowTicks / (swapInterval * period), rounded to the nearest
    private static long FullWindowFrames(long windowTicks, uint swapInterval, RefreshPeriod period)
    {
      long numerator = windowTicks * OneTickQ32;
      long denominator = swapInterval * period.TicksQ32;
      return (numerator + (denominator / 2)) / denominator;
    }

    private readonly struct Entry
    {
      public Entry(long displayTicks, long workTicks, bool late)
      {
        DisplayTicks = displayTicks;
        WorkTicks = workTicks;
        Late = late;
      }

      public readonly long DisplayTicks;

      public readonly long WorkTicks;

      public readonly bool Late;
    }
  }
}
