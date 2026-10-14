# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The trace module: the frame record, what an application logs to its own trace for every frame so that the tools
# can tell a frame of the trace from a frame of a capture (sdk/doc/trace-frame-record.md). It defines the record's strings and
# works out, from a marker's payload, what to log; it writes no trace.
# No dependencies besides the marker module (and so the core), no allocations.

add_library(mb_framepacing_trace STATIC)
add_library(mb_framepacing::trace ALIAS mb_framepacing_trace)
set_target_properties(mb_framepacing_trace PROPERTIES EXPORT_NAME trace POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_trace
  PRIVATE
    source/mb/framepacing/trace/FrameRecordUtil.cpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include
    FILES
      include/mb/framepacing/trace/FrameDetail.hpp
      include/mb/framepacing/trace/FrameDetailName.hpp
      include/mb/framepacing/trace/FrameRecord.hpp
      include/mb/framepacing/trace/FrameRecordAnnotation.hpp
      include/mb/framepacing/trace/FrameRecordName.hpp
      include/mb/framepacing/trace/FrameRecordText.hpp
      include/mb/framepacing/trace/FrameRecordUtil.hpp)
target_compile_features(mb_framepacing_trace PUBLIC cxx_std_20)
target_link_libraries(mb_framepacing_trace PUBLIC mb_framepacing::marker)
target_compile_options(mb_framepacing_trace PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS} ${MB_FRAMEPACING_SECTION_FLAGS})

if(MB_FRAMEPACING_INSTALL)
  install(TARGETS mb_framepacing_trace EXPORT mb_framepacingTargets
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
endif()

if(MB_FRAMEPACING_BUILD_TESTS)
  add_executable(mb_framepacing_trace_tests tests/AllocationTests.cpp tests/FrameDetailTests.cpp tests/FrameRecordTests.cpp tests/GoldenTests.cpp)
  target_link_libraries(mb_framepacing_trace_tests PRIVATE mb_framepacing::trace mb_framepacing_test_support GTest::gtest_main)
  target_compile_options(mb_framepacing_trace_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  # The golden data (test-data/trace) is found above this folder; a copy outside the repository skips those tests
  target_compile_definitions(mb_framepacing_trace_tests PRIVATE MB_FRAMEPACING_TRACE_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
  mb_framepacing_add_x11_macro_check(mb_framepacing_trace_tests trace)
  gtest_discover_tests(mb_framepacing_trace_tests)
endif()
