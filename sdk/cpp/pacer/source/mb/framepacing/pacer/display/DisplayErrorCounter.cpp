// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The animation error from the display times an application reports (sdk/doc/pacer-design.md, the "+" beside a
// tier): statistics by the measuring tools' rules. Nothing here paces a frame.
#include <mb/framepacing/pacer/display/DisplayErrorCounter.hpp>
#include <algorithm>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! The length of one part of the last second: an eighth of a second
    constexpr int64_t BucketNanoseconds = NanosecondTimeSpan::NanosecondsPerSecond / DisplayErrorCounter::RecentBuckets;
    //! A display time step this share of a refresh period or more off the animation time step is at another refresh: one in
    //! this many
    constexpr int64_t OffTargetDivisor = 2;

    constexpr bool IsBeyond(const int64_t value, const int64_t limit) noexcept
    {
      return value > limit || value < -limit;
    }
  }

  void DisplayErrorCounter::AddFrame(const uint64_t frameId, const NanosecondTimeSpan animationStep, const uint32_t intendedRefreshes,
                                     const NanosecondTickCount startTime) noexcept
  {
    if (frameId != m_newestId + 1u)
    {
      // Not the next frame: the ids started again, and the frames before are not this run's
      m_newestId = frameId == 0 ? 0 : frameId - 1u;
      Restart();
    }
    m_newestId = frameId;
    // The oldest frame kept is the newest after a start: no frame is older than it then
    if (m_newestId >= m_oldestId && (m_newestId - m_oldestId) >= Capacity)
    {
      ++m_oldestId;
    }
    m_steps[static_cast<std::size_t>(frameId % Capacity)] = animationStep;
    m_starts[static_cast<std::size_t>(frameId % Capacity)] = startTime;
    m_intendedRefreshes[static_cast<std::size_t>(frameId % Capacity)] = intendedRefreshes;
  }

  void DisplayErrorCounter::AddDisplayReport(const DisplayReport& report, const RefreshPeriod period) noexcept
  {
    const uint64_t frameId = report.FrameId;
    if (frameId <= m_newestReportId || frameId < m_oldestId || frameId > m_newestId)
    {
      ++m_state.Refused;
      return;
    }
    m_newestReportId = frameId;
    ++m_state.Reports;
    if (!report.Shown)
    {
      ++m_state.NotShown;
      // Never shown: the frame after it is measured from the last frame shown, and its animation moved for this frame too. A frame
      // without a report may have been shown: nothing is judged across that
      if (frameId == m_unbrokenId + 1u)
      {
        m_unbrokenId = frameId;
        m_stepsNotShown += m_steps[static_cast<std::size_t>(frameId % Capacity)].Nanoseconds();
      }
      else
      {
        m_shownId = 0;
      }
      return;
    }
    // The part of the last second the frame was shown in
    const int64_t index = report.DisplayTime.Nanoseconds() / BucketNanoseconds;
    Bucket& bucket = m_buckets[static_cast<std::size_t>(static_cast<uint64_t>(index) % RecentBuckets)];
    if (bucket.Index != index)
    {
      bucket = Bucket{};
      bucket.Index = index;
    }
    m_newestBucket = std::max(m_newestBucket, index);

    // From its start to its display: of every frame that was shown, whatever became of the frame before it. A display time
    // before the frame's start is no time a frame took
    const int64_t startToDisplay = report.DisplayTime.Nanoseconds() - m_starts[static_cast<std::size_t>(frameId % Capacity)].Nanoseconds();
    if (startToDisplay >= 0)
    {
      ++bucket.StartToDisplayFrames;
      bucket.StartToDisplayTotal += startToDisplay;
      bucket.StartToDisplayLongest = std::max(bucket.StartToDisplayLongest, startToDisplay);
      ++m_state.StartToDisplayFrames;
      m_startToDisplayTotal += startToDisplay;
      m_startToDisplayLongest = std::max(m_startToDisplayLongest, startToDisplay);
    }

    // Judged against the last frame shown, when every frame since has a report: a step across a frame without a report says
    // nothing of either frame
    if (m_shownId != 0 && frameId == m_unbrokenId + 1u)
    {
      const int64_t animationStep = m_stepsNotShown + m_steps[static_cast<std::size_t>(frameId % Capacity)].Nanoseconds();
      const int64_t displayStep = report.DisplayTime.Nanoseconds() - m_shownTime.Nanoseconds();
      const int64_t error = animationStep - displayStep;
      const int64_t offTarget = period.ToNanosecondTimeSpan().Nanoseconds() / OffTargetDivisor;
      // A frame without an intended period (on demand) has no target, and is never late
      const uint32_t intendedRefreshes = m_intendedRefreshes[static_cast<std::size_t>(frameId % Capacity)];
      const int64_t target = intendedRefreshes > 0 ? period.TimeFor(intendedRefreshes).Nanoseconds() : 0;
      // The sum cancels a stop against a jump: a frame that replaces one never shown has moved as far as it stayed away. Against the
      // target they are two parts, the stop or rush and the step
      const int64_t partLimit = ErrorThreshold.Nanoseconds() + PartRounding.Nanoseconds();
      const bool isError = IsBeyond(error, ErrorThreshold.Nanoseconds()) ||
                           (intendedRefreshes > 0 && (IsBeyond(target - displayStep, partLimit) || IsBeyond(animationStep - target, partLimit)));
      const bool isOffTarget = error >= offTarget || error <= -offTarget;
      // Shown later than its animation time step put it and later than intended: the display time step is half a refresh or more
      // longer than both
      const bool isLate = error <= -offTarget && intendedRefreshes > 0 && displayStep >= target + offTarget;

      ++bucket.Judged;
      bucket.Errors += isError ? 1u : 0u;
      bucket.OffTarget += isOffTarget ? 1u : 0u;
      bucket.Late += isLate ? 1u : 0u;

      ++m_state.JudgedFrames;
      m_state.ErrorFrames += isError ? 1u : 0u;
      m_state.OffTargetFrames += isOffTarget ? 1u : 0u;
      m_state.LateFrames += isLate ? 1u : 0u;
    }
    m_shownId = frameId;
    m_unbrokenId = frameId;
    m_stepsNotShown = 0;
    m_shownTime = report.DisplayTime;
  }

  void DisplayErrorCounter::Restart() noexcept
  {
    m_oldestId = m_newestId + 1u;
    m_newestReportId = m_newestId;
    m_shownId = 0;
  }

  DisplayErrorState DisplayErrorCounter::State() const noexcept
  {
    DisplayErrorState state = m_state;
    state.StartToDisplayTotal = NanosecondTimeDuration::FromNanoseconds(m_startToDisplayTotal);
    state.StartToDisplayLongest = NanosecondTimeDuration::FromNanoseconds(m_startToDisplayLongest);
    int64_t recentTotal = 0;
    int64_t recentLongest = 0;
    for (const Bucket& bucket : m_buckets)
    {
      // The parts of the second that ends with the newest display time
      if (bucket.Index >= 0 && bucket.Index > m_newestBucket - int64_t{RecentBuckets})
      {
        state.RecentJudgedFrames += bucket.Judged;
        state.RecentErrorFrames += bucket.Errors;
        state.RecentOffTargetFrames += bucket.OffTarget;
        state.RecentLateFrames += bucket.Late;
        state.RecentStartToDisplayFrames += bucket.StartToDisplayFrames;
        recentTotal += bucket.StartToDisplayTotal;
        recentLongest = std::max(recentLongest, bucket.StartToDisplayLongest);
      }
    }
    state.RecentStartToDisplayTotal = NanosecondTimeDuration::FromNanoseconds(recentTotal);
    state.RecentStartToDisplayLongest = NanosecondTimeDuration::FromNanoseconds(recentLongest);
    return state;
  }
}
