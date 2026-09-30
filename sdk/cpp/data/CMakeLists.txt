# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The data module: reads the mb-framepacing tools' capture data (captures.mbcd) and analysis output (summary.json and the CSVs). It uses
# the marker module to decode the markers' payloads. Reading allocates and throws: it is not meant for a per-frame path.

include(FetchContent)

# JSON for summary.json, used inside the module only (header only, not part of the API). An installed copy wins.
FetchContent_Declare(nlohmann_json
  URL https://github.com/nlohmann/json/releases/download/v3.12.0/json.tar.xz
  URL_HASH SHA256=42f6e95cad6ec532fd372391373363b62a14af6d771056dbfc86160e6dfff7aa
  EXCLUDE_FROM_ALL
  SYSTEM
  FIND_PACKAGE_ARGS 3.12 CONFIG)
FetchContent_MakeAvailable(nlohmann_json)

add_library(mb_framepacing_data STATIC)
add_library(mb_framepacing::data ALIAS mb_framepacing_data)
set_target_properties(mb_framepacing_data PROPERTIES EXPORT_NAME data POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_data
  PRIVATE
    source/mb/framepacing/data/analysis/AnalysisFiles.cpp
    source/mb/framepacing/data/analysis/AnalysisSummary.cpp
    source/mb/framepacing/data/capture/CaptureDataHeader.cpp
    source/mb/framepacing/data/capture/CaptureDataReader.cpp
    source/mb/framepacing/data/capture/CaptureDataRecord.cpp
    source/mb/framepacing/data/analysis/CapturesCsv.cpp
    source/mb/framepacing/data/analysis/FramesCsv.cpp
    source/mb/framepacing/data/analysis/Milliseconds.cpp
    source/mb/framepacing/data/analysis/detail/CsvCells.hpp
    source/mb/framepacing/data/analysis/detail/CsvTable.cpp
    source/mb/framepacing/data/analysis/detail/CsvTable.hpp
    source/mb/framepacing/data/capture/detail/CaptureDataFormat.hpp
    source/mb/framepacing/data/capture/detail/LittleEndian.hpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include
    FILES
      include/mb/framepacing/data/analysis/AnalysisFiles.hpp
      include/mb/framepacing/data/analysis/AnalysisSummary.hpp
      include/mb/framepacing/data/analysis/CaptureCsvRow.hpp
      include/mb/framepacing/data/analysis/CapturesCsv.hpp
      include/mb/framepacing/data/capture/CaptureDataHeader.hpp
      include/mb/framepacing/data/capture/CaptureDataReader.hpp
      include/mb/framepacing/data/capture/CaptureDataRecord.hpp
      include/mb/framepacing/data/capture/CaptureDataStatus.hpp
      include/mb/framepacing/data/DataFormatError.hpp
      include/mb/framepacing/data/analysis/FrameRow.hpp
      include/mb/framepacing/data/analysis/FramesCsv.hpp
      include/mb/framepacing/data/capture/MarkerLocation.hpp
      include/mb/framepacing/data/analysis/Milliseconds.hpp
      include/mb/framepacing/data/analysis/OlderFrame.hpp
      include/mb/framepacing/data/analysis/SummaryCamera.hpp
      include/mb/framepacing/data/analysis/SummaryCounts.hpp
      include/mb/framepacing/data/analysis/SummaryHistogram.hpp
      include/mb/framepacing/data/analysis/SummaryHistogramBin.hpp
      include/mb/framepacing/data/analysis/SummaryHistograms.hpp
      include/mb/framepacing/data/analysis/SummaryMarker.hpp
      include/mb/framepacing/data/analysis/SummaryPacing.hpp
      include/mb/framepacing/data/analysis/SummaryRun.hpp
      include/mb/framepacing/data/analysis/SummaryStatistics.hpp
      include/mb/framepacing/data/analysis/ValueStatistics.hpp)
target_compile_features(mb_framepacing_data PUBLIC cxx_std_20)
target_link_libraries(mb_framepacing_data PUBLIC mb_framepacing::marker PRIVATE $<BUILD_INTERFACE:nlohmann_json::nlohmann_json>)
target_compile_options(mb_framepacing_data PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

install(TARGETS mb_framepacing_data EXPORT mb_framepacingTargets
  ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
  FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
# The one third-party component compiled into the module (nlohmann/json)
install(FILES ${MB_FRAMEPACING_LEGAL_DIR}/licenses/nlohmann-json-MIT.txt DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framepacing/licenses)

if(MB_FRAMEPACING_BUILD_TESTS)
  add_executable(mb_framepacing_data_tests tests/AnalysisOutputTests.cpp tests/CaptureDataTests.cpp tests/GoldenDataTests.cpp)
  target_link_libraries(mb_framepacing_data_tests PRIVATE mb_framepacing::data nlohmann_json::nlohmann_json GTest::gtest_main)
  # The tests check the private file format too (source/mb/framepacing/data/capture/detail/CaptureDataFormat.hpp)
  target_include_directories(mb_framepacing_data_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/source)
  target_compile_options(mb_framepacing_data_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  # The golden data (test-data/data) is found above this folder; a copy outside the repository skips those tests
  target_compile_definitions(mb_framepacing_data_tests PRIVATE MB_FRAMEPACING_DATA_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
  gtest_discover_tests(mb_framepacing_data_tests)
endif()
