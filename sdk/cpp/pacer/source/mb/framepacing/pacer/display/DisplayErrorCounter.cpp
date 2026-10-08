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
  }

  void DisplayErrorCounter::AddFrame(const uint64_t frameId, const NanosecondTimeSpan animationStep) noexcept
  {
    if (frameId != m_newestId + 1u)
    {
      // Not the next frame: the ids started again, and the frames before are not this run's
      m_newestId = frameId == 0 ? 0 : frameId - 1u;
      Restart();
    }
    m_newestId = frameId;
    if (m_newestId - m_oldestId >= Capacity)
    {
      ++m_oldestId;
    }
    m_steps[static_cast<std::size_t>(frameId % Capacity)] = animationStep;
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
      // Never shown: the frame after it has no frame before it to be judged against
      ++m_state.NotShown;
      return;
    }
    // Judged against the frame before it, when that one was reported as shown: a step across a frame without a display time
    // says nothing of either frame
    if (m_shownId != 0 && frameId == m_shownId + 1u)
    {
      const NanosecondTimeSpan animationStep = m_steps[static_cast<std::size_t>(frameId % Capacity)];
      const int64_t displayStep = report.DisplayTime.Nanoseconds() - m_shownTime.Nanoseconds();
      const int64_t error = animationStep.Nanoseconds() - displayStep;
      const int64_t offTarget = period.ToNanosecondTimeSpan().Nanoseconds() / OffTargetDivisor;
      const bool isError = error > ErrorThreshold.Nanoseconds() || error < -ErrorThreshold.Nanoseconds();
      const bool isOffTarget = error >= offTarget || error <= -offTarget;
      // Shown later than its animation time step put it: the display time step is the longer of the two
      const bool isLate = error <= -offTarget;

      // The part of the last second the frame was shown in
      const int64_t index = report.DisplayTime.Nanoseconds() / BucketNanoseconds;
      Bucket& bucket = m_buckets[static_cast<std::size_t>(static_cast<uint64_t>(index) % RecentBuckets)];
      if (bucket.Index != index)
      {
        bucket = Bucket{index, 0, 0, 0, 0};
      }
      m_newestBucket = std::max(m_newestBucket, index);
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
    for (const Bucket& bucket : m_buckets)
    {
      // The parts of the second that ends with the newest display time
      if (bucket.Index >= 0 && bucket.Index > m_newestBucket - int64_t{RecentBuckets})
      {
        state.RecentJudgedFrames += bucket.Judged;
        state.RecentErrorFrames += bucket.Errors;
        state.RecentOffTargetFrames += bucket.OffTarget;
        state.RecentLateFrames += bucket.Late;
      }
    }
    return state;
  }
}
