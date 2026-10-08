// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The wait for a present as every tier that has one makes it (sdk/doc/pacer-design.md, "How a pacer is put
// together").
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/pacer/hold/PresentWaitRule.hpp>
#include <algorithm>

namespace MB::FramePacing::Pacer
{
  FrameStartPlan PresentWaitRule::Plan(const PacerSettings& settings, const RefreshPeriod period, const uint32_t swapInterval) const noexcept
  {
    FrameStartPlan plan;
    // The present to wait for: the one WaitingPresents back from the frame that is about to be made. While the waits run out
    // nothing is waited for: the plan asks after a present that many frames older, which has had the time a wait would give
    const bool stopped = Stopped();
    const uint64_t back = (uint64_t{settings.WaitingPresents()} - 1u) + (stopped ? settings.PresentWaitSwapIntervals() : 0u);
    // Asking is not free everywhere: while stopped it is done once in a number of frames
    const bool asks = !stopped || m_framesSinceAsk >= FramesBetweenAsks;
    if (asks && !m_waitReported && m_lastAcceptedId > back && (m_lastAcceptedId - back) >= m_oldestWaitableId &&
        (m_lastAcceptedId - back) != m_waitedForId && (!stopped || (m_lastAcceptedId - back) >= m_runOutFromId))
    {
      plan.WaitForPresentFrameId = m_lastAcceptedId - back;
      // As long as a few of the frame's own swap intervals, and the least time a wait is given: a present that is never
      // shown holds the loop no longer
      plan.WaitForPresentTimeout = stopped ? NanosecondTimeDuration() : settings.WaitTimeoutAt(period, swapInterval);
    }
    return plan;
  }

  bool PresentWaitRule::HeldTheLoop(const PresentWaitReport& report, const RefreshPeriod period) noexcept
  {
    return report.Blocked().Nanoseconds() >= (period.ToNanosecondTimeSpan().Nanoseconds() / HeldDivisor);
  }

  bool PresentWaitRule::AddPresentWait(const PresentWaitReport& report, const RefreshPeriod period) noexcept
  {
    m_waitedForId = report.FrameId;
    m_waitReported = true;
    const bool heldTheLoop = HeldTheLoop(report, period);
    // A wait that ran out, and every answer while the waits are stopped: the window is not shown, or was not a moment ago
    m_disturbed = m_disturbed || !report.Shown || Stopped();
    if (Stopped())
    {
      // The answer to what the plan asked. Answers in a row that say shown end the stop: one by itself can be a frame of a
      // window that is still covered, shown in passing. An ask that held the loop is the pacer's doing as a wait is
      m_framesSinceAsk = 0;
      m_timeouts += report.Shown ? 0u : 1u;
      m_shownAsks = report.Shown ? m_shownAsks + 1u : 0u;
      m_waitRanOut = m_waitRanOut || heldTheLoop;
      if (m_shownAsks >= AsksShownToWait)
      {
        m_waitsRunOut = 0;
      }
      return false;
    }
    if (!report.Shown)
    {
      ++m_timeouts;
      m_runOutFromId = m_waitsRunOut == 0 ? report.FrameId : m_runOutFromId;
      m_waitsRunOut = std::min(m_waitsRunOut + 1u, WaitsRunOutToStop);
      m_framesSinceAsk = 0;
      m_shownAsks = 0;
      // The frame that starts after a wait that held the loop until it ran out starts late because the pacer asked for the wait
      m_waitRanOut = m_waitRanOut || heldTheLoop;
      return false;
    }
    m_waitsRunOut = 0;
    return true;
  }

  void PresentWaitRule::AddPresent(const PresentReport& report) noexcept
  {
    if (report.Accepted)
    {
      m_lastAcceptedId = report.FrameId;
      // A frame presented again after its first present was not taken: this present is one to wait for
      m_oldestWaitableId = std::min(m_oldestWaitableId, report.FrameId);
    }
    else
    {
      // The present will not be shown, and the swap chain it was made for is gone with the presents before it
      m_oldestWaitableId = report.FrameId + 1u;
    }
  }

  void PresentWaitRule::ForgetPresents() noexcept
  {
    m_oldestWaitableId = m_lastAcceptedId + 1u;
    // And with them the waits that ran out: a new swap chain's presents are waited for again
    m_waitsRunOut = 0;
    m_shownAsks = 0;
  }

  void PresentWaitRule::BeginFrame() noexcept
  {
    m_waitRanOut = false;
    m_waitReported = false;
    m_disturbed = false;
    m_framesSinceAsk = Stopped() ? std::min(m_framesSinceAsk + 1u, FramesBetweenAsks) : 0u;
  }

  void PresentWaitRule::Reset() noexcept
  {
    ForgetPresents();
    m_waitRanOut = false;
    m_disturbed = false;
  }
}
