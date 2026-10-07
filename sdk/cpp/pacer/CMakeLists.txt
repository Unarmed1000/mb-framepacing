# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The pacer module (EXPERIMENTAL): paces a frame loop with a steady clock and a Present that waits for vsync: an animation time in
# whole refreshes, an adaptive swap interval, a target frame rate.
# No dependencies besides the core, no allocations per frame.

add_library(mb_framepacing_pacer STATIC)
add_library(mb_framepacing::pacer ALIAS mb_framepacing_pacer)
set_target_properties(mb_framepacing_pacer PROPERTIES EXPORT_NAME pacer POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_pacer
  PRIVATE
    source/mb/framepacing/pacer/clock/PacerRefreshClock.cpp
    source/mb/framepacing/pacer/frame/FramesInFlight.cpp
    source/mb/framepacing/pacer/frame/PresentFeedback.cpp
    source/mb/framepacing/pacer/FramePacer.cpp
    source/mb/framepacing/pacer/PacerSettings.cpp
    source/mb/framepacing/pacer/RefreshPeriod.cpp
    source/mb/framepacing/pacer/RefreshTime.cpp
    source/mb/framepacing/pacer/rule/SwapIntervalRule.cpp
    source/mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.cpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include
    FILES
      include/mb/framepacing/pacer/capability/HoldTier.hpp
      include/mb/framepacing/pacer/capability/PacerCapabilities.hpp
      include/mb/framepacing/pacer/capability/PacerCapability.hpp
      include/mb/framepacing/pacer/capability/PacerRating.hpp
      include/mb/framepacing/pacer/capability/PacerTierUtil.hpp
      include/mb/framepacing/pacer/capability/QueueTier.hpp
      include/mb/framepacing/pacer/clock/PacerRefreshClock.hpp
      include/mb/framepacing/pacer/clock/AnimationTime.hpp
      include/mb/framepacing/pacer/clock/FrameMeasurement.hpp
      include/mb/framepacing/pacer/FramePacer.hpp
      include/mb/framepacing/pacer/frame/FrameSchedule.hpp
      include/mb/framepacing/pacer/frame/FrameStartPlan.hpp
      include/mb/framepacing/pacer/frame/GpuWorkReport.hpp
      include/mb/framepacing/pacer/frame/PresentPlan.hpp
      include/mb/framepacing/pacer/frame/PresentReport.hpp
      include/mb/framepacing/pacer/frame/VBlankReading.hpp
      include/mb/framepacing/pacer/frame/FramesInFlight.hpp
      include/mb/framepacing/pacer/frame/PresentFeedback.hpp
      include/mb/framepacing/pacer/frame/PresentFeedbackState.hpp
      include/mb/framepacing/pacer/frame/PresentResult.hpp
      include/mb/framepacing/pacer/PacerSettings.hpp
      include/mb/framepacing/pacer/RefreshPeriod.hpp
      include/mb/framepacing/pacer/RefreshTime.hpp
      include/mb/framepacing/pacer/rule/SlowDownRule.hpp
      include/mb/framepacing/pacer/rule/SwapIntervalChange.hpp
      include/mb/framepacing/pacer/rule/SwapIntervalRule.hpp
      include/mb/framepacing/pacer/rule/FrameWindowState.hpp
      include/mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.hpp)
target_compile_features(mb_framepacing_pacer PUBLIC cxx_std_20)
target_link_libraries(mb_framepacing_pacer PUBLIC mb_framepacing::core)
target_compile_options(mb_framepacing_pacer PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS} ${MB_FRAMEPACING_SECTION_FLAGS})

if(MB_FRAMEPACING_INSTALL)
  install(TARGETS mb_framepacing_pacer EXPORT mb_framepacingTargets
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
endif()

if(MB_FRAMEPACING_BUILD_TESTS)
  # Test code, not part of the library: the simulation of the sister repository's frame model (a frame starts when the previous one is
  # shown, and is shown at its target or at the first refresh after it is done), shared by the tests and pacer-sim, which writes the
  # golden data (python tools/update_pacer_test_data.py)
  # With it: a frame loop on a display that queues what a present gives it (DisplayModel, FrameLoopSimulation), for what the first
  # integration's logs show and that model can not
  add_library(mb_framepacing_pacer_simulation STATIC EXCLUDE_FROM_ALL tests/simulation/PacerSimulation.cpp tests/simulation/DisplayModel.cpp
                                                                      tests/simulation/FrameLoopSimulation.cpp tests/simulation/FrameLogReplay.cpp)
  target_include_directories(mb_framepacing_pacer_simulation PUBLIC tests/simulation)
  target_link_libraries(mb_framepacing_pacer_simulation PUBLIC mb_framepacing::pacer)
  target_compile_options(mb_framepacing_pacer_simulation PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

  add_executable(pacer-sim tests/pacer-sim/main.cpp)
  target_link_libraries(pacer-sim PRIVATE mb_framepacing_pacer_simulation)
  target_compile_options(pacer-sim PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

  # A frame log's frames given to the pacer again, and its answers next to what the log shows the display did
  add_executable(pacer-replay tests/pacer-replay/main.cpp)
  target_link_libraries(pacer-replay PRIVATE mb_framepacing_pacer_simulation)
  target_compile_options(pacer-replay PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

  add_executable(mb_framepacing_pacer_tests tests/PacerTests.cpp tests/AllocationTests.cpp tests/DiagramTests.cpp tests/GoldenTests.cpp tests/MonitorRateTests.cpp
                                            tests/PresentFeedbackTests.cpp tests/WorkOverTimeTests.cpp tests/StartsAheadTests.cpp
                                            tests/DisplayModelTests.cpp tests/FrameLoopTests.cpp tests/FrameLogReplayTests.cpp
                                            tests/PacerCapabilityTests.cpp tests/PacerPlanTests.cpp
                                            tests/TimerPeriodOnlyPacerTests.cpp)
  target_link_libraries(mb_framepacing_pacer_tests PRIVATE mb_framepacing_pacer_simulation mb_framepacing_test_support GTest::gtest_main)
  target_compile_options(mb_framepacing_pacer_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  # The golden data (test-data/pacer) is found above this folder; a copy outside the repository skips those tests
  target_compile_definitions(mb_framepacing_pacer_tests PRIVATE MB_FRAMEPACING_PACER_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
  mb_framepacing_add_x11_macro_check(mb_framepacing_pacer_tests pacer)
  gtest_discover_tests(mb_framepacing_pacer_tests)
endif()
