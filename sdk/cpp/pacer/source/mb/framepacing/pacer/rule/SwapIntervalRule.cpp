// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The swap interval rule of sdk/doc/pacer.md: the adaptive swap interval rule (as mb-framepacing-explained simulates it in
// tools/frame_pacing_video/adaptive_rate.py) and the late count fix. Integer arithmetic on whole nanoseconds only, so every port decides
// exactly alike.
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <algorithm>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! The most frames a window is allocated for
    constexpr int64_t MaxCapacity = int64_t{1} << 20;

    std::size_t Capacity(const PacerSettings& settings) noexcept
    {
      // The frames of a window at the preferred swap interval, and the frame on either side; twice that for a faster display
      const NanosecondTimeSpan frameTime = settings.Refresh().TimeFor(settings.PreferredSwapIntervalAt(settings.Refresh()));
      const int64_t frames = (settings.FrameWindowLength().Nanoseconds() / frameTime.Nanoseconds()) + 2;
      return static_cast<std::size_t>(std::min(frames * 2, MaxCapacity));
    }

    //! The swap interval a frame of frameTime needs: the fewest refreshes that take at least as long, from 1 to
    //! PacerSettings::MaxSwapInterval.
    uint32_t NeededSwapInterval(const NanosecondTimeSpan frameTime, const RefreshPeriod period) noexcept
    {
      return static_cast<uint32_t>(std::clamp(period.RefreshesToFit(frameTime), int64_t{1}, int64_t{PacerSettings::MaxSwapInterval}));
    }

    //! 100 * late / frames rounded to a whole percent, a half to the even one (as Python's round in the simulation).
    int64_t LatePercent(const uint32_t late, const uint32_t frames) noexcept
    {
      const int64_t numerator = int64_t{100} * late;
      const int64_t count = frames;
      const int64_t whole = numerator / count;
      const int64_t twiceRest = 2 * (numerator % count);
      return whole + ((twiceRest > count || (twiceRest == count && whole % 2 == 1)) ? 1 : 0);
    }

    //! The frames a full window holds at swapInterval: the window's length in frame times, rounded to the nearest.
    int64_t FullWindowFrames(const NanosecondTimeSpan windowLength, const uint32_t swapInterval, const RefreshPeriod period) noexcept
    {
      const int64_t frameNanoseconds = period.TimeFor(swapInterval).Nanoseconds();
      return (windowLength.Nanoseconds() + (frameNanoseconds / 2)) / frameNanoseconds;
    }

    //! a + b, for spans that are far from the ends of NanosecondTimeSpan's range (NanosecondTimeSpan's own + throws there, and the rule never does)
    constexpr NanosecondTimeSpan Sum(const NanosecondTimeSpan a, const NanosecondTimeSpan b) noexcept
    {
      return NanosecondTimeSpan(a.Nanoseconds() + b.Nanoseconds());
    }
  }

  SwapIntervalRule::SwapIntervalRule(const PacerSettings& settings)
    : m_settings(settings)
    , m_entries(Capacity(settings))
    , m_preferredSwapInterval(settings.PreferredSwapIntervalAt(settings.Refresh()))
    , m_swapInterval(m_preferredSwapInterval)
  {
  }

  SwapIntervalChange SwapIntervalRule::AddFrame(const NanosecondTimeSpan displayTime, const NanosecondTimeSpan work, const bool late,
                                                const NanosecondTimeSpan nextStartAhead) noexcept
  {
    const NanosecondTimeSpan windowLength = m_settings.FrameWindowLength();
    // The frames of the last FrameWindowLength: the oldest go once the second oldest is more than FrameWindowLength older than this one (so the
    // window keeps one frame beyond it, as the reference simulation's does), and when the window holds all it can
    if (m_count == m_entries.size())
    {
      PopFront();
    }
    const NanosecondTimeSpan counted = std::clamp(work, NanosecondTimeSpan(), windowLength);
    // A late frame's next start says how late it was, not whether the loop runs ahead: left out
    const NanosecondTimeSpan ahead = late ? NanosecondTimeSpan() : nextStartAhead;
    m_entries[(m_first + m_count) % m_entries.size()] = Entry{displayTime, counted, late, ahead};
    ++m_count;
    m_workSum = Sum(m_workSum, counted);
    m_lateCount += late ? 1u : 0u;
    m_startsAhead = Sum(m_startsAhead, ahead);
    while (m_count >= 2u && displayTime > Sum(At(1).DisplayTime, windowLength))
    {
      PopFront();
    }

    if (!m_settings.AutoSwapInterval())
    {
      return SwapIntervalChange::Unchanged;
    }
    const auto frames = static_cast<uint32_t>(m_count);
    const bool full = IsFull();
    const RefreshPeriod period = m_settings.Refresh();
    const NanosecondTimeSpan margin = m_settings.FrameMargin();
    // What a frame needs: the frames' average work, and the margin
    const NanosecondTimeSpan frameTime(m_workSum.Nanoseconds() / frames + margin.Nanoseconds());

    const bool mayGoSlower =
      m_swapInterval < PacerSettings::MaxSwapInterval && period.TimeFor(m_swapInterval) <= Sum(m_settings.SlowestFrameTime(), margin);
    const bool slowerByShare = full && LatePercent(m_lateCount, frames) > m_settings.SlowDownLatePercent();
    // The fix: as many late frames as would make a full window's share late, without waiting for the window to fill again
    const bool slowerByCount = m_settings.SlowDown() == SlowDownRule::LateCount &&
                               int64_t{100} * m_lateCount > m_settings.SlowDownLatePercent() * FullWindowFrames(windowLength, m_swapInterval, period);

    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    uint32_t swapInterval = m_swapInterval;
    if (mayGoSlower && (slowerByShare || slowerByCount))
    {
      swapInterval = std::max(m_swapInterval + 1u, NeededSwapInterval(frameTime, period));
      change = SwapIntervalChange::Slower;
    }
    else if (full && m_lateCount == 0 && m_swapInterval > m_preferredSwapInterval && Sum(frameTime, margin) < period.TimeFor(m_swapInterval - 1u))
    {
      swapInterval = std::max(m_preferredSwapInterval, NeededSwapInterval(frameTime, period));
      change = SwapIntervalChange::Faster;
    }
    if (change != SwapIntervalChange::Unchanged)
    {
      m_swapInterval = swapInterval;
      Clear();
    }
    return change;
  }

  void SwapIntervalRule::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    m_settings.SetRefresh(period);
    m_preferredSwapInterval = m_settings.PreferredSwapIntervalAt(period);
    m_swapInterval = m_preferredSwapInterval;
    Clear();
  }

  void SwapIntervalRule::SetSettings(const PacerSettings& settings)
  {
    if (settings == m_settings)
    {
      return;
    }
    // More room first: when there is no memory for it the rule is as it was
    const std::size_t capacity = Capacity(settings);
    if (capacity > m_entries.size())
    {
      m_entries.resize(capacity);
    }
    m_settings = settings;
    m_preferredSwapInterval = settings.PreferredSwapIntervalAt(settings.Refresh());
    m_swapInterval = m_preferredSwapInterval;
    Clear();
  }

  void SwapIntervalRule::Reset(const uint32_t swapInterval) noexcept
  {
    m_swapInterval = std::clamp(swapInterval, m_preferredSwapInterval, PacerSettings::MaxSwapInterval);
    Clear();
  }

  void SwapIntervalRule::Clear() noexcept
  {
    m_first = 0;
    m_count = 0;
    m_workSum = NanosecondTimeSpan();
    m_lateCount = 0;
    m_startsAhead = NanosecondTimeSpan();
  }

  FrameWindowState SwapIntervalRule::FrameWindow() const noexcept
  {
    if (m_count == 0)
    {
      return {};
    }
    const NanosecondTimeSpan span(At(m_count - 1u).DisplayTime.Nanoseconds() - At(0).DisplayTime.Nanoseconds());
    return {static_cast<uint32_t>(m_count),
            m_lateCount,
            NanosecondTimeSpan(m_workSum.Nanoseconds() / static_cast<int64_t>(m_count)),
            span,
            IsFull(),
            m_startsAhead};
  }

  const SwapIntervalRule::Entry& SwapIntervalRule::At(const std::size_t index) const noexcept
  {
    return m_entries[(m_first + index) % m_entries.size()];
  }

  bool SwapIntervalRule::IsFull() const noexcept
  {
    // Called with a frame in the window. More than FrameWindowLength from its oldest frame to its newest; a window that holds all the
    // frames it can is full too: after a change to a much faster display it is too small for FrameWindowLength of frames, and would
    // otherwise never decide
    return m_count == m_entries.size() || At(m_count - 1u).DisplayTime > Sum(At(0).DisplayTime, m_settings.FrameWindowLength());
  }

  void SwapIntervalRule::PopFront() noexcept
  {
    const Entry& oldest = At(0);
    m_workSum = NanosecondTimeSpan(m_workSum.Nanoseconds() - oldest.Work.Nanoseconds());
    m_lateCount -= oldest.Late ? 1u : 0u;
    m_startsAhead = NanosecondTimeSpan(m_startsAhead.Nanoseconds() - oldest.NextStartAhead.Nanoseconds());
    m_first = (m_first + 1u) % m_entries.size();
    --m_count;
  }
}
