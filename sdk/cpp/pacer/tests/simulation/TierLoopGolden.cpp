// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Test code: the runs of the tier pacer's simulated loop that are golden data, and their text.
#include "TierLoopGolden.hpp"
#include <mb/framepacing/core/Crc32Util.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <string_view>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"
#include "TierLoopGoldenRun.hpp"

namespace MB::FramePacing::Pacer::Simulation::TierLoopGolden
{
  namespace
  {
    constexpr int32_t Frames = 200;

    struct Way
    {
      std::string_view Name;
      PacerCapability Named{PacerCapability::NoCapabilities};
    };

    constexpr std::array<Way, 4> Ways = {{{"timer", PacerCapability::NoCapabilities},
                                          {"timer-wait", PacerCapability::WaitForPresent},
                                          {"vblank", PacerCapability::VBlankTimes},
                                          {"vblank-wait", PacerCapability::VBlankTimes | PacerCapability::WaitForPresent}}};

    //! A case: its name and what it changes of a loop with light work
    struct Case
    {
      std::string_view Name;
      void (*Apply)(LoopSettings& rSettings, int64_t period, PacerCapability named);
    };

    //! A share of a refresh period, in percent, as a range of one value
    NanosecondRange Share(const int64_t period, const int64_t percent) noexcept
    {
      return {(period * percent) / 100, (period * percent) / 100};
    }

    void Light(LoopSettings& /*rSettings*/, const int64_t /*period*/, const PacerCapability /*named*/)
    {
    }

    void Gpu90(LoopSettings& rSettings, const int64_t period, const PacerCapability /*named*/)
    {
      rSettings.GpuWork = Share(period, 90);
      rSettings.ReportsGpuWork = true;
    }

    void Gpu130(LoopSettings& rSettings, const int64_t period, const PacerCapability /*named*/)
    {
      rSettings.GpuWork = Share(period, 130);
      rSettings.ReportsGpuWork = true;
    }

    void LongFrame(LoopSettings& rSettings, const int64_t period, const PacerCapability /*named*/)
    {
      rSettings.LongFrames = {120};
      rSettings.LongFrameCpuNanoseconds = (period * 240) / 100;
    }

    void TwoRefreshesOnALateTimer(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability /*named*/)
    {
      rSettings.PreferredSwapInterval = 2;
      rSettings.TimerLate = {0, 100'000};
    }

    void SlowDisplay(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability /*named*/)
    {
      rSettings.DisplayPeriodPpm = 2'000;
    }

    void TwoInFlight(LoopSettings& rSettings, const int64_t period, const PacerCapability /*named*/)
    {
      rSettings.CpuWork = Share(period, 72);
      rSettings.GpuWork = Share(period, 72);
      rSettings.ReportsGpuWork = true;
      rSettings.MaxFramesInFlight = 2;
      rSettings.WaitsForPreviousGpuWork = false;
    }

    void HeldBlanks(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability /*named*/)
    {
      rSettings.Display.HeldBlanks = {100, 101, 102};
    }

    void AtTime(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability /*named*/)
    {
      rSettings.PresentsAtTime = true;
    }

    void DurationAtTwoRefreshes(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability /*named*/)
    {
      rSettings.PresentsAfterDuration = true;
      rSettings.PreferredSwapInterval = 2;
    }

    void SwapIntervalAtTwoRefreshes(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability /*named*/)
    {
      rSettings.MaxPresentSwapInterval = 4;
      rSettings.PreferredSwapInterval = 2;
    }

    void GpuWait(LoopSettings& rSettings, const int64_t period, const PacerCapability /*named*/)
    {
      rSettings.HasGpuWait = true;
      rSettings.GpuWork = Share(period, 130);
      rSettings.ReportsGpuWork = true;
      rSettings.MaxFramesInFlight = 2;
      rSettings.WaitsForPreviousGpuWork = false;
    }

    void DisplayReports(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability /*named*/)
    {
      rSettings.ReportsDisplayTimes = true;
    }

    void ActiveSetChanges(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability named)
    {
      // To the way of pacing that has what this one lacks and lacks what it has, and back
      const PacerCapability both = PacerCapability::VBlankTimes | PacerCapability::WaitForPresent;
      rSettings.ActiveSetChanges = {{70, Without(both, named)}, {140, named}};
    }

    void SixtyHertz(LoopSettings& rSettings, const int64_t /*period*/, const PacerCapability /*named*/)
    {
      rSettings.RateNumerator = 60;
      rSettings.GpuWork = {3'300'000, 3'300'000};
    }

    constexpr std::array<Case, 15> Cases = {{{"light", Light},
                                             {"gpu-90", Gpu90},
                                             {"gpu-130", Gpu130},
                                             {"long-frame", LongFrame},
                                             {"two-refreshes-late-timer", TwoRefreshesOnALateTimer},
                                             {"slow-display", SlowDisplay},
                                             {"two-in-flight", TwoInFlight},
                                             {"held-blanks", HeldBlanks},
                                             {"at-time", AtTime},
                                             {"duration-two-refreshes", DurationAtTwoRefreshes},
                                             {"swap-interval-two-refreshes", SwapIntervalAtTwoRefreshes},
                                             {"gpu-wait", GpuWait},
                                             {"display-reports", DisplayReports},
                                             {"active-set-changes", ActiveSetChanges},
                                             {"60-hz", SixtyHertz}}};

    void Append(std::string& rText, const int64_t value, const char after)
    {
      rText += std::to_string(value);
      rText += after;
    }

    void Append(std::string& rText, const uint64_t value, const char after)
    {
      rText += std::to_string(value);
      rText += after;
    }
  }

