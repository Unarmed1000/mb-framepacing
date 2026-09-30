// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The frame pacer of sdk/doc/pacer.md. Refresh times come from an exact grid: an origin refresh (ticks and a 2^-32 fraction) and the
// period in the same fixed point, so no refresh time drifts. Integer arithmetic only, so C++ and C# plan exactly alike.
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <algorithm>
#include <limits>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    constexpr int64_t OneTickQ32 = RefreshPeriod::OneTickQ32;
    //! The grid is used within this distance of its origin (30 s, so an offset times the period in 2^-32 ticks fits 62 bits); a frame
    //! further away starts a new grid, as after a pause
    constexpr int64_t MaxGridTicks = 30 * TicksPerSecond;

    uint32_t ToTicks32(const int64_t ticks) noexcept
    {
      return static_cast<uint32_t>(std::clamp(ticks, int64_t{0}, static_cast<int64_t>(std::numeric_limits<uint32_t>::max())));
    }
  }

  FramePacer::FramePacer(const PacerSettings& settings)
    : m_rule(settings)
    , m_period(settings.Refresh())
  {
  }

  FrameSchedule FramePacer::BeginFrame(const FrameInput& input) noexcept
  {
    const int64_t now = input.NowTicks;
    if (input.RefreshPeriodNanoseconds != 0 && input.RefreshPeriodNanoseconds != m_period.Nanoseconds())
    {
      // The platform reports another refresh period: a display mode change
      SetRefreshPeriod(RefreshPeriod::FromNanoseconds(input.RefreshPeriodNanoseconds));
    }
    SwapIntervalChange change = SwapIntervalChange::None;
    int64_t base = 0;
    if (!m_started)
    {
      // The first frame: the grid starts at the vsync the platform reported (when it is near now), or now, and the frame starts from
      // the refresh it is in
      const bool vsyncNearNow = input.VsyncTicks != 0 && input.VsyncTicks - now <= MaxGridTicks && now - input.VsyncTicks <= MaxGridTicks;
      Anchor(vsyncNearNow ? input.VsyncTicks : now, m_originSlot);
      m_started = true;
      m_frameOpen = false;
      base = SlotAtOrBefore(now);
    }
    else
    {
      if (m_frameOpen)
      {
        if (!m_frameEnded)
        {
          // No EndFrame: the frame is taken as presented now
          m_presentTicks = now;
          m_workTicks = std::max(now - m_cpuStartTicks, int64_t{0});
        }
        // The previous frame's display: reported, or inferred as the first refresh it can make after its Present, never before its target
        int64_t displayed = 0;
        int64_t displayTicks = 0;
        if (input.PreviousDisplayTicks != 0 && IsNearGrid(input.PreviousDisplayTicks))
        {
          displayed = NearestSlot(input.PreviousDisplayTicks);
          displayTicks = input.PreviousDisplayTicks;
          Anchor(input.PreviousDisplayTicks, displayed);
        }
        else
        {
          displayed = std::max(m_targetSlot, SlotAtOrAfter(m_presentTicks + Settings().PresentLatencyTicks()));
          displayTicks = SlotTicks(displayed);
          MoveOrigin(displayed);
        }
        change = m_rule.AddFrame(displayTicks, m_workTicks, displayed > m_targetSlot, m_period);
        base = displayed;
      }
      else
      {
        base = IsNearGrid(now) ? SlotAtOrBefore(now) : m_originSlot;
      }
      if (!IsNearGrid(now))
      {
        // A long pause: a new grid from now, and the window's frames from before it do not count
        base += 1;
        Anchor(now, base);
        m_rule.Clear();
      }
    }
    if (input.VsyncTicks != 0 && IsNearGrid(input.VsyncTicks))
    {
      Anchor(input.VsyncTicks, NearestSlot(input.VsyncTicks));
    }

    const uint32_t swapInterval = m_rule.SwapInterval();
    int64_t target = std::max(base + swapInterval, SlotAtOrAfter(now));
    if (input.PredictedDisplayTicks != 0 && IsNearGrid(input.PredictedDisplayTicks))
    {
      const int64_t predicted = NearestSlot(input.PredictedDisplayTicks);
      Anchor(input.PredictedDisplayTicks, predicted);
      target = std::max(target, predicted);
    }

    m_frameOpen = true;
    m_frameEnded = false;
    m_targetSlot = target;
    m_cpuStartTicks = now;
    FrameSchedule schedule;
    schedule.FrameIndex = m_nextFrameIndex++;
    schedule.IntendedDisplayTicks = SlotTicks(target);
    schedule.EarliestPresentTicks = SlotTicks(target - 1);
    schedule.SwapInterval = swapInterval;
    schedule.TargetFrameTicks = ToTicks32(m_period.TicksFor(swapInterval));
    schedule.PreferredFrameTicks = ToTicks32(m_period.TicksFor(Settings().PreferredSwapInterval()));
    schedule.CpuStartTicks = now;
    schedule.Change = change;
    return schedule;
  }

  uint32_t FramePacer::EndFrame(const FrameEnd& end) noexcept
  {
    if (!m_frameOpen)
    {
      return 0;
    }
    const int64_t busy = std::max(end.PresentTicks - m_cpuStartTicks, int64_t{0});
    m_presentTicks = end.PresentTicks;
    m_workTicks = end.WorkTicks > 0 ? end.WorkTicks : busy;
    m_frameEnded = true;
    return ToTicks32(busy);
  }

  void FramePacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_period)
    {
      m_period = period;
      Reset();
    }
  }

  void FramePacer::Reset() noexcept
  {
    m_started = false;
    m_frameOpen = false;
    m_frameEnded = false;
    m_rule.Reset(Settings().PreferredSwapInterval());
  }

  int64_t FramePacer::SlotTicks(const int64_t slot) const noexcept
  {
    // Rounded to the nearest tick; >> on a negative value rounds down, as the floor the rounding needs
    const int64_t offsetQ32 = static_cast<int64_t>(m_originFraction) + ((slot - m_originSlot) * m_period.TicksQ32()) + (OneTickQ32 / 2);
    return m_originTicks + (offsetQ32 >> 32);
  }

  int64_t FramePacer::SlotAtOrBefore(const int64_t ticks) const noexcept
  {
    // An estimate from the rounded period (at least a tick), then exact (the rounding is at most half a tick a refresh)
    int64_t slot = m_originSlot + ((ticks - m_originTicks) / m_period.Ticks());
    while (SlotTicks(slot) > ticks)
    {
      --slot;
    }
    while (SlotTicks(slot + 1) <= ticks)
    {
      ++slot;
    }
    return slot;
  }

  int64_t FramePacer::SlotAtOrAfter(const int64_t ticks) const noexcept
  {
    const int64_t slot = SlotAtOrBefore(ticks);
    return SlotTicks(slot) < ticks ? slot + 1 : slot;
  }

  int64_t FramePacer::NearestSlot(const int64_t ticks) const noexcept
  {
    const int64_t slot = SlotAtOrBefore(ticks);
    // The later refresh on a tie
    return (ticks - SlotTicks(slot)) * 2 >= SlotTicks(slot + 1) - SlotTicks(slot) ? slot + 1 : slot;
  }

  bool FramePacer::IsNearGrid(const int64_t ticks) const noexcept
  {
    return ticks - m_originTicks <= MaxGridTicks && m_originTicks - ticks <= MaxGridTicks;
  }

  void FramePacer::Anchor(const int64_t ticks, const int64_t slot) noexcept
  {
    m_originTicks = ticks;
    m_originFraction = 0;
    m_originSlot = slot;
  }

  void FramePacer::MoveOrigin(const int64_t slot) noexcept
  {
    // The exact position of slot on the grid: the origin moves along it without rounding, so the grid never drifts
    const int64_t positionQ32 = static_cast<int64_t>(m_originFraction) + ((slot - m_originSlot) * m_period.TicksQ32());
    m_originTicks += positionQ32 >> 32;
    m_originFraction = static_cast<uint32_t>(static_cast<uint64_t>(positionQ32) & 0xFFFF'FFFFu);
    m_originSlot = slot;
  }
}
