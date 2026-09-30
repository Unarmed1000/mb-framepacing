//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Paces frames on a grid of refreshes (sdk/doc/pacer.md). Every frame: BeginFrame with what the platform knows, apply the schedule (a swap
//* interval, a present time, or sleep until EarliestPresentTicks), draw, EndFrame when presenting. The pacer aims every frame at the refresh one
//* swap interval after the previous frame's, and the swap interval rule adapts the interval to how long frames take. Values in, values out: the
//* pacer calls no platform API and never reads a clock. Made once (it allocates the rule's window); nothing after that allocates.
//*
//* Refresh times come from an exact grid: an origin refresh (ticks and a 2^-32 fraction) and the period in the same fixed point, so no refresh
//* time drifts. Integer arithmetic only, the C++ library's to the tick.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer
{
  public sealed class FramePacer
  {
    private const long OneTickQ32 = RefreshPeriod.OneTickQ32;

    // The grid is used within this distance of its origin (30 s, so an offset times the period in 2^-32 ticks fits 62 bits); a frame further away
    // starts a new grid, as after a pause
    private const long MaxGridTicks = 30 * TimeSpan.TicksPerSecond;

    private readonly PacerSettings m_settings;
    private readonly SwapIntervalRule m_rule;
    private RefreshPeriod m_period;

    // The refresh grid: refresh m_originSlot is at m_originTicks + m_originFraction / 2^32 ticks
    private long m_originTicks;
    private uint m_originFraction;
    private long m_originSlot;
    private bool m_started;
    private ulong m_nextFrameIndex;

    // The frame between BeginFrame and the next BeginFrame
    private bool m_frameOpen;
    private bool m_frameEnded;
    private long m_targetSlot;
    private long m_cpuStartTicks;
    private long m_presentTicks;
    private long m_workTicks;

    /// <summary>Copies the settings and allocates the rule's window: the only allocation.</summary>
    public FramePacer(PacerSettings settings)
    {
      if (settings == null)
        throw new ArgumentNullException(nameof(settings));
      m_settings = settings.Clone();
      m_rule = new SwapIntervalRule(m_settings);
      m_period = m_settings.Refresh;
    }

    /// <summary>What the rule's window holds now.</summary>
    public WindowState Window => m_rule.Window;

    /// <summary>The swap interval the next frame is paced at.</summary>
    public uint SwapInterval => m_rule.SwapInterval;

    /// <summary>The refresh period the pacer paces at now (PacerSettings.Refresh until the period changes).</summary>
    public RefreshPeriod Refresh => m_period;

    /// <summary>A copy of the settings the pacer was made with.</summary>
    public PacerSettings Settings => m_settings.Clone();

    /// <summary>
    /// Start a frame: the previous frame's display is resolved (from PreviousDisplayTicks, or inferred from its Present), the rule decides, and
    /// this frame is planned.
    /// </summary>
    public FrameSchedule BeginFrame(in FrameInput input)
    {
      long now = input.NowTicks;
      if (input.RefreshPeriodNanoseconds != 0 && input.RefreshPeriodNanoseconds != m_period.Nanoseconds)
      {
        // The platform reports another refresh period: a display mode change
        SetRefreshPeriod(RefreshPeriod.FromNanoseconds(input.RefreshPeriodNanoseconds));
      }
      var change = SwapIntervalChange.None;
      long baseSlot;
      if (!m_started)
      {
        // The first frame: the grid starts at the vsync the platform reported (when it is near now), or now, and the frame starts from the
        // refresh it is in
        bool vsyncNearNow = input.VsyncTicks != 0 && input.VsyncTicks - now <= MaxGridTicks && now - input.VsyncTicks <= MaxGridTicks;
        Anchor(vsyncNearNow ? input.VsyncTicks : now, m_originSlot);
        m_started = true;
        m_frameOpen = false;
        baseSlot = SlotAtOrBefore(now);
      }
      else
      {
        if (m_frameOpen)
        {
          if (!m_frameEnded)
          {
            // No EndFrame: the frame is taken as presented now
            m_presentTicks = now;
            m_workTicks = Math.Max(now - m_cpuStartTicks, 0);
          }
          // The previous frame's display: reported, or inferred as the first refresh it can make after its Present, never before its target
          long displayed;
          long displayTicks;
          if (input.PreviousDisplayTicks != 0 && IsNearGrid(input.PreviousDisplayTicks))
          {
            displayed = NearestSlot(input.PreviousDisplayTicks);
            displayTicks = input.PreviousDisplayTicks;
            Anchor(input.PreviousDisplayTicks, displayed);
          }
          else
          {
            displayed = Math.Max(m_targetSlot, SlotAtOrAfter(m_presentTicks + m_settings.PresentLatencyTicks));
            displayTicks = SlotTicks(displayed);
            MoveOrigin(displayed);
          }
          change = m_rule.AddFrame(displayTicks, m_workTicks, displayed > m_targetSlot, m_period);
          baseSlot = displayed;
        }
        else
        {
          baseSlot = IsNearGrid(now) ? SlotAtOrBefore(now) : m_originSlot;
        }
        if (!IsNearGrid(now))
        {
          // A long pause: a new grid from now, and the window's frames from before it do not count
          baseSlot += 1;
          Anchor(now, baseSlot);
          m_rule.Clear();
        }
      }
      if (input.VsyncTicks != 0 && IsNearGrid(input.VsyncTicks))
        Anchor(input.VsyncTicks, NearestSlot(input.VsyncTicks));

      uint swapInterval = m_rule.SwapInterval;
      long target = Math.Max(baseSlot + swapInterval, SlotAtOrAfter(now));
      if (input.PredictedDisplayTicks != 0 && IsNearGrid(input.PredictedDisplayTicks))
      {
        long predicted = NearestSlot(input.PredictedDisplayTicks);
        Anchor(input.PredictedDisplayTicks, predicted);
        target = Math.Max(target, predicted);
      }

      m_frameOpen = true;
      m_frameEnded = false;
      m_targetSlot = target;
      m_cpuStartTicks = now;
      return new FrameSchedule(
        m_nextFrameIndex++,
        SlotTicks(target),
        SlotTicks(target - 1),
        swapInterval,
        ToTicks32(m_period.TicksFor(swapInterval)),
        ToTicks32(m_period.TicksFor(m_settings.PreferredSwapInterval)),
        now,
        change
      );
    }

    /// <summary>The frame is presented now. Returns the CPU busy time (PresentTicks - the frame's CpuStartTicks) for the marker.</summary>
    public uint EndFrame(in FrameEnd end)
    {
      if (!m_frameOpen)
        return 0;
      long busy = Math.Max(end.PresentTicks - m_cpuStartTicks, 0);
      m_presentTicks = end.PresentTicks;
      m_workTicks = end.WorkTicks > 0 ? end.WorkTicks : busy;
      m_frameEnded = true;
      return ToTicks32(busy);
    }

    /// <summary>
    /// The display's refresh period changed (a mode change, another monitor): the pacer starts again on a new grid with an empty window at the
    /// preferred swap interval. The frame count goes on. The period the pacer has already changes nothing. A platform that reports the period with
    /// every frame passes it in FrameInput.RefreshPeriodNanoseconds instead.
    /// </summary>
    /// <exception cref="ArgumentException">For default(RefreshPeriod).</exception>
    public void SetRefreshPeriod(RefreshPeriod period)
    {
      PacerSettings.RequireRefresh(period, nameof(period));
      if (period != m_period)
      {
        m_period = period;
        Reset();
      }
    }

    /// <summary>Start again (after a pause): the next frame is planned as the first, the window is empty, the swap interval the preferred one.</summary>
    public void Reset()
    {
      m_started = false;
      m_frameOpen = false;
      m_frameEnded = false;
      m_rule.Reset(m_settings.PreferredSwapInterval);
    }

    private static uint ToTicks32(long ticks) => (uint)Math.Clamp(ticks, 0, uint.MaxValue);

    private long SlotTicks(long slot)
    {
      // Rounded to the nearest tick; >> on a negative value rounds down, as the floor the rounding needs
      long offsetQ32 = (long)m_originFraction + ((slot - m_originSlot) * m_period.TicksQ32) + (OneTickQ32 / 2);
      return m_originTicks + (offsetQ32 >> 32);
    }

    private long SlotAtOrBefore(long ticks)
    {
      // An estimate from the rounded period (at least a tick), then exact (the rounding is at most half a tick a refresh)
      long slot = m_originSlot + ((ticks - m_originTicks) / m_period.Ticks);
      while (SlotTicks(slot) > ticks)
        --slot;
      while (SlotTicks(slot + 1) <= ticks)
        ++slot;
      return slot;
    }

    private long SlotAtOrAfter(long ticks)
    {
      long slot = SlotAtOrBefore(ticks);
      return SlotTicks(slot) < ticks ? slot + 1 : slot;
    }

    private long NearestSlot(long ticks)
    {
      long slot = SlotAtOrBefore(ticks);
      // The later refresh on a tie
      return (ticks - SlotTicks(slot)) * 2 >= SlotTicks(slot + 1) - SlotTicks(slot) ? slot + 1 : slot;
    }

    private bool IsNearGrid(long ticks) => ticks - m_originTicks <= MaxGridTicks && m_originTicks - ticks <= MaxGridTicks;

    private void Anchor(long ticks, long slot)
    {
      m_originTicks = ticks;
      m_originFraction = 0;
      m_originSlot = slot;
    }

    private void MoveOrigin(long slot)
    {
      // The exact position of slot on the grid: the origin moves along it without rounding, so the grid never drifts
      long positionQ32 = (long)m_originFraction + ((slot - m_originSlot) * m_period.TicksQ32);
      m_originTicks += positionQ32 >> 32;
      m_originFraction = (uint)((ulong)positionQ32 & 0xFFFF_FFFFUL);
      m_originSlot = slot;
    }
  }
}
