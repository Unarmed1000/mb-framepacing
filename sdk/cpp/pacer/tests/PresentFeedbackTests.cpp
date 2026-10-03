// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Present feedback (optional): the frames measured by the display times the platform reports and not by their starts. FramesInFlight on
// its own, the refresh clock's MeasureLate, the pacer on a display that queues presents (frame starts that wobble, late frames, feedback
// that is late, missing, refused or stops), and a present log of a real swap chain (test-data/pacer/240-vulkan-present-log.csv).
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/clock/AnimationTime.hpp>
#include <mb/framepacing/pacer/clock/FrameMeasurement.hpp>
#include <mb/framepacing/pacer/clock/PacerRefreshClock.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FramesInFlight.hpp>
#include <mb/framepacing/pacer/frame/MeasuredFrame.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedback.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedbackState.hpp>
#include <mb/framepacing/pacer/frame/PresentResult.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <deque>
#include <filesystem>
#include <fstream>
#include <optional>
#include <sstream>
#include <string>
#include <vector>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr int64_t Ms = FP::TimeSpan::TicksPerMillisecond;
  constexpr int64_t Second = FP::TimeSpan::TicksPerSecond;
  constexpr int64_t StartTicks = 100 * Second;

  const PC::RefreshPeriod g_hz60 = PC::RefreshPeriod::FromRate(60);
  const PC::RefreshPeriod g_hz240 = PC::RefreshPeriod::FromRate(240);

  FP::TimeSpan Span(const int64_t ticks) noexcept
  {
    return FP::TimeSpan(ticks);
  }

  FP::TickCount64 At(const int64_t ticks) noexcept
  {
    return FP::TickCount64(ticks);
  }

  //! The time of a refresh of a display whose refresh 0 is at StartTicks
  FP::TickCount64 Refresh(const PC::RefreshPeriod period, const int64_t refresh) noexcept
  {
    return At(StartTicks + period.TimeFor(refresh).Ticks());
  }

  //! Within a tick: a display time plus whole refreshes is rounded once more than the refresh itself
  ::testing::AssertionResult WithinATick(const FP::TickCount64 actual, const FP::TickCount64 expected)
  {
    const int64_t off = (actual - expected).Ticks();
    return std::abs(off) <= 1 ? ::testing::AssertionSuccess() : (::testing::AssertionFailure() << off << " ticks off");
  }

  //! Frames begun one refresh apart from StartTicks, each presented a millisecond after its start
  void BeginFrames(PC::FramesInFlight& rFrames, const PC::RefreshPeriod period, const int64_t count, const uint32_t swapInterval = 1)
  {
    for (int64_t index = 0; index < count; ++index)
    {
      const auto frame = static_cast<int64_t>(rFrames.NewestFrameId());
      const FP::TickCount64 start = Refresh(period, frame * swapInterval);
      static_cast<void>(rFrames.Begin(swapInterval, period.TimeFor(frame * swapInterval), start));
      rFrames.End(Span(Ms), start + Span(Ms));
    }
  }

  //! Every decided frame, oldest first
  std::vector<PC::MeasuredFrame> TakeAll(PC::FramesInFlight& rFrames)
  {
    std::vector<PC::MeasuredFrame> measured;
    PC::MeasuredFrame frame;
    while (rFrames.TakeMeasured(frame))
    {
      measured.push_back(frame);
    }
    return measured;
  }

  //! What a frame of QueuedLoop does
  struct LoopFrame
  {
    //! The frame starts this long after the refresh it would start on
    FP::TimeSpan Wobble;
    FP::TimeSpan Work{Ms};
    //! The display shows it this many refreshes later than its place in the queue
    int64_t LateRefreshes{0};
    //! The platform reports its display time
    bool Feedback{true};
  };

  //! A frame loop on a swap chain that queues presents: a frame is shown QueueRefreshes after the refresh it is presented in, no
  //! earlier than its swap interval after the frame before it, and its display time is reported FeedbackDelay frames later. The frames
  //! start on a refresh, a swap interval apart, plus their wobble: the time a busy machine takes to give the thread the CPU.
  class QueuedLoop
  {
    struct Shown
    {
      uint64_t FrameId{0};
      int64_t Frame{0};
      FP::TickCount64 PresentTime;
      int64_t Refresh{0};
    };

    PC::FramePacer& m_pacer;
    PC::RefreshPeriod m_display;
    int64_t m_frame{0};
    int64_t m_startRefresh{0};
    int64_t m_shownOnRefresh{0};
    std::deque<Shown> m_results;

  public:
    static constexpr int64_t QueueRefreshes = 3;
    static constexpr int64_t FeedbackDelay = 4;

    QueuedLoop(PC::FramePacer& pacer, const PC::RefreshPeriod display)
      : m_pacer(pacer)
      , m_display(display)
    {
    }

    PC::FrameSchedule Frame(const LoopFrame& frame = {})
    {
      while (!m_results.empty() && m_results.front().Frame + FeedbackDelay <= m_frame)
      {
        const Shown& shown = m_results.front();
        m_pacer.AddPresentFeedback(PC::PresentFeedback::Shown(shown.FrameId, Refresh(m_display, shown.Refresh), shown.PresentTime));
        m_results.pop_front();
      }
      const FP::TickCount64 start = Refresh(m_display, m_startRefresh) + frame.Wobble;
      const PC::FrameSchedule schedule = m_pacer.BeginFrame(start);
      const FP::TickCount64 presentTime = start + frame.Work;
      static_cast<void>(m_pacer.EndFrame(presentTime, frame.Work));
      const int64_t queued = m_display.RefreshesToFit(presentTime - Refresh(m_display, 0)) + QueueRefreshes - 1;
      m_shownOnRefresh = std::max(m_shownOnRefresh + int64_t{schedule.SwapInterval}, queued) + frame.LateRefreshes;
      if (frame.Feedback)
      {
        m_results.push_back({schedule.FrameId, m_frame, presentTime, m_shownOnRefresh});
      }
      m_startRefresh += int64_t{schedule.SwapInterval} + frame.LateRefreshes;
      ++m_frame;
      return schedule;
    }

    //! The refresh the last frame is shown on.
    [[nodiscard]] int64_t ShownOnRefresh() const noexcept
    {
      return m_shownOnRefresh;
    }

    //! The pause: the loop goes on that many refreshes later, and the results still on their way are lost.
    void Pause(const int64_t refreshes)
    {
      m_startRefresh += refreshes;
      m_shownOnRefresh += refreshes;
      m_results.clear();
    }
  };

  //! The long and short frame starts of a busy machine (the real log's pattern): every fourth frame starts 0.7 refresh late, and the
  //! one after it on its refresh again
  FP::TimeSpan Wobble(const PC::RefreshPeriod period, const int64_t frame) noexcept
  {
    return frame % 4 == 3 ? Span(period.ToTimeSpan().Ticks() * 7 / 10) : Span(0);
  }

  PC::PacerSettings FeedbackSettings(const PC::RefreshPeriod period)
  {
    PC::PacerSettings settings(period);
    settings.SetUsePresentFeedback(true);
    return settings;
  }

  std::optional<std::filesystem::path> FindTestData()
  {
    for (auto folder = std::filesystem::path(MB_FRAMEPACING_PACER_SOURCE_DIR); !folder.empty(); folder = folder.parent_path())
    {
      auto candidate = folder / "test-data" / "pacer" / "240-vulkan-present-log.csv";
      if (std::filesystem::exists(candidate))
      {
        return candidate;
      }
      if (folder == folder.parent_path())
      {
        break;
      }
    }
    return std::nullopt;
  }

  //! A row of the present log: a frame of a real swap chain. Times in ticks; -1 where the platform gave none
  struct LogFrame
  {
    int64_t Frame{0};
    int64_t StartTicks{0};
    int64_t PresentTicks{0};
    int64_t DisplayTicks{-1};
    int64_t FeedbackFrame{-1};
  };

  std::vector<LogFrame> ReadLog(const std::filesystem::path& path)
  {
    std::vector<LogFrame> frames;
    std::ifstream file(path, std::ios::binary);
    std::string line;
    std::getline(file, line);
    while (std::getline(file, line))
    {
      std::vector<std::string> fields;
      std::stringstream stream(line + ",");
      std::string field;
      while (std::getline(stream, field, ','))
      {
        fields.push_back(field);
      }
      const auto number = [&fields](const std::size_t index) { return fields[index].empty() ? int64_t{-1} : std::stoll(fields[index]); };
      frames.push_back({number(0), number(1), number(2), number(3), number(4)});
    }
    return frames;
  }
  //! What pacing a present log gave
  struct LogResult
  {
    //! Frames whose animation stepped further than their swap interval
    int64_t CatchUps{0};
    int64_t Slower{0};
    uint32_t MostLateFrames{0};
    uint32_t SwapInterval{0};
    PC::PresentFeedbackState State;
  };

  //! A pacer over the log's frames, measured by their starts or by the display times as the application read them
  LogResult PaceLog(const std::vector<LogFrame>& log, const PC::RefreshPeriod period, const bool useFeedback, const bool autoSwapInterval)
  {
    PC::PacerSettings settings(period);
    settings.SetUsePresentFeedback(useFeedback);
    settings.SetAutoSwapInterval(autoSwapInterval);
    PC::FramePacer pacer(settings);
    LogResult result;
    std::size_t next = 0;
    for (const LogFrame& frame : log)
    {
      // The results the application read at this frame's start. The log's frame numbers are the pacer's ids: both count from 1
      for (; next < log.size() && log[next].FeedbackFrame <= frame.Frame; ++next)
      {
        if (log[next].DisplayTicks >= 0)
        {
          pacer.AddPresentFeedback(
            PC::PresentFeedback::Shown(static_cast<uint64_t>(log[next].Frame), At(log[next].DisplayTicks), At(log[next].PresentTicks)));
        }
      }
      const PC::FrameSchedule schedule = pacer.BeginFrame(At(frame.StartTicks));
      static_cast<void>(pacer.EndFrame(At(frame.PresentTicks)));
      EXPECT_EQ(schedule.FrameId, static_cast<uint64_t>(frame.Frame));
      result.CatchUps += period.NearestRefreshes(schedule.AnimationStep) > int64_t{schedule.SwapInterval} ? 1 : 0;
      result.Slower += schedule.Change == PC::SwapIntervalChange::Slower ? 1 : 0;
      result.MostLateFrames = std::max(result.MostLateFrames, pacer.FrameWindow().LateFrames);
    }
    result.SwapInterval = pacer.SwapInterval();
    result.State = pacer.FeedbackState();
    return result;
  }
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// PacerSettings
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(PresentFeedbackSettings, FeedbackIsOffUnlessAsked)
{
  const PC::PacerSettings defaults(g_hz60);
  EXPECT_FALSE(defaults.UsePresentFeedback());
  PC::PacerSettings on(g_hz60);
  on.SetUsePresentFeedback(true);
  EXPECT_TRUE(on.UsePresentFeedback());
  EXPECT_NE(on, defaults);
  on.SetUsePresentFeedback(false);
  EXPECT_EQ(on, defaults);
}

