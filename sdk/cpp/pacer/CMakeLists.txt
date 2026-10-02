# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The pacer module (EXPERIMENTAL): paces a frame loop with a steady clock and a Present that waits for vsync: an animation timer in
# whole refreshes, an adaptive swap interval, a target frame rate.
# No dependencies besides the core, no allocations per frame.

add_library(mb_framepacing_pacer STATIC)
add_library(mb_framepacing::pacer ALIAS mb_framepacing_pacer)
set_target_properties(mb_framepacing_pacer PROPERTIES EXPORT_NAME pacer POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_pacer
  PRIVATE
    source/mb/framepacing/pacer/clock/PacerRefreshClock.cpp
    source/mb/framepacing/pacer/FramePacer.cpp
    source/mb/framepacing/pacer/PacerSettings.cpp
    source/mb/framepacing/pacer/RefreshPeriod.cpp
    source/mb/framepacing/pacer/RefreshTime.cpp
    source/mb/framepacing/pacer/rule/SwapIntervalRule.cpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include
    FILES
      include/mb/framepacing/pacer/clock/PacerRefreshClock.hpp
      include/mb/framepacing/pacer/clock/AnimationTime.hpp
      include/mb/framepacing/pacer/clock/FrameMeasurement.hpp
      include/mb/framepacing/pacer/FramePacer.hpp
      include/mb/framepacing/pacer/frame/FrameSchedule.hpp
      include/mb/framepacing/pacer/PacerSettings.hpp
      include/mb/framepacing/pacer/RefreshPeriod.hpp
      include/mb/framepacing/pacer/RefreshTime.hpp
      include/mb/framepacing/pacer/rule/SlowDownRule.hpp
      include/mb/framepacing/pacer/rule/SwapIntervalChange.hpp
      include/mb/framepacing/pacer/rule/SwapIntervalRule.hpp
      include/mb/framepacing/pacer/rule/FrameWindowState.hpp)
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
  add_library(mb_framepacing_pacer_simulation STATIC EXCLUDE_FROM_ALL tests/simulation/PacerSimulation.cpp)
  target_include_directories(mb_framepacing_pacer_simulation PUBLIC tests/simulation)
  target_link_libraries(mb_framepacing_pacer_simulation PUBLIC mb_framepacing::pacer)
  target_compile_options(mb_framepacing_pacer_simulation PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

  add_executable(pacer-sim tests/pacer-sim/main.cpp)
  target_link_libraries(pacer-sim PRIVATE mb_framepacing_pacer_simulation)
  target_compile_options(pacer-sim PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

  add_executable(mb_framepacing_pacer_tests tests/PacerTests.cpp tests/AllocationTests.cpp tests/DiagramTests.cpp tests/GoldenTests.cpp tests/MonitorRateTests.cpp)
  target_link_libraries(mb_framepacing_pacer_tests PRIVATE mb_framepacing_pacer_simulation mb_framepacing_test_support GTest::gtest_main)
  target_compile_options(mb_framepacing_pacer_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  # The golden data (test-data/pacer) is found above this folder; a copy outside the repository skips those tests
  target_compile_definitions(mb_framepacing_pacer_tests PRIVATE MB_FRAMEPACING_PACER_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
  gtest_discover_tests(mb_framepacing_pacer_tests)
endif()
