//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The animation timer aligned to display time slots: the vsync timer of mb-framepacing-explained (sdk/doc/pacer.md). On a display with a fixed
//* refresh every frame is shown a whole number of refreshes after the previous one, and animates for its predicted display time: the previous
//* frame's display plus its swap interval. So the animation time advances in whole refreshes: by the refreshes between the pacer's intended
//* display times (Advance), or, without a pacer, by the time since the previous frame rounded to whole refreshes, corrected for a change of swap
//* interval (AdvanceMeasured). A late frame shows a moment already past, and the next frame catches up exactly. The steps add up in 2^-32 ticks,
//* so the animation time never drifts from the refreshes, and is never pulled back in whole refreshes. No allocation after construction.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer
{
  public sealed class AnimationClock
  {
    private const long OneTickQ32 = RefreshPeriod.OneTickQ32;

    private readonly long m_maxStepRefreshes;
    private RefreshPeriod m_period;

    // The animation time: m_ticks + m_fraction / 2^32
    private long m_ticks;
    private uint m_fraction;
    private long m_lastTicks;
    private long m_lastSwapInterval;
    private bool m_hasLast;
    private AnimationTime m_current;

    /// <summary>
    /// <paramref name="startTicks"/>: the animation time of the first frame. <paramref name="maxStepRefreshes"/>: the longest step (0 = no limit; a
    /// negative value is 0), for a process that was suspended or a debugger that stopped it.
    /// </summary>
    /// <exception cref="ArgumentException">For default(RefreshPeriod).</exception>
    public AnimationClock(RefreshPeriod period, long startTicks = 0, long maxStepRefreshes = 0)
    {
      m_period = PacerSettings.RequireRefresh(period, nameof(period));
      m_maxStepRefreshes = Math.Max(maxStepRefreshes, 0);
      m_ticks = startTicks;
      m_current = new AnimationTime(startTicks, 0, 0);
    }

    /// <summary>The last frame's animation time.</summary>
    public AnimationTime Current => m_current;

    public bool IsPaused { get; private set; }

    /// <summary>With the pacer: the step is the whole refreshes from the previous frame's intended display time to this one's.</summary>
    public AnimationTime Advance(in FrameSchedule schedule)
    {
      // The intended display times are the previous display plus the swap interval already: the step is the refreshes between them
      long refreshes = m_hasLast ? m_period.NearestRefreshes(schedule.IntendedDisplayTicks - m_lastTicks) : 0;
      m_lastTicks = schedule.IntendedDisplayTicks;
      m_lastSwapInterval = schedule.SwapInterval;
      m_hasLast = true;
      return Step(refreshes);
    }

    /// <summary>
    /// Without a pacer, in a loop paced by vsync (it waits in Present, so every frame starts when the previous one is shown): the time since the
    /// previous frame's wake-up is how long the frame before stayed on screen, rounded to whole refreshes and at least that frame's swap interval.
    /// The step is that, minus the previous frame's swap interval, plus this frame's (<paramref name="swapInterval"/>): each frame animates for the
    /// previous frame's display plus its own swap interval. With one swap interval the step is the rounded time. Rounding removes the wake-ups'
    /// jitter while it stays under half a refresh.
    /// </summary>
    public AnimationTime AdvanceMeasured(long wakeUpTicks, long swapInterval)
    {
      long interval = Math.Max(swapInterval, 1);
      long refreshes = 0;
      if (m_hasLast)
      {
        // How long the previous frame's predecessor stayed on screen, at least the previous frame's swap interval (the jitter can make the wake-up
        // early), then the change from the previous frame's swap interval to this one's
        long stayed = Math.Max(m_period.NearestRefreshes(wakeUpTicks - m_lastTicks), m_lastSwapInterval);
        refreshes = stayed - m_lastSwapInterval + interval;
      }
      m_lastTicks = wakeUpTicks;
      m_lastSwapInterval = interval;
      m_hasLast = true;
      return Step(refreshes);
    }

    /// <summary>While paused the animation time holds; after Resume it goes on from there, without a jump.</summary>
    public void Pause() => IsPaused = true;

    public void Resume() => IsPaused = false;

    /// <summary>The display's refresh period changed; the animation time goes on from where it is.</summary>
    /// <exception cref="ArgumentException">For default(RefreshPeriod).</exception>
    public void SetRefreshPeriod(RefreshPeriod period) => m_period = PacerSettings.RequireRefresh(period, nameof(period));

    private AnimationTime Step(long refreshes)
    {
      if (m_maxStepRefreshes > 0)
        refreshes = Math.Min(refreshes, m_maxStepRefreshes);
      if (IsPaused || refreshes < 0)
        refreshes = 0;

      long before = m_ticks + ((long)m_fraction >= OneTickQ32 / 2 ? 1 : 0);
      // refreshes times the period, added in 2^-32 ticks: the whole ticks and the fraction apart, so a long step cannot overflow
      long whole = m_period.TicksQ32 >> 32;
      ulong fraction = (ulong)m_period.TicksQ32 & 0xFFFF_FFFFUL;
      ulong fractionSum = m_fraction + ((ulong)refreshes * fraction);
      m_ticks += (refreshes * whole) + (long)(fractionSum >> 32);
      m_fraction = (uint)(fractionSum & 0xFFFF_FFFFUL);
      long after = m_ticks + ((long)m_fraction >= OneTickQ32 / 2 ? 1 : 0);
      m_current = new AnimationTime(after, after - before, refreshes);
      return m_current;
    }
  }
}