TEST(PresentFeedbackSettings, TheFactoriesFillTheFeedback)
{
  const PC::PresentFeedback shown = PC::PresentFeedback::Shown(7, At(500));
  EXPECT_EQ(shown.FrameId, 7u);
  EXPECT_EQ(shown.Result, PC::PresentResult::Shown);
  EXPECT_EQ(shown.DisplayTime, At(500));
  EXPECT_FALSE(shown.HasPresentTime);

  const PC::PresentFeedback presented = PC::PresentFeedback::Shown(8, At(500), At(400));
  EXPECT_EQ(presented.FrameId, 8u);
  EXPECT_EQ(presented.Result, PC::PresentResult::Shown);
  EXPECT_EQ(presented.DisplayTime, At(500));
  EXPECT_TRUE(presented.HasPresentTime);
  EXPECT_EQ(presented.PresentTime, At(400));

  const PC::PresentFeedback notShown = PC::PresentFeedback::NotShown(9);
  EXPECT_EQ(notShown.FrameId, 9u);
  EXPECT_EQ(notShown.Result, PC::PresentResult::NotShown);
  EXPECT_FALSE(notShown.HasPresentTime);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// FramesInFlight
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(FramesInFlight, FramesCountFromOneAndNothingIsKnownWithoutFeedback)
{
  PC::FramesInFlight frames(g_hz60);
  EXPECT_EQ(frames.NewestFrameId(), 0u);
  EXPECT_EQ(frames.Refresh(), g_hz60);
  // Before any frame: nothing to end, to take, or to give feedback for
  frames.End(Span(Ms), At(StartTicks));
  frames.Add(PC::PresentFeedback::Shown(0, At(StartTicks)));
  frames.Add(PC::PresentFeedback::Shown(1, At(StartTicks)));
  EXPECT_EQ(frames.State().Refused, 2u);

  EXPECT_EQ(frames.Begin(1, Span(0), At(StartTicks)), 1u);
  EXPECT_EQ(frames.Begin(1, Span(166'667), At(StartTicks + 166'667)), 2u);
  EXPECT_EQ(frames.NewestFrameId(), 2u);
  EXPECT_EQ(frames.IntendedDisplayTime(), FP::TickCount64());
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  EXPECT_TRUE(TakeAll(frames).empty());
  EXPECT_EQ(frames.State().Used, 0u);
  EXPECT_EQ(frames.State().Missing, 0u);
}

TEST(FramesInFlight, FramesAreMeasuredByTheRefreshesBetweenTheirDisplayTimes)
{
  PC::FramesInFlight frames(g_hz60);
  BeginFrames(frames, g_hz60, 3);
  // The first display time is where the count starts: three refreshes after the frame's start, a queue
  frames.Add(PC::PresentFeedback::Shown(1, Refresh(g_hz60, 3)));
  EXPECT_EQ(frames.State().Used, 1u);
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  // The newest frame is two frames after it: two refreshes later when nothing is late
  EXPECT_TRUE(WithinATick(frames.IntendedDisplayTime(), Refresh(g_hz60, 5)));
  std::vector<PC::MeasuredFrame> measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 1u);
  EXPECT_EQ(measured[0].FrameId, 1u);
  EXPECT_EQ(measured[0].AnimationTime, Span(0));
  EXPECT_EQ(measured[0].Work, Span(Ms));
  EXPECT_FALSE(measured[0].Late);

  // One refresh later: on time. Two refreshes after that: a refresh late
  frames.Add(PC::PresentFeedback::Shown(2, Refresh(g_hz60, 4)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  frames.Add(PC::PresentFeedback::Shown(3, Refresh(g_hz60, 6)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 1u);
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u) << "taken once";
  measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 2u);
  EXPECT_FALSE(measured[0].Late);
  EXPECT_TRUE(measured[1].Late);
  EXPECT_EQ(measured[1].AnimationTime, g_hz60.TimeFor(2));
  // The newest frame is the one just measured: its own display time, and the next frame's is a swap interval later
  EXPECT_TRUE(WithinATick(frames.IntendedDisplayTime(), Refresh(g_hz60, 6)));
  BeginFrames(frames, g_hz60, 1);
  EXPECT_TRUE(WithinATick(frames.IntendedDisplayTime(), Refresh(g_hz60, 7)));
  EXPECT_EQ(frames.State().Used, 3u);
  EXPECT_EQ(frames.State().Refused, 0u);
  EXPECT_EQ(frames.State().Missing, 0u);
}

TEST(FramesInFlight, FramesAtASlowerSwapIntervalAreDueThatManyRefreshesApart)
{
  PC::FramesInFlight frames(g_hz60);
  BeginFrames(frames, g_hz60, 4, 2);
  frames.Add(PC::PresentFeedback::Shown(1, Refresh(g_hz60, 4)));
  frames.Add(PC::PresentFeedback::Shown(2, Refresh(g_hz60, 6)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  // Five refreshes for a swap interval of two: three late, all of them counted
  frames.Add(PC::PresentFeedback::Shown(3, Refresh(g_hz60, 11)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 3u);
  EXPECT_TRUE(WithinATick(frames.IntendedDisplayTime(), Refresh(g_hz60, 13)));
  const std::vector<PC::MeasuredFrame> measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 3u);
  EXPECT_TRUE(measured[2].Late);
}

TEST(FramesInFlight, AFrameWithoutFeedbackCountsAsOnTimeAndTheNextMeasuresAcrossIt)
{
  PC::FramesInFlight frames(g_hz60);
  BeginFrames(frames, g_hz60, 5);
  frames.Add(PC::PresentFeedback::Shown(1, Refresh(g_hz60, 3)));
  // Nothing for frame 2, frame 3 was never shown, frame 4 is three refreshes after frame 1: all on time
  frames.Add(PC::PresentFeedback::NotShown(3));
  frames.Add(PC::PresentFeedback::Shown(4, Refresh(g_hz60, 6)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  std::vector<PC::MeasuredFrame> measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 4u);
  for (const PC::MeasuredFrame& frame : measured)
  {
    EXPECT_FALSE(frame.Late) << frame.FrameId;
  }
  EXPECT_EQ(frames.State().Used, 2u);
  EXPECT_EQ(frames.State().NotShown, 1u);
  EXPECT_EQ(frames.State().Missing, 1u) << "frame 2";

  // The lateness of a stretch without feedback is found at its end
  BeginFrames(frames, g_hz60, 3);
  frames.Add(PC::PresentFeedback::Shown(8, Refresh(g_hz60, 12)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 2u);
  measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 4u);
  EXPECT_FALSE(measured[0].Late);
  EXPECT_TRUE(measured[3].Late);
  EXPECT_EQ(frames.State().Missing, 4u);
}

TEST(FramesInFlight, TwoFramesInOneRefreshPutTheCountAheadAndCostNoStepLater)
{
  PC::FramesInFlight frames(g_hz60);
  BeginFrames(frames, g_hz60, 5);
  frames.Add(PC::PresentFeedback::Shown(1, Refresh(g_hz60, 3)));
  // Frame 2 has frame 1's display time: frame 1 was not seen, and the count is a refresh ahead of the display
  frames.Add(PC::PresentFeedback::Shown(2, Refresh(g_hz60, 3)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  EXPECT_TRUE(WithinATick(frames.IntendedDisplayTime(), Refresh(g_hz60, 7))) << "frame 5, on the refresh it was due on all along";
  // Frame 3 two refreshes later: held a refresh longer than its swap interval (late for the rule), and where it was due: no catch-up
  frames.Add(PC::PresentFeedback::Shown(3, Refresh(g_hz60, 5)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  EXPECT_TRUE(WithinATick(frames.IntendedDisplayTime(), Refresh(g_hz60, 7)));
  // With the lead used up the next late refresh counts
  frames.Add(PC::PresentFeedback::Shown(4, Refresh(g_hz60, 7)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 1u);
  const std::vector<PC::MeasuredFrame> measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 4u);
  EXPECT_FALSE(measured[1].Late);
  EXPECT_TRUE(measured[2].Late);
  EXPECT_TRUE(measured[3].Late);
  EXPECT_EQ(frames.State().Used, 4u);

  // A display time a little before the one used counts as the same refresh
  frames.Add(PC::PresentFeedback::Shown(5, Refresh(g_hz60, 7) - Span(Ms)));
  EXPECT_EQ(frames.State().Used, 5u);
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
}

TEST(FramesInFlight, FeedbackThatCanNotBeRightIsRefused)
{
  PC::FramesInFlight frames(g_hz60);
  BeginFrames(frames, g_hz60, 6);
  // A frame that was never begun, and one given twice or after a newer one
  frames.Add(PC::PresentFeedback::Shown(7, Refresh(g_hz60, 9)));
  frames.Add(PC::PresentFeedback::Shown(2, Refresh(g_hz60, 4)));
  frames.Add(PC::PresentFeedback::Shown(2, Refresh(g_hz60, 4)));
  frames.Add(PC::PresentFeedback::Shown(1, Refresh(g_hz60, 3)));
  EXPECT_EQ(frames.State().Used, 1u);
  EXPECT_EQ(frames.State().Refused, 3u);

  // Shown before it was presented: before the time EndFrame was given (a millisecond after the frame's start)
  frames.Add(PC::PresentFeedback::Shown(3, Refresh(g_hz60, 2)));
  EXPECT_EQ(frames.State().Refused, 4u);
  // The present time given with the feedback counts where there is one: later than EndFrame's (the application waited), and earlier
  frames.Add(PC::PresentFeedback::Shown(4, Refresh(g_hz60, 6), Refresh(g_hz60, 6) + Span(1)));
  EXPECT_EQ(frames.State().Refused, 5u);
  frames.Add(PC::PresentFeedback::Shown(5, Refresh(g_hz60, 4), Refresh(g_hz60, 4)));
  EXPECT_EQ(frames.State().Used, 2u) << "before EndFrame's time, at the present time given: used";
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  // Refused feedback is feedback: the frames are not missing
  static_cast<void>(TakeAll(frames));
  EXPECT_EQ(frames.State().Missing, 1u) << "frame 1";
}

TEST(FramesInFlight, ADisplayTimeOffTheGridIsRefusedUntilTheNextOneAgreesWithIt)
{
  PC::FramesInFlight frames(g_hz60);
  const FP::TimeSpan half(g_hz60.ToTimeSpan().Ticks() / 2);
  BeginFrames(frames, g_hz60, 9);
  frames.Add(PC::PresentFeedback::Shown(1, Refresh(g_hz60, 3)));
  // Half a refresh off: not a refresh of this display. An eighth is the limit
  frames.Add(PC::PresentFeedback::Shown(2, Refresh(g_hz60, 4) + half));
  EXPECT_EQ(frames.State().Refused, 1u);
  // The next one on the old grid: used, measured across the refused one
  frames.Add(PC::PresentFeedback::Shown(3, Refresh(g_hz60, 5) + Span(g_hz60.ToTimeSpan().Ticks() / 8)));
  EXPECT_EQ(frames.State().Used, 2u);
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);

  // Off the grid twice, each on a grid of its own: both refused
  frames.Add(PC::PresentFeedback::Shown(4, Refresh(g_hz60, 6) + half));
  frames.Add(PC::PresentFeedback::Shown(5, Refresh(g_hz60, 7) + Span(half.Ticks() / 2)));
  EXPECT_EQ(frames.State().Refused, 3u);
  // A step back of a whole refresh is no refresh either
  frames.Add(PC::PresentFeedback::Shown(6, Refresh(g_hz60, 4)));
  EXPECT_EQ(frames.State().Refused, 4u);
  // Two in a row that agree with each other: the display's refreshes moved (a new swap chain), and the count starts again there,
  // with nothing measured across, however long it took
  frames.Add(PC::PresentFeedback::Shown(7, Refresh(g_hz60, 12) + half));
  EXPECT_EQ(frames.State().Refused, 5u);
  frames.Add(PC::PresentFeedback::Shown(8, Refresh(g_hz60, 14) + half));
  EXPECT_EQ(frames.State().Used, 3u);
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  EXPECT_TRUE(WithinATick(frames.IntendedDisplayTime(), Refresh(g_hz60, 15) + half)) << "frame 9, counted from frame 8";
  const std::vector<PC::MeasuredFrame> measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 8u);
  EXPECT_FALSE(measured[7].Late);
}

TEST(FramesInFlight, AFrameLeavesAsOnTimeWhenNothingDecidedIt)
{
  PC::FramesInFlight frames(g_hz60);
  BeginFrames(frames, g_hz60, PC::FramesInFlight::Capacity - 1);
  EXPECT_TRUE(TakeAll(frames).empty());
  // The next frame begun takes the oldest one's place, so that one is given now
  BeginFrames(frames, g_hz60, 1);
  std::vector<PC::MeasuredFrame> measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 1u);
  EXPECT_EQ(measured[0].FrameId, 1u);
  EXPECT_FALSE(measured[0].Late);
  EXPECT_EQ(frames.State().Missing, 1u);

  // Frames that left without being taken are not given later, and feedback for one that left is refused
  BeginFrames(frames, g_hz60, 10);
  frames.Add(PC::PresentFeedback::Shown(10, Refresh(g_hz60, 12)));
  EXPECT_EQ(frames.State().Refused, 1u);
  measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 1u);
  EXPECT_EQ(measured[0].FrameId, 11u);
}

TEST(FramesInFlight, ADisplayTimeWhoseFrameHasLeftIsNotCountedFrom)
{
  PC::FramesInFlight frames(g_hz60);
  BeginFrames(frames, g_hz60, 2);
  frames.Add(PC::PresentFeedback::Shown(1, Refresh(g_hz60, 3)));
  frames.Add(PC::PresentFeedback::Shown(2, Refresh(g_hz60, 4) + Span(g_hz60.ToTimeSpan().Ticks() / 2)));
  EXPECT_EQ(frames.State().Refused, 1u);
  BeginFrames(frames, g_hz60, PC::FramesInFlight::Capacity - 2);
  EXPECT_NE(frames.IntendedDisplayTime(), FP::TickCount64()) << "frame 1 is the oldest kept";
  BeginFrames(frames, g_hz60, 1);
  EXPECT_EQ(frames.IntendedDisplayTime(), FP::TickCount64());
  // The next display time starts the count again: nothing is late, wherever it is, and the refused one of before is forgotten
  frames.Add(PC::PresentFeedback::Shown(60, Refresh(g_hz60, 200) + Span(g_hz60.ToTimeSpan().Ticks() / 2)));
  EXPECT_EQ(frames.State().Used, 2u);
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u);
  frames.Add(PC::PresentFeedback::Shown(61, Refresh(g_hz60, 201)));
  EXPECT_EQ(frames.State().Refused, 2u) << "off the new grid, and no refused one before it to agree with";
}

TEST(FramesInFlight, RestartForgetsTheFramesAndTheCount)
{
  PC::FramesInFlight frames(g_hz60);
  BeginFrames(frames, g_hz60, 4);
  frames.Add(PC::PresentFeedback::Shown(1, Refresh(g_hz60, 3)));
  frames.Add(PC::PresentFeedback::Shown(2, Refresh(g_hz60, 6)));
  frames.Restart();
  EXPECT_EQ(frames.TakeLateRefreshes(), 0u) << "what was late before the restart is not caught up";
  EXPECT_EQ(frames.IntendedDisplayTime(), FP::TickCount64());
  EXPECT_TRUE(TakeAll(frames).empty());
  frames.Add(PC::PresentFeedback::Shown(3, Refresh(g_hz60, 7)));
  EXPECT_EQ(frames.State().Refused, 1u);
  // No frame is kept: nothing to end
  frames.End(Span(5 * Ms), At(StartTicks));
  EXPECT_EQ(frames.Begin(1, Span(0), Refresh(g_hz60, 10)), 5u) << "the ids go on";
  frames.Add(PC::PresentFeedback::Shown(5, Refresh(g_hz60, 13)));
  const std::vector<PC::MeasuredFrame> measured = TakeAll(frames);
  ASSERT_EQ(measured.size(), 1u);
  EXPECT_EQ(measured[0].FrameId, 5u);
  EXPECT_EQ(measured[0].Work, Span(0));

  // Another refresh period: the same, on the new period
  frames.SetRefreshPeriod(g_hz240);
  EXPECT_EQ(frames.Refresh(), g_hz240);
  EXPECT_EQ(frames.IntendedDisplayTime(), FP::TickCount64());
  BeginFrames(frames, g_hz240, 2);
  frames.Add(PC::PresentFeedback::Shown(6, Refresh(g_hz240, 30)));
  frames.Add(PC::PresentFeedback::Shown(7, Refresh(g_hz240, 32)));
  EXPECT_EQ(frames.TakeLateRefreshes(), 1u);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// PacerRefreshClock::MeasureLate
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(PacerRefreshClockFeedback, TheDisplayMovesOnByTheSwapIntervalAndWhatWasLate)
{
  PC::PacerRefreshClock clock(g_hz60, Span(2 * Second));
  EXPECT_TRUE(clock.MeasureLate(At(StartTicks), 5).Restarted) << "the first frame: nothing to count";
  EXPECT_EQ(clock.Step(1).StepRefreshes, 0u);

  // The frame starts say nothing here: 1.9 refreshes after the previous start is one swap interval when nothing was late
  int64_t start = StartTicks + (g_hz60.ToTimeSpan().Ticks() * 19 / 10);
  PC::FrameMeasurement measured = clock.MeasureLate(At(start), 0);
  EXPECT_FALSE(measured.Restarted);
  EXPECT_EQ(measured.Refreshes, 1u);
  EXPECT_FALSE(measured.Late);
  EXPECT_EQ(measured.DisplayTime, g_hz60.TimeFor(1));
  EXPECT_EQ(clock.Step(2).StepRefreshes, 2u);

  // Two refreshes late, at a swap interval of two: the display moved four, and the next frame catches the two up
  start += Ms;
  measured = clock.MeasureLate(At(start), 2);
  EXPECT_EQ(measured.Refreshes, 4u);
  EXPECT_TRUE(measured.Late);
  EXPECT_EQ(measured.DisplayTime, g_hz60.TimeFor(5));
  const PC::AnimationTime animation = clock.Step(1);
  EXPECT_EQ(animation.StepRefreshes, 3u);
  EXPECT_EQ(animation.Time, g_hz60.TimeFor(5));

  // A pause is still read from the frame starts, and what was late is not counted across it
  measured = clock.MeasureLate(At(start + (3 * Second)), 7);
  EXPECT_TRUE(measured.Restarted);
  EXPECT_EQ(clock.Step(1).StepRefreshes, 1u);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// FramePacer with present feedback
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(PacerFeedback, FrameStartsThatWobbleAreLateByTheirStartsAndOnTimeByFeedback)
{
  // The display shows every frame one refresh after the one before it; only the starts are uneven, as a busy machine makes them
  PC::FramePacer byStarts{PC::PacerSettings(g_hz240)};
  PC::FramePacer byFeedback(FeedbackSettings(g_hz240));
  QueuedLoop startsLoop(byStarts, g_hz240);
  QueuedLoop feedbackLoop(byFeedback, g_hz240);
  const int64_t frames = 2'000;
  int64_t slowerByStarts = 0;
  int64_t wrongByFeedback = 0;
  PC::FrameSchedule schedule;
  for (int64_t frame = 0; frame < frames; ++frame)
  {
    slowerByStarts += startsLoop.Frame({Wobble(g_hz240, frame)}).Change == PC::SwapIntervalChange::Slower ? 1 : 0;
    schedule = feedbackLoop.Frame({Wobble(g_hz240, frame)});
    const bool right = schedule.SwapInterval == 1u && schedule.Change == PC::SwapIntervalChange::Unchanged &&
                       schedule.AnimationTime == g_hz240.TimeFor(frame) && schedule.FrameId == static_cast<uint64_t>(frame) + 1u;
    wrongByFeedback += right ? 0 : 1;
  }
  EXPECT_GT(slowerByStarts, 0) << "a quarter of the frames read as late: the rule slows down, with a display that shows every frame";
  EXPECT_EQ(wrongByFeedback, 0);
  EXPECT_EQ(byFeedback.SwapInterval(), 1u);
  EXPECT_EQ(byFeedback.FrameWindow().LateFrames, 0u);
  EXPECT_GT(byFeedback.FrameWindow().Frames, 0u);
  const PC::PresentFeedbackState state = byFeedback.FeedbackState();
  EXPECT_EQ(state.Used, static_cast<uint64_t>(frames - QueuedLoop::FeedbackDelay));
  EXPECT_EQ(state.Refused, 0u);
  EXPECT_EQ(state.NotShown, 0u);
  EXPECT_EQ(state.Missing, 0u);
}

TEST(PacerFeedback, TheIntendedDisplayTimeIsTheRefreshTheFrameReaches)
{
  PC::FramePacer pacer(FeedbackSettings(g_hz240));
  QueuedLoop loop(pacer, g_hz240);
  for (int64_t frame = 0; frame < 200; ++frame)
  {
    const FP::TimeSpan wobble = Wobble(g_hz240, frame);
    const PC::FrameSchedule schedule = loop.Frame({wobble});
    const FP::TickCount64 start = Refresh(g_hz240, frame) + wobble;
    // What a loop that sleeps holds to is counted from the frame's start, whatever the display does
    EXPECT_LE(std::abs((schedule.NextFrameStartTime - (start + g_hz240.TimeFor(1))).Ticks()), 1) << frame;
    if (frame < QueuedLoop::FeedbackDelay)
    {
      EXPECT_EQ(schedule.IntendedDisplayTime, FP::TickCount64()) << frame << ": no display time to count from yet";
    }
    else
    {
      // The queue's three refreshes are in it, and the start's wobble is not
      EXPECT_TRUE(WithinATick(schedule.IntendedDisplayTime, Refresh(g_hz240, loop.ShownOnRefresh()))) << frame;
      EXPECT_EQ(loop.ShownOnRefresh(), frame + QueuedLoop::QueueRefreshes) << frame;
    }
  }
}

TEST(PacerFeedback, ALateFrameIsCaughtUpWhenItsFeedbackComes)
{
  PC::FramePacer pacer(FeedbackSettings(g_hz60));
  QueuedLoop loop(pacer, g_hz60);
  const int64_t lateFrame = 20;
  const int64_t caughtUpFrame = lateFrame + QueuedLoop::FeedbackDelay;
  for (int64_t frame = 0; frame < 60; ++frame)
  {
    const PC::FrameSchedule schedule = loop.Frame({Span(0), Span(Ms), frame == lateFrame ? int64_t{2} : int64_t{0}});
    // The frames until the feedback comes are paced as if nothing happened; the frame that has it steps the two refreshes more
    const int64_t stepRefreshes = frame == 0 ? 0 : (frame == caughtUpFrame ? 3 : 1);
    EXPECT_EQ(g_hz60.NearestRefreshes(schedule.AnimationStep), stepRefreshes) << frame;
    // The animation time is the display's count: before the late frame and from the catch-up on, the queue's refreshes before it
    const int64_t animationRefresh = g_hz60.NearestRefreshes(schedule.AnimationTime);
    if (frame < lateFrame || frame >= caughtUpFrame)
    {
      EXPECT_EQ(loop.ShownOnRefresh() - animationRefresh, QueuedLoop::QueueRefreshes) << frame;
      if (frame >= QueuedLoop::FeedbackDelay)
      {
        EXPECT_TRUE(WithinATick(schedule.IntendedDisplayTime, Refresh(g_hz60, loop.ShownOnRefresh()))) << frame;
      }
    }
    else
    {
      EXPECT_EQ(loop.ShownOnRefresh() - animationRefresh, QueuedLoop::QueueRefreshes + 2) << frame;
    }
  }
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.SwapInterval(), 1u);
}

TEST(PacerFeedback, TheRuleSlowsDownOnFramesLateByFeedbackAndSpeedsUpAgain)
{
  PC::FramePacer pacer(FeedbackSettings(g_hz60));
  QueuedLoop loop(pacer, g_hz60);
  int64_t slowerAt = -1;
  int64_t fasterAt = -1;
  for (int64_t frame = 0; frame < 400; ++frame)
  {
    // Every third frame a refresh late, until the rule has slowed down
    const bool late = slowerAt < 0 && frame % 3 == 0;
    const PC::FrameSchedule schedule = loop.Frame({Span(0), Span(Ms), late ? int64_t{1} : int64_t{0}});
    if (schedule.Change == PC::SwapIntervalChange::Slower)
    {
      ASSERT_LT(slowerAt, 0) << "once";
      slowerAt = frame;
      EXPECT_EQ(schedule.SwapInterval, 2u);
    }
    else if (schedule.Change == PC::SwapIntervalChange::Faster)
    {
      ASSERT_LT(fasterAt, 0) << "once";
      fasterAt = frame;
      EXPECT_EQ(schedule.SwapInterval, 1u);
    }
    if (slowerAt >= 0 && fasterAt < 0)
    {
      // The late frames still in flight when the rule changed were paced at the old swap interval: the new window never counts them
      EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u) << frame;
    }
  }
  EXPECT_GT(slowerAt, 0);
  EXPECT_LT(slowerAt, 60) << "with a third of the frames late, within a second";
  EXPECT_GT(fasterAt, slowerAt + 60) << "after a frame window without a late frame";
  EXPECT_EQ(pacer.SwapInterval(), 1u);
}

TEST(PacerFeedback, WhenFeedbackStopsTheFramesLeaveAsOnTimeAndTheRuleSpeedsUpAgain)
{
  PC::FramePacer pacer(FeedbackSettings(g_hz60));
  QueuedLoop loop(pacer, g_hz60);
  bool slower = false;
  int64_t frame = 0;
  for (; frame < 200 && !slower; ++frame)
  {
    slower = loop.Frame({Span(0), Span(Ms), frame % 3 == 0 ? int64_t{1} : int64_t{0}}).Change == PC::SwapIntervalChange::Slower;
  }
  ASSERT_TRUE(slower);
  const uint64_t usedBefore = pacer.FeedbackState().Used;
  // No feedback from here on: nothing is late, each frame is counted once it is Capacity frames old, and a frame window of those
  // later the rule is back at full rate
  bool faster = false;
  for (int64_t later = 0; later < 300 && !faster; ++later)
  {
    faster = loop.Frame({Span(0), Span(Ms), 0, false}).Change == PC::SwapIntervalChange::Faster;
  }
  EXPECT_TRUE(faster);
  EXPECT_EQ(pacer.SwapInterval(), 1u);
  EXPECT_GE(pacer.FeedbackState().Missing, 60u);
  EXPECT_LE(pacer.FeedbackState().Used, usedBefore + static_cast<uint64_t>(QueuedLoop::FeedbackDelay));
}

TEST(PacerFeedback, WithFeedbackOffThePacerIsAsItWas)
{
  PC::FramePacer plain{PC::PacerSettings(g_hz60)};
  PC::FramePacer given{PC::PacerSettings(g_hz60)};
  int64_t now = StartTicks;
  int64_t changes = 0;
  for (int64_t frame = 0; frame < 300; ++frame)
  {
    const int64_t work = frame % 5 == 0 ? 30 * Ms : 4 * Ms;
    const PC::FrameSchedule expected = plain.BeginFrame(At(now));
    // Feedback that would count as late, were it used
    given.AddPresentFeedback(PC::PresentFeedback::Shown(static_cast<uint64_t>(frame), At(now + (frame * Ms))));
    const PC::FrameSchedule schedule = given.BeginFrame(At(now));
    EXPECT_EQ(schedule.FrameId, static_cast<uint64_t>(frame) + 1u);
    EXPECT_EQ(schedule.FrameId, expected.FrameId);
    EXPECT_EQ(schedule.SwapInterval, expected.SwapInterval) << frame;
    EXPECT_EQ(schedule.AnimationTime, expected.AnimationTime) << frame;
    EXPECT_EQ(schedule.AnimationStep, expected.AnimationStep) << frame;
    EXPECT_EQ(schedule.IntendedDisplayTime, expected.IntendedDisplayTime) << frame;
    EXPECT_EQ(schedule.IntendedDisplayTime, schedule.NextFrameStartTime) << frame;
    EXPECT_EQ(schedule.Change, expected.Change) << frame;
    EXPECT_EQ(plain.EndFrame(At(now + work)), given.EndFrame(At(now + work)));
    changes += schedule.Change != PC::SwapIntervalChange::Unchanged ? 1 : 0;
    now = std::max(schedule.NextFrameStartTime.Ticks(), now + work);
  }
  EXPECT_GT(changes, 0) << "the frames over a refresh slowed it down: the comparison covered a change";
  const PC::PresentFeedbackState state = given.FeedbackState();
  EXPECT_EQ(state.Used + state.Refused + state.NotShown + state.Missing, 0u);
}

TEST(PacerFeedback, APauseAndEveryRestartForgetTheFramesInFlight)
{
  PC::FramePacer pacer(FeedbackSettings(g_hz60));
  QueuedLoop loop(pacer, g_hz60);
  PC::FrameSchedule schedule;
  for (int64_t frame = 0; frame < 30; ++frame)
  {
    schedule = loop.Frame();
  }
  EXPECT_NE(schedule.IntendedDisplayTime, FP::TickCount64());
  const uint64_t beforeThePause = schedule.FrameId;

  // A pause longer than the frame window: the pacer starts again, and feedback for a frame from before it is refused
  loop.Pause(5 * 60);
  schedule = loop.Frame();
  EXPECT_EQ(schedule.FrameId, beforeThePause + 1u) << "the ids go on";
  EXPECT_EQ(schedule.IntendedDisplayTime, FP::TickCount64());
  EXPECT_EQ(g_hz60.NearestRefreshes(schedule.AnimationStep), 1);
  uint64_t refused = pacer.FeedbackState().Refused;
  pacer.AddPresentFeedback(PC::PresentFeedback::Shown(beforeThePause, schedule.NextFrameStartTime));
  EXPECT_EQ(pacer.FeedbackState().Refused, refused + 1u);

  // The same after Reset, another refresh period and other settings
  for (int kind = 0; kind < 3; ++kind)
  {
    for (int64_t frame = 0; frame < 10; ++frame)
    {
      schedule = loop.Frame();
    }
    EXPECT_NE(schedule.IntendedDisplayTime, FP::TickCount64()) << kind;
    if (kind == 0)
    {
      pacer.Reset();
    }
    else if (kind == 1)
    {
      pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(60'000, 1'001));
      pacer.SetRefreshPeriod(g_hz60);
    }
    else
    {
      PC::PacerSettings other = pacer.Settings();
      other.SetPreferredSwapInterval(2);
      pacer.SetSettings(other);
      other.SetPreferredSwapInterval(1);
      pacer.SetSettings(other);
    }
    refused = pacer.FeedbackState().Refused;
    pacer.AddPresentFeedback(PC::PresentFeedback::Shown(schedule.FrameId, schedule.IntendedDisplayTime));
    EXPECT_EQ(pacer.FeedbackState().Refused, refused + 1u) << kind;
    loop.Pause(0);
    EXPECT_EQ(loop.Frame().IntendedDisplayTime, FP::TickCount64()) << kind;
  }
}

TEST(PacerFeedback, TheSourceChangesOnALivePacer)
{
  PC::FramePacer pacer{PC::PacerSettings(g_hz60)};
  QueuedLoop loop(pacer, g_hz60);
  PC::FrameSchedule schedule;
  for (int64_t frame = 0; frame < 20; ++frame)
  {
    schedule = loop.Frame();
    EXPECT_EQ(schedule.IntendedDisplayTime, schedule.NextFrameStartTime);
  }
  EXPECT_EQ(pacer.FeedbackState().Used, 0u);

  // On: the pacer starts again and counts from the first display time of a frame begun after the switch
  pacer.SetSettings(FeedbackSettings(g_hz60));
  loop.Pause(0);
  for (int64_t frame = 0; frame < 20; ++frame)
  {
    schedule = loop.Frame();
  }
  EXPECT_EQ(schedule.FrameId, 40u);
  EXPECT_GT(pacer.FeedbackState().Used, 0u);
  EXPECT_TRUE(WithinATick(schedule.IntendedDisplayTime, Refresh(g_hz60, loop.ShownOnRefresh())));

  // Off again: by the frame starts, and feedback is not looked at
  const PC::PresentFeedbackState before = pacer.FeedbackState();
  pacer.SetSettings(PC::PacerSettings(g_hz60));
  for (int64_t frame = 0; frame < 20; ++frame)
  {
    schedule = loop.Frame();
    EXPECT_EQ(schedule.IntendedDisplayTime, schedule.NextFrameStartTime);
  }
  EXPECT_EQ(pacer.FeedbackState().Used, before.Used);
  EXPECT_EQ(pacer.FeedbackState().Refused, before.Refused);
}

TEST(PacerFeedback, AFrameWithoutEndFrameIsPresentedAtItsStart)
{
  PC::FramePacer pacer(FeedbackSettings(g_hz60));
  const PC::FrameSchedule first = pacer.BeginFrame(Refresh(g_hz60, 0));
  const PC::FrameSchedule second = pacer.BeginFrame(Refresh(g_hz60, 1));
  static_cast<void>(pacer.EndFrame(Refresh(g_hz60, 1) + Span(4 * Ms)));
  // Before the frame's start: refused. The second frame was ended: before that time is refused too
  pacer.AddPresentFeedback(PC::PresentFeedback::Shown(first.FrameId, Refresh(g_hz60, 0) - Span(1)));
  pacer.AddPresentFeedback(PC::PresentFeedback::Shown(second.FrameId, Refresh(g_hz60, 1) + Span(2 * Ms)));
  EXPECT_EQ(pacer.FeedbackState().Refused, 2u);
  const PC::FrameSchedule third = pacer.BeginFrame(Refresh(g_hz60, 2));
  pacer.AddPresentFeedback(PC::PresentFeedback::Shown(third.FrameId, Refresh(g_hz60, 2)));
  EXPECT_EQ(pacer.FeedbackState().Used, 1u);
  // The frame without EndFrame worked until the next one began, as without feedback
  const PC::FrameSchedule fourth = pacer.BeginFrame(Refresh(g_hz60, 3));
  EXPECT_TRUE(WithinATick(fourth.IntendedDisplayTime, Refresh(g_hz60, 3)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 3u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((g_hz60.TimeFor(1).Ticks() + (4 * Ms) + g_hz60.TimeFor(1).Ticks()) / 3));
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// A real swap chain
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(PacerFeedback, ARealSwapChainsFrameStartsReadAsLateAndItsDisplayTimesDoNot)
{
  // 1999 frames of a Vulkan FIFO swap chain on a 240 Hz display (fixed refresh, windowed, VK_EXT_present_timing), not paced, on a
  // machine busy with other work: when each frame started, when it was presented, when its first pixel left for the display, and the
  // frame in which that was reported
  const std::optional<std::filesystem::path> path = FindTestData();
  if (!path)
  {
    GTEST_SKIP() << "test-data/pacer/240-vulkan-present-log.csv not found (a copy outside the repository)";
  }
  const std::vector<LogFrame> log = ReadLog(*path);
  ASSERT_EQ(log.size(), 1999u);
  // The refresh duration the swap chain reports
  const PC::RefreshPeriod period = PC::RefreshPeriod::FromNanoseconds(4'166'500);

  // What the display did: the refreshes between the display times of frames next to each other
  int64_t heldLonger = 0;
  int64_t steps = 0;
  for (std::size_t index = 1; index < log.size(); ++index)
  {
    if (log[index].DisplayTicks >= 0 && log[index - 1].DisplayTicks >= 0)
    {
      ++steps;
      heldLonger += period.NearestRefreshes(Span(log[index].DisplayTicks - log[index - 1].DisplayTicks)) > 1 ? 1 : 0;
    }
  }
  EXPECT_EQ(steps, 1955);
  EXPECT_EQ(heldLonger, 2);

  // Paced at a fixed swap interval of one, as the log was taken: by their starts a tenth of the frames read as late, and the
  // animation steps a refresh further each time although the display showed all but two frames one refresh apart. By their display
  // times those two are late, and nothing else
  const LogResult fixedByStarts = PaceLog(log, period, false, false);
  const LogResult fixedByFeedback = PaceLog(log, period, true, false);
  EXPECT_EQ(fixedByStarts.CatchUps, 214);
  EXPECT_EQ(fixedByFeedback.CatchUps, 2);
  EXPECT_EQ(fixedByFeedback.State.Used, 1'974u);
  EXPECT_EQ(fixedByFeedback.State.Refused, 0u);
  EXPECT_EQ(fixedByFeedback.State.NotShown, 0u);

  // With the rule: by the starts it slows down, by the display times it never changes
  const LogResult ruleByStarts = PaceLog(log, period, false, true);
  const LogResult ruleByFeedback = PaceLog(log, period, true, true);
  EXPECT_GT(ruleByStarts.Slower, 0);
  EXPECT_EQ(ruleByFeedback.Slower, 0);
  EXPECT_EQ(ruleByFeedback.CatchUps, 2);
  EXPECT_EQ(ruleByFeedback.MostLateFrames, 2u);
  EXPECT_EQ(ruleByFeedback.SwapInterval, 1u);
}
