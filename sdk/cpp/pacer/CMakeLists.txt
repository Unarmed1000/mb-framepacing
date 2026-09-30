# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The pacer module: paces frames on a grid of refreshes with an adaptive swap interval, and aligns the animation timer to the refreshes.
# No dependencies besides the core, no allocations per frame.

add_library(mb_framepacing_pacer STATIC)
add_library(mb_framepacing::pacer ALIAS mb_framepacing_pacer)
set_target_properties(mb_framepacing_pacer PROPERTIES EXPORT_NAME pacer POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_pacer
  PRIVATE
    source/mb/framepacing/pacer/animation/AnimationClock.cpp
    source/mb/framepacing/pacer/FramePacer.cpp
    source/mb/framepacing/pacer/rule/SwapIntervalRule.cpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include
    FILES
      include/mb/framepacing/pacer/animation/AnimationClock.hpp
      include/mb/framepacing/pacer/animation/AnimationTime.hpp
      include/mb/framepacing/pacer/frame/FrameEnd.hpp
      include/mb/framepacing/pacer/frame/FrameInput.hpp
      include/mb/framepacing/pacer/FramePacer.hpp
      include/mb/framepacing/pacer/frame/FrameSchedule.hpp
      include/mb/framepacing/pacer/PacerSettings.hpp
      include/mb/framepacing/pacer/RefreshPeriod.hpp
      include/mb/framepacing/pacer/rule/SlowDownRule.hpp
      include/mb/framepacing/pacer/rule/SwapIntervalChange.hpp
      include/mb/framepacing/pacer/rule/SwapIntervalRule.hpp
      include/mb/framepacing/pacer/rule/WindowState.hpp)
target_compile_features(mb_framepacing_pacer PUBLIC cxx_std_20)
target_link_libraries(mb_framepacing_pacer PUBLIC mb_framepacing::core)
target_compile_options(mb_framepacing_pacer PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

install(TARGETS mb_framepacing_pacer EXPORT mb_framepacingTargets
  ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
  FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

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

  add_executable(mb_framepacing_pacer_tests tests/PacerTests.cpp tests/AllocationTests.cpp tests/DiagramTests.cpp tests/GoldenTests.cpp)
  target_link_libraries(mb_framepacing_pacer_tests PRIVATE mb_framepacing_pacer_simulation GTest::gtest_main)
  target_compile_options(mb_framepacing_pacer_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  # The golden data (test-data/pacer) is found above this folder; a copy outside the repository skips those tests
  target_compile_definitions(mb_framepacing_pacer_tests PRIVATE MB_FRAMEPACING_PACER_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
  gtest_discover_tests(mb_framepacing_pacer_tests)
endif()