  std::vector<TierLoopGoldenRun> Runs()
  {
    std::vector<TierLoopGoldenRun> runs;
    for (const Way& way : Ways)
    {
      for (const PacerAim aim : {PacerAim::LowLatency, PacerAim::Smoothness})
      {
        for (const Case& item : Cases)
        {
          TierLoopGoldenRun run;
          run.Name = std::string(way.Name) + (aim == PacerAim::Smoothness ? "-smoothness-" : "-low-latency-") + std::string(item.Name);
          run.Named = way.Named;
          run.WritesFrames = item.Name == "light";
          run.Settings.Frames = Frames;
          run.Settings.Aim = aim;
          const int64_t period =
            RefreshPeriod::FromRate(run.Settings.RateNumerator, run.Settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
          item.Apply(run.Settings, period, way.Named);
          runs.push_back(run);
        }
      }
    }
    return runs;
  }

  std::string FileNameOf(const TierLoopGoldenRun& run)
  {
    return "tier-loop-" + run.Name + ".csv";
  }

  std::string ToCsv(const std::vector<LoopFrame>& frames)
  {
    std::string text =
      "frameId,waitBeginNs,waitTargetNs,startNs,workEndNs,gpuBeginNs,gpuEndNs,presentWaitBeginNs,presentWaitTargetNs,presentNs,shownNs,"
      "pendingAtStart,workCpuNs,workGpuNs,swapInterval,animationNs,animationStepNs,intendedDisplayNs,nextFrameStartNs,"
      "targetFrameTimeNs,windowFrames,windowLateFrames,gpuWaitFrameId,gpuWaitBlockedNs,activeCapabilities,refreshesBehindClock,"
      "displayJudgedFrames,displayErrorFrames,displayOffTargetFrames,displayLateFrames,displayStartToDisplayFrames,"
      "displayStartToDisplayTotalNs,displayStartToDisplayLongestNs\n";
    for (const LoopFrame& frame : frames)
    {
      Append(text, frame.FrameId, ',');
      Append(text, frame.WaitBeginNanoseconds, ',');
      Append(text, frame.WaitTargetNanoseconds, ',');
      Append(text, frame.StartNanoseconds, ',');
      Append(text, frame.WorkEndNanoseconds, ',');
      Append(text, frame.GpuBeginNanoseconds, ',');
      Append(text, frame.GpuEndNanoseconds, ',');
      Append(text, frame.PresentWaitBeginNanoseconds, ',');
      Append(text, frame.PresentWaitTargetNanoseconds, ',');
      Append(text, frame.PresentNanoseconds, ',');
      Append(text, frame.ShownNanoseconds, ',');
      Append(text, int64_t{frame.PendingAtStart}, ',');
      Append(text, frame.WorkCpuNanoseconds, ',');
      Append(text, frame.WorkGpuNanoseconds, ',');
      Append(text, uint64_t{frame.SwapInterval}, ',');
      Append(text, frame.AnimationNanoseconds, ',');
      Append(text, frame.AnimationStepNanoseconds, ',');
      Append(text, frame.IntendedDisplayNanoseconds, ',');
      Append(text, frame.NextFrameStartNanoseconds, ',');
      Append(text, frame.TargetFrameTimeNanoseconds, ',');
      Append(text, uint64_t{frame.WindowFrames}, ',');
      Append(text, uint64_t{frame.WindowLateFrames}, ',');
      Append(text, frame.GpuWaitFrameId, ',');
      Append(text, frame.GpuWaitBlockedNanoseconds, ',');
      Append(text, uint64_t{frame.ActiveCapabilities}, ',');
      Append(text, frame.RefreshesBehindClock, ',');
      Append(text, frame.DisplayJudgedFrames, ',');
      Append(text, frame.DisplayErrorFrames, ',');
      Append(text, frame.DisplayOffTargetFrames, ',');
      Append(text, frame.DisplayLateFrames, ',');
      Append(text, frame.DisplayStartToDisplayFrames, ',');
      Append(text, frame.DisplayStartToDisplayTotalNanoseconds, ',');
      Append(text, frame.DisplayStartToDisplayLongestNanoseconds, '\n');
    }
    return text;
  }

  std::string Simulate(const TierLoopGoldenRun& run)
  {
    return ToCsv(SimulateTierLoop(run.Settings, run.Named));
  }

  std::string Digests(const std::vector<TierLoopGoldenRun>& runs)
  {
    std::string text = "run,frames,bytes,crc32\n";
    for (const TierLoopGoldenRun& run : runs)
    {
      const std::string csv = Simulate(run);
      std::vector<uint8_t> bytes;
      bytes.reserve(csv.size());
      for (const char character : csv)
      {
        bytes.push_back(static_cast<uint8_t>(character));
      }
      text += run.Name;
      text += ',';
      Append(text, int64_t{run.Settings.Frames}, ',');
      Append(text, static_cast<uint64_t>(csv.size()), ',');
      Append(text, uint64_t{Crc32Util::ComputeFast(std::span<const uint8_t>(bytes))}, '\n');
    }
    return text;
  }
}
