#ifndef MB_FRAMEPACING_PACER_DISPLAY_DISPLAYERRORCOUNTER_HPP
#define MB_FRAMEPACING_PACER_DISPLAY_DISPLAYERRORCOUNTER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/display/DisplayErrorState.hpp>
#include <mb/framepacing/pacer/display/DisplayReport.hpp>
#include <array>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built; the simulation only). The animation error
  //! where the application runs, from the display times it reports: one of the parts a pacer is put together from, used where
  //! display times are reported (the "+" beside a tier). Statistics only: nothing here paces a frame.
  //!
  //! The rules are the measuring tools': the animation error of a frame is its animation time step less its display time step
  //! (the time between its display time and that of the frame before it); an error of more than ErrorThreshold is an error
  //! frame; a display time step half a refresh or more off the animation time step is a frame at another refresh than it was made
  //! for; and a step next to a frame without a display time (never shown, no report) is not judged. What differs: the times are
  //! the platform's word, where the tools read them off the display.
  //!
  //! Values in, values out: no clock is read and nothing is allocated.
  class DisplayErrorCounter
  {
  public:
    //! The frames kept. A report for an older frame is refused: more than any platform's queue of results.
    static constexpr uint32_t Capacity = 64;

    //! An animation error of more than this is an error frame: the measuring tools' threshold, 1 ms.
    static constexpr NanosecondTimeSpan ErrorThreshold{NanosecondTimeSpan::NanosecondsPerMillisecond};

    //! The last second is counted in this many parts of an eighth of a second each.
    static constexpr uint32_t RecentBuckets = 8;

  private:
    struct Bucket
    {
      // Which eighth of a second on the steady clock; -1: none
      int64_t Index{-1};
      uint32_t Judged{0};
      uint32_t Errors{0};
      uint32_t OffTarget{0};
      uint32_t Late{0};
    };

    // The animation time step of every frame kept, by its id
    std::array<NanosecondTimeSpan, Capacity> m_steps{};
    // The newest frame begun (0: none), the oldest one kept, and the newest one a report came for
    uint64_t m_newestId{0};
    uint64_t m_oldestId{1};
    uint64_t m_newestReportId{0};
    // The frame shown last by the reports, and when: the frame after it is judged against it
    uint64_t m_shownId{0};
    NanosecondTickCount m_shownTime;
    std::array<Bucket, RecentBuckets> m_buckets{};
    int64_t m_newestBucket{-1};
    DisplayErrorState m_state;

  public:
    //! A frame begins: its id (FrameSchedule::FrameId, one more than the frame before) and its animation time step. An id that
    //! is not the next one starts again from it.
    void AddFrame(uint64_t frameId, NanosecondTimeSpan animationStep) noexcept;

    //! A display report for a frame that was begun: oldest first, at most once a frame. period is the display's refresh period.
    void AddDisplayReport(const DisplayReport& report, RefreshPeriod period) noexcept;

    //! Start again: the frames begun so far are forgotten (reports for them are refused), and nothing is judged across. The
    //! counts stay.
    void Restart() noexcept;

    //! What the reports said so far.
    [[nodiscard]] DisplayErrorState State() const noexcept;
  };
}

#endif
